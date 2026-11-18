using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using P = (double X, double Y);
using P3 = (double X, double Y, double Z);

[assembly: CommandClass(typeof(PVLocal.Commands))]

namespace PVLocal;

/// PV Local commands. Workflow (same order as the ribbon):
///   Site:       PVAREA, PVEXCLUDE, PVMESH (terrain from the drawing), PVTERRAIN (slope check)
///   Design:     PVSETTINGS, PVLAYOUT (structures + piles), PVCOMPARE
///   Electrical: PVDC, PVAC, PVMV, PVBOM (each reads the trackers in the drawing, so manual edits are respected)
///   PVPLANT = everything; PVCLEAR = erase generated objects
public class Commands
{
    const string BoundaryLayer = Drawing.BoundaryLayer, ExclusionLayer = Drawing.ExclusionLayer, SettingsKey = "PVLOCAL_SETTINGS";
    static readonly string[] ElectricalSteps = ["dc", "ac", "mv"];

    // ------------------------------------------------ settings stored in the DWG (travel with the project)
    static Settings LoadSettings(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(SettingsKey)) return new Settings();
        var xr = (Xrecord)tr.GetObject(nod.GetAt(SettingsKey), OpenMode.ForRead);
        var xml = string.Concat(xr.Data.AsArray().Select(v => (string)v.Value));
        try { return Settings.FromXml(xml); }
        catch (InvalidOperationException) { return new Settings(); }   // unreadable/old format -> defaults
    }

    static void SaveSettings(Database db, Settings s)
    {
        var xml = s.ToXml();
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        Xrecord xr;
        if (nod.Contains(SettingsKey)) xr = (Xrecord)tr.GetObject(nod.GetAt(SettingsKey), OpenMode.ForWrite);
        else { xr = new Xrecord(); nod.SetAt(SettingsKey, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        // xrecord strings are length-limited: store in 200-char chunks
        xr.Data = new ResultBuffer(Enumerable.Range(0, (xml.Length + 199) / 200)
            .Select(i => new TypedValue((int)DxfCode.Text, xml.Substring(i * 200, Math.Min(200, xml.Length - i * 200)))).ToArray());
        tr.Commit();
    }

    /// Per-command context: document, settings (auto-stringed), terrain.
    sealed class Ctx
    {
        public Database Db = null!; public Editor Ed = null!; public Settings S = null!; public Terrain? Terrain;
        public P? Substation => S.UseSubstationPoint ? (S.SubstationX, S.SubstationY) : null;
    }

    static Ctx? Begin()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var c = new Ctx { Db = doc.Database, Ed = doc.Editor, S = LoadSettings(doc.Database) };
        if (Engine.AutoString(c.S) is { } note) { SaveSettings(c.Db, c.S); c.Ed.WriteMessage($"\nAuto-string: {note}"); }
        try { c.Terrain = c.S.TerrainFile != "" ? Terrain.LoadAsc(c.S.TerrainFile) : null; }
        catch (System.Exception ex) { c.Ed.WriteMessage($"\nCannot read terrain {c.S.TerrainFile}: {ex.Message}\n"); return null; }
        return c;
    }

    // ------------------------------------------------ SITE
    [CommandMethod("PVAREA", CommandFlags.Modal)]
    public void PvArea() => MoveToLayer(BoundaryLayer, "site area (closed polylines)");

    [CommandMethod("PVEXCLUDE", CommandFlags.Modal)]
    public void PvExclude() => MoveToLayer(ExclusionLayer, "exclusion zones: roads, wetlands, buildings, buffers (closed polylines)");

    static void MoveToLayer(string layer, string what)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var sel = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = $"\nSelect {what}: " },
            new SelectionFilter([new TypedValue((int)DxfCode.Start, "LWPOLYLINE")]));
        if (sel.Status != PromptStatus.OK) return;
        using var tr = doc.Database.TransactionManager.StartTransaction();
        EnsureLayers(tr, doc.Database);
        int n = 0;
        foreach (var id in sel.Value.GetObjectIds())
        {
            var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
            if (!pl.Closed && pl.NumberOfVertices > 2) pl.Closed = true;
            pl.Layer = layer; n++;
        }
        tr.Commit();
        doc.Editor.WriteMessage($"\n{n} polyline(s) set as {(layer == BoundaryLayer ? "site area" : "exclusions")} (layer {layer}).\n");
    }

    /// Terrain from the drawing: 3D faces, polyface/sub-D meshes, points, survey blocks, 3D polylines, contours with elevation.
    [CommandMethod("PVMESH", CommandFlags.Modal)]
    public void PvMesh()
    {
        if (Begin() is not { } c) return;
        var sel = c.Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect terrain: 3D faces, meshes, points, survey blocks, 3D polylines or contours: " });
        if (sel.Status != PromptStatus.OK) return;
        var tris = new List<P3[]>(); var pts = new List<P3>();
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            foreach (var id in sel.Value.GetObjectIds()) Collect(tr, tr.GetObject(id, OpenMode.ForRead), tris, pts);
            var site = Site(tr, c.Db);
            // grid over the site (+20 m) when there is one, else over the selection
            var all = site.Boundaries.Count > 0 ? site.Boundaries.SelectMany(b => b).ToList() : pts.Concat(tris.SelectMany(t => t)).Select(p => (p.X, p.Y)).ToList();
            if (all.Count == 0) { c.Ed.WriteMessage("\nNothing with elevation found in the selection.\n"); return; }
            double m = site.Boundaries.Count > 0 ? 20 : 0;
            var t = Terrain.FromSamples(tris, pts, all.Min(p => p.X) - m, all.Min(p => p.Y) - m, all.Max(p => p.X) + m, all.Max(p => p.Y) + m, c.S.TerrainCell);
            var path = Path.Combine(OutDir(c.Db), Stem(c.Db) + "_terrain.asc");
            t.SaveAsc(path);
            c.S.TerrainFile = path;
            SaveSettings(c.Db, c.S);
            double zmin = double.MaxValue, zmax = double.MinValue;
            foreach (var z in t.Z) { zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z); }
            c.Ed.WriteMessage($"\nTerrain: {tris.Count} triangles, {pts.Count} points -> {t.Cols}x{t.Rows} grid @ {c.S.TerrainCell} m, z {zmin:0.00}..{zmax:0.00} m" +
                              $"\nSaved {path} and set as TerrainFile. Run PVTERRAIN to check slopes, PVLAYOUT to place piles on it.\n");
            tr.Commit();
        }
    }

    static void Collect(Transaction tr, DBObject o, List<P3[]> tris, List<P3> pts)
    {
        static P3 V(Point3d p) => (p.X, p.Y, p.Z);
        switch (o)
        {
            case Face f:
                var q = Enumerable.Range(0, 4).Select(i => V(f.GetVertexAt((short)i))).ToArray();
                tris.Add([q[0], q[1], q[2]]);
                if (q[3] != q[2]) tris.Add([q[0], q[2], q[3]]);
                break;
            case PolyFaceMesh pm:
                var verts = new List<P3>();
                foreach (ObjectId id in pm)
                {
                    var sub = tr.GetObject(id, OpenMode.ForRead);
                    if (sub is PolyFaceMeshVertex v) verts.Add(V(v.Position));
                    else if (sub is FaceRecord fr)
                    {
                        var idx = Enumerable.Range(0, 4).Select(i => Math.Abs((int)fr.GetVertexAt((short)i)) - 1).Where(i => i >= 0 && i < verts.Count).ToList();
                        for (int k = 1; k + 1 < idx.Count; k++) tris.Add([verts[idx[0]], verts[idx[k]], verts[idx[k + 1]]]);
                    }
                }
                break;
            case SubDMesh sd:
                var mv = sd.Vertices; var fa = sd.FaceArray;
                for (int i = 0; i < fa.Count;)
                {
                    int n = fa[i];
                    for (int k = 1; k + 1 < n; k++) tris.Add([V(mv[fa[i + 1]]), V(mv[fa[i + 1 + k]]), V(mv[fa[i + 2 + k]])]);
                    i += n + 1;
                }
                break;
            case DBPoint p: pts.Add(V(p.Position)); break;
            case BlockReference br: if (br.Position.Z != 0) pts.Add(V(br.Position)); break;   // survey point blocks
            case Polyline3d p3:
                foreach (ObjectId id in p3) if (tr.GetObject(id, OpenMode.ForRead) is PolylineVertex3d pv) pts.Add(V(pv.Position));
                break;
            case Polyline pl when pl.Elevation != 0:   // contour
                for (int i = 0; i < pl.NumberOfVertices; i++) { var v2 = pl.GetPoint2dAt(i); pts.Add((v2.X, v2.Y, pl.Elevation)); }
                break;
            case Line ln when ln.StartPoint.Z != 0 || ln.EndPoint.Z != 0: pts.Add(V(ln.StartPoint)); pts.Add(V(ln.EndPoint)); break;
        }
    }

    const double TerrainCheckCell = 5;   // ponytail: fixed 5 m map cell; make it a setting if users need finer maps

    [CommandMethod("PVTERRAIN", CommandFlags.Modal)]
    public void PvTerrain()
    {
        if (Begin() is not { } c) return;
        if (c.Terrain == null) { c.Ed.WriteMessage("\nNo terrain: run PVMESH (Fetch mesh) or set TerrainFile in PVSETTINGS.\n"); return; }
        Clear(c.Db, [Drawing.TerrainLayer]);
        using var tr = c.Db.TransactionManager.StartTransaction();
        var site = Site(tr, c.Db);
        if (site.Boundaries.Count == 0) { c.Ed.WriteMessage($"\nNo site area: run PVAREA first.\n"); return; }
        var canvas = Canvas(tr, c.Db);
        var cells = site.Boundaries.SelectMany(b => Engine.TerrainCheck(c.Terrain, b, TerrainCheckCell, c.S)).ToList();
        var pts = site.Boundaries.SelectMany(b => b).ToList();
        var (ok, warn, bad) = Drawing.TerrainCheck(canvas, cells, c.S, pts.Max(p => p.X) + 20, pts.Max(p => p.Y));
        tr.Commit();
        c.Ed.WriteMessage($"\nTerrain check ({TerrainCheckCell} m cells, limit {c.S.MaxUnitSlopePct}%): {ok:0}% easy, {warn:0}% near limit, {bad:0}% too steep (red).\n");
    }

    // ------------------------------------------------ DESIGN
    [CommandMethod("PVSETTINGS", CommandFlags.Modal)]
    public void PvSettings()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var s = LoadSettings(doc.Database);
        using var form = new System.Windows.Forms.Form
        {
            Text = "PV Local - design settings (saved in this drawing)", Width = 480, Height = 700,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        };
        var grid = new System.Windows.Forms.PropertyGrid { Dock = System.Windows.Forms.DockStyle.Fill, SelectedObject = s };
        var ok = new System.Windows.Forms.Button { Text = "OK", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 32, DialogResult = System.Windows.Forms.DialogResult.OK };
        form.Controls.Add(grid); form.Controls.Add(ok); form.AcceptButton = ok;
        if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        using (doc.LockDocument()) SaveSettings(doc.Database, s);
        doc.Editor.WriteMessage("\nPV settings saved in drawing.\n");
    }

    [CommandMethod("PVLAYOUT", CommandFlags.Modal)]
    public void PvLayout() => Layout(askBoundary: true);

    static bool Layout(bool askBoundary)
    {
        if (Begin() is not { } c) return false;
        PromptSelectionResult? sel = null;
        if (askBoundary)
        {
            sel = c.Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = $"\nSelect site area polylines <Enter = all on layer {BoundaryLayer}>: " },
                new SelectionFilter([new TypedValue((int)DxfCode.Start, "LWPOLYLINE")]));
            if (sel.Status == PromptStatus.Cancel) return false;
        }
        Clear(c.Db, Drawing.LayersFrom("layout"));
        using var tr = c.Db.TransactionManager.StartTransaction();
        var site = Site(tr, c.Db);
        var boundaries = sel?.Status == PromptStatus.OK
            ? sel.Value.GetObjectIds().Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).Where(p => p.Count >= 3).ToList()
            : site.Boundaries;
        if (boundaries.Count == 0) { c.Ed.WriteMessage($"\nNo site area: select closed polylines or run PVAREA.\n"); return false; }
        var canvas = Canvas(tr, c.Db);
        var all = new Design();
        foreach (var b in boundaries)
        {
            var d = Engine.Layout(b, site.Exclusions, c.S, c.Terrain);
            Drawing.Mechanical(canvas, d, c.S);
            all.Units.AddRange(d.Units); all.Warnings.AddRange(d.Warnings);
        }
        tr.Commit();
        int strings = all.Units.Sum(u => u.Strings);
        c.Ed.WriteMessage($"\nLayout: {all.Units.Count} {(c.S.Mount == MountType.Tracker ? "trackers" : "tables")}, {strings} strings, " +
                          $"{strings * c.S.ModulesPerString * c.S.ModulePowerW / 1e6:0.000} MWp, {all.Units.Sum(u => u.Piles.Count)} piles" +
                          $"{(c.Terrain == null ? " (flat ground: run PVMESH for terrain)" : "")}");
        foreach (var w in all.Warnings.Distinct()) c.Ed.WriteMessage($"\n  WARNING: {w}");
        c.Ed.WriteMessage("\nNext: DC cables (PVDC), AC cables (PVAC), MV cables (PVMV), BOM (PVBOM).\n");
        return true;
    }

    [CommandMethod("PVCOMPARE", CommandFlags.Modal)]
    public void PvCompare()
    {
        if (Begin() is not { } c) return;
        var r = c.Ed.GetString(new PromptStringOptions("\nGCR values to compare <0.30,0.35,0.40,0.45>: ") { AllowSpaces = false });
        if (r.Status == PromptStatus.Cancel) return;
        var txt = string.IsNullOrWhiteSpace(r.StringResult) ? "0.30,0.35,0.40,0.45" : r.StringResult;
        var gcrs = txt.Split(',', ';').Select(v => double.Parse(v.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        (List<List<P>> Boundaries, List<List<P>> Exclusions) site;
        using (var tr = c.Db.TransactionManager.StartTransaction()) site = Site(tr, c.Db);
        if (site.Boundaries.Count == 0) { c.Ed.WriteMessage("\nNo site area: run PVAREA first.\n"); return; }
        var weather = Weather(c);
        c.Ed.WriteMessage($"\n  GCR   units      kWp      MWh/yr  kWh/kWp{(weather.ClearSky ? "   (clear-sky: set WeatherFile for P50)" : "")}");
        foreach (var b in site.Boundaries)
            foreach (var k in Engine.Compare(b, site.Exclusions, c.S, gcrs, c.Terrain, weather))
                c.Ed.WriteMessage($"\n  {k.Gcr:0.00} {k.Units,7} {k.Kwp,9:0} {k.Mwh,11:0} {k.SpecYield,8:0}");
        c.Ed.WriteMessage("\n");
    }

    // ------------------------------------------------ ELECTRICAL
    [CommandMethod("PVDC", CommandFlags.Modal)]
    public void PvDc() => Electrical("dc", report: false);

    [CommandMethod("PVAC", CommandFlags.Modal)]
    public void PvAc() => Electrical("ac", report: false);

    [CommandMethod("PVMV", CommandFlags.Modal)]
    public void PvMv()
    {
        if (Begin() is not { } c) return;
        var pt = c.Ed.GetPoint(new PromptPointOptions($"\nSubstation location <Enter = {(c.S.UseSubstationPoint ? "keep current" : "auto")}>: ") { AllowNone = true });
        if (pt.Status == PromptStatus.Cancel) return;
        if (pt.Status == PromptStatus.OK)
        {
            c.S.UseSubstationPoint = true; c.S.SubstationX = pt.Value.X; c.S.SubstationY = pt.Value.Y;
            SaveSettings(c.Db, c.S);
            Clear(c.Db, Drawing.LayersFrom("mv"));   // substation moved: redo MV
        }
        Electrical("mv", report: false);
    }

    [CommandMethod("PVBOM", CommandFlags.Modal)]
    public void PvBom() => Electrical("mv", report: true);

    [CommandMethod("PVPLANT", CommandFlags.Modal)]
    public void PvPlant()
    {
        if (!Layout(askBoundary: false)) return;
        Electrical("mv", report: true);
    }

    /// Wires the trackers that are in the drawing. Steps already drawn are kept; missing or requested ones are (re)drawn.
    static void Electrical(string upTo, bool report)
    {
        if (Begin() is not { } c) return;
        Design d;
        (List<List<P>> Boundaries, List<List<P>> Exclusions) site;
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            site = Site(tr, c.Db);
            var rects = OnLayer(tr, c.Db, "PV_UNITS").Select(id => Rect((Polyline)tr.GetObject(id, OpenMode.ForRead))).ToList();
            if (rects.Count == 0) { c.Ed.WriteMessage("\nNo trackers/tables in the drawing: run PVLAYOUT first.\n"); return; }
            d = Engine.FromRects(rects, c.S, c.Terrain);
        }
        Engine.Wire(d, site.Exclusions, c.S, c.Substation);

        // redraw from the first step that is missing (or the requested one); later steps are stale then
        int last = Array.IndexOf(ElectricalSteps, upTo);
        int from = last;
        for (int i = 0; i < last; i++) if (!Drawn(c.Db, ElectricalSteps[i])) { from = i; break; }
        Clear(c.Db, Drawing.LayersFrom(ElectricalSteps[from]));
        using (var tr = c.Db.TransactionManager.StartTransaction())
        {
            var canvas = Canvas(tr, c.Db);
            for (int i = from; i <= last; i++)
                (ElectricalSteps[i] switch { "dc" => (Action<ICanvas, Design, Settings>)Drawing.Dc, "ac" => Drawing.Ac, _ => Drawing.Mv })(canvas, d, c.S);
            if (report) Report(c, tr, d, site.Boundaries, canvas);
            tr.Commit();
        }

        var msg = upTo switch
        {
            "dc" => $"DC: {d.Inverters.Count} inverters x {c.S.InverterKw} kW, {d.Units.Sum(u => Engine.DcCableLen(u, c.S)):#,0} m string cable, max drop {d.Units.Max(u => u.DcDropPct):0.00}%",
            "ac" => $"AC: {d.Stations.Count} transformer stations, {d.Inverters.Sum(i => Engine.RouteLen(i.AcRoute)) * c.S.CableSlack:#,0} m LV cable, max DC+AC drop {Engine.MaxTotalDrop(d):0.00}%",
            _ => $"MV: {d.Stations.Count} feeders to substation, {d.Stations.Sum(t => Engine.RouteLen(t.MvRoute)) * c.S.CableSlack:#,0} m MV cable, max MV drop {d.Stations.Select(t => t.MvDropPct).DefaultIfEmpty().Max():0.00}%, trench {d.TrenchLength:#,0} m",
        };
        if (!report) c.Ed.WriteMessage($"\n{msg}");
        foreach (var w in d.Warnings.Distinct()) c.Ed.WriteMessage($"\n  WARNING: {w}");
        c.Ed.WriteMessage("\n");
    }

    static void Report(Ctx c, Transaction tr, Design d, List<List<P>> boundaries, ICanvas canvas)
    {
        var y = Yield.Run(d, c.S, Weather(c));
        var bom = Engine.Bom(d, c.S, y);
        var pts = boundaries.SelectMany(b => b).Concat(d.Units.SelectMany(u => new P[] { (u.X1, u.Y1) })).ToList();
        double right = pts.Max(p => p.X), top = pts.Max(p => p.Y);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(c.Db), OpenMode.ForWrite);
        DrawBomTable(tr, ms, c.Db, bom, new Point3d(right + 30, top, 0));
        Drawing.Sld(canvas, d, c.S, right + 160, top);
        c.Ed.WriteMessage("\n== PV plant");
        foreach (var (item, qty, unit) in bom) c.Ed.WriteMessage($"\n  {item,-52} {qty,12:#,0.##} {unit}");
        var csv = Path.Combine(OutDir(c.Db), Stem(c.Db) + "_bom.csv");
        Engine.WriteCsv(csv, bom);
        c.Ed.WriteMessage($"\nBOM saved: {csv}");
    }

    static Weather Weather(Ctx c) =>
        c.S.WeatherFile != "" ? PVLocal.Weather.LoadPvgisTmy(c.S.WeatherFile) : PVLocal.Weather.ClearSkyYear(c.S.Latitude, c.S.Longitude);

    [CommandMethod("PVCLEAR", CommandFlags.Modal)]
    public void PvClear()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        int n = Clear(doc.Database, Drawing.Generated.Keys);
        doc.Editor.WriteMessage($"\nErased {n} generated PV objects (site area and exclusions kept).\n");
    }

    // ------------------------------------------------ helpers
    static (List<List<P>> Boundaries, List<List<P>> Exclusions) Site(Transaction tr, Database db) =>
        (OnLayer(tr, db, BoundaryLayer).Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).Where(p => p.Count >= 3).ToList(),
         OnLayer(tr, db, ExclusionLayer).Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).Where(p => p.Count >= 3).ToList());

    static List<ObjectId> OnLayer(Transaction tr, Database db, string layer)
    {
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        return ms.Cast<ObjectId>().Where(id => id.ObjectClass.DxfName == "LWPOLYLINE")
                 .Where(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    static bool Drawn(Database db, string step)
    {
        var layers = Drawing.Steps.First(s => s.Step == step).Layers.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        return ms.Cast<ObjectId>().Any(id => layers.Contains(((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer));
    }

    static (double X0, double Y0, double X1, double Y1) Rect(Polyline pl)
    {
        var v = Vertices(pl);
        return (v.Min(p => p.X), v.Min(p => p.Y), v.Max(p => p.X), v.Max(p => p.Y));
    }

    // ponytail: arc segments (bulges) are treated as straight chords
    static List<P> Vertices(Polyline pl)
    {
        var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => { var v = pl.GetPoint2dAt(i); return (v.X, v.Y); }).ToList();
        if (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    static int Clear(Database db, IEnumerable<string> layers)
    {
        var set = layers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
            if (set.Contains(e.Layer)) { e.UpgradeOpen(); e.Erase(); n++; }
        }
        tr.Commit();
        return n;
    }

    static AcadCanvas Canvas(Transaction tr, Database db)
    {
        EnsureLayers(tr, db);
        return new AcadCanvas(tr, (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite));
    }

    static void EnsureLayers(Transaction tr, Database db)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (name, color) in Drawing.Generated.Append(new(BoundaryLayer, 3)).Append(new(ExclusionLayer, 1)))
        {
            if (lt.Has(name)) continue;
            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
        }
    }

    static void DrawBomTable(Transaction tr, BlockTableRecord ms, Database db, List<(string Item, double Qty, string UnitName)> bom, Point3d at)
    {
        var t = new Table { TableStyle = db.Tablestyle, Position = at };
        t.SetSize(bom.Count + 2, 3);
        t.SetRowHeight(4); t.Columns[0].Width = 80; t.Columns[1].Width = 25; t.Columns[2].Width = 14;
        t.Cells[0, 0].TextString = "PV BILL OF MATERIALS";
        string[] head = ["Item", "Qty", "Unit"];
        for (int c = 0; c < 3; c++) t.Cells[1, c].TextString = head[c];
        for (int r = 0; r < bom.Count; r++)
        {
            t.Cells[r + 2, 0].TextString = bom[r].Item;
            t.Cells[r + 2, 1].TextString = bom[r].Qty.ToString("#,0.##");
            t.Cells[r + 2, 2].TextString = bom[r].UnitName;
            t.Cells[r + 2, 0].Alignment = CellAlignment.MiddleLeft;
            t.Cells[r + 2, 1].Alignment = CellAlignment.MiddleRight;
            t.Cells[r + 2, 2].Alignment = CellAlignment.MiddleCenter;
        }
        for (int r = 0; r < bom.Count + 2; r++) for (int c = 0; c < 3; c++) t.Cells[r, c].TextHeight = 1.8;
        t.Layer = "PV_BOM";
        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
    }

    static string Stem(Database db) => Saved(db) ? Path.GetFileNameWithoutExtension(db.Filename) : "pvlayout";
    static string OutDir(Database db) => Saved(db) ? Path.GetDirectoryName(db.Filename)! : Path.GetTempPath();
    static bool Saved(Database db) =>
        !string.IsNullOrEmpty(db.Filename) && File.Exists(db.Filename) && !db.Filename.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase);
}

/// Draws straight into the AutoCAD database.
class AcadCanvas(Transaction tr, BlockTableRecord ms) : ICanvas
{
    void Add(Entity e, string layer) { e.Layer = layer; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }

    public void Pline(IEnumerable<P> pts, bool closed, string layer, double width = 0)
    {
        var pl = new Polyline(); int i = 0;
        foreach (var (x, y) in pts) pl.AddVertexAt(i++, new Point2d(x, y), 0, 0, 0);
        if (i < 2) { pl.Dispose(); return; }
        pl.Closed = closed;
        if (width > 0) pl.ConstantWidth = width;
        Add(pl, layer);
    }
    public void Circle(double x, double y, double z, double r, string layer) =>
        Add(new Circle(new Point3d(x, y, z), Vector3d.ZAxis, r), layer);
    public void Line(double x1, double y1, double z1, double x2, double y2, double z2, string layer) =>
        Add(new Line(new Point3d(x1, y1, z1), new Point3d(x2, y2, z2)), layer);

    public void Text(double x, double y, double h, string text, string layer, double rotation = 0, TextAlign align = TextAlign.Left)
    {
        var p = new Point3d(x, y, 0);
        var t = new DBText { Position = p, Height = h, TextString = text, Rotation = rotation * Math.PI / 180 };
        if (align != TextAlign.Left)
        {
            t.HorizontalMode = align switch { TextAlign.MiddleCenter => TextHorizontalMode.TextCenter, TextAlign.MiddleRight => TextHorizontalMode.TextRight, _ => TextHorizontalMode.TextLeft };
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = p;
        }
        Add(t, layer);
        if (align != TextAlign.Left) t.AdjustAlignment(ms.Database);
    }

    public void Fill(P[] q, short color, string layer)
    {
        Point3d V(int i) => new(q[i].X, q[i].Y, 0);
        Add(new Solid(V(0), V(1), V(3), V(2)) { ColorIndex = color }, layer);   // SOLID corner order: 1, 2, 4, 3
    }
}
