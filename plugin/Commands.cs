using System.Text;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using P = (double X, double Y);

[assembly: CommandClass(typeof(PVLocal.Commands))]

namespace PVLocal;

public class Commands
{
    const string BoundaryLayer = Drawing.BoundaryLayer, ExclusionLayer = Drawing.ExclusionLayer, SettingsKey = "PVLOCAL_SETTINGS";

    static Dictionary<string, short> Generated => Drawing.Generated;

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

    // ------------------------------------------------ PVSETTINGS
    [CommandMethod("PVSETTINGS", CommandFlags.Modal)]
    public void PvSettings()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var s = LoadSettings(doc.Database);
        using var form = new System.Windows.Forms.Form
        {
            Text = "PV Local - design settings (saved in this drawing)", Width = 460, Height = 640,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        };
        var grid = new System.Windows.Forms.PropertyGrid { Dock = System.Windows.Forms.DockStyle.Fill, SelectedObject = s };
        var ok = new System.Windows.Forms.Button { Text = "OK", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 32, DialogResult = System.Windows.Forms.DialogResult.OK };
        form.Controls.Add(grid); form.Controls.Add(ok); form.AcceptButton = ok;
        if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        using (doc.LockDocument()) SaveSettings(doc.Database, s);
        doc.Editor.WriteMessage("\nPV settings saved in drawing. Run PVLAYOUT.\n");
    }

    // ------------------------------------------------ PVLAYOUT
    [CommandMethod("PVLAYOUT", CommandFlags.Modal)]
    public void PvLayout()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database; Editor ed = doc.Editor;
        var s = LoadSettings(db);
        if (Engine.AutoString(s) is { } note) { SaveSettings(db, s); ed.WriteMessage($"\nAuto-string: {note}"); }
        Terrain? terrain; Weather weather;
        try
        {
            terrain = s.TerrainFile != "" ? Terrain.LoadAsc(s.TerrainFile) : null;
            weather = s.WeatherFile != "" ? Weather.LoadPvgisTmy(s.WeatherFile) : Weather.ClearSkyYear(s.Latitude, s.Longitude);
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nCannot read terrain/weather file: {ex.Message}\n"); return; }

        var opts = new PromptSelectionOptions { MessageForAdding = $"\nSelect site boundary polylines <Enter = all on layer {BoundaryLayer}>: " };
        var sel = ed.GetSelection(opts, new SelectionFilter([new TypedValue((int)DxfCode.Start, "LWPOLYLINE")]));
        if (sel.Status == PromptStatus.Cancel) return;
        var subPt = ed.GetPoint(new PromptPointOptions("\nSubstation location <Enter = auto>: ") { AllowNone = true });
        if (subPt.Status == PromptStatus.Cancel) return;
        P? substation = subPt.Status == PromptStatus.OK ? (subPt.Value.X, subPt.Value.Y) : null;

        ClearGenerated(db);
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        var boundaryIds = sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds().ToList() : OnLayer(tr, ms, BoundaryLayer);
        var boundaries = boundaryIds.Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).Where(p => p.Count >= 3).ToList();
        if (boundaries.Count == 0) { ed.WriteMessage($"\nNo boundary: select closed polylines or put them on layer {BoundaryLayer}.\n"); return; }
        var exclusions = OnLayer(tr, ms, ExclusionLayer)
            .Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).Where(p => p.Count >= 3).ToList();

        EnsureLayers(tr, db);
        var all = new Design();
        var designs = boundaries.Select(b => Engine.Run(b, exclusions, s, substation, terrain)).ToList();
        foreach (var d in designs)
        {
            all.Units.AddRange(d.Units); all.Inverters.AddRange(d.Inverters);
            all.Warnings.AddRange(d.Warnings); all.TrenchLength += d.TrenchLength;
        }
        // renumber inverters across all boundaries (before drawing labels)
        for (int i = 0; i < all.Inverters.Count; i++) { all.Inverters[i].Id = $"INV-{i + 1:00}"; foreach (var u in all.Inverters[i].Units) u.Inverter = all.Inverters[i].Id; }
        var canvas = new AcadCanvas(tr, ms);
        foreach (var d in designs) Drawing.Layout(canvas, d, s);

        var y = Yield.Run(all, s, weather);
        var bom = Engine.Bom(all, s, y);
        double right = boundaries.SelectMany(p => p).Max(p => p.X), top = boundaries.SelectMany(p => p).Max(p => p.Y);
        DrawBomTable(tr, ms, db, bom, new Point3d(right + 30, top, 0));
        Drawing.Sld(canvas, all, s, right + 160, top);
        tr.Commit();

        ed.WriteMessage("\n== PV layout");
        foreach (var (item, qty, unit) in bom) ed.WriteMessage($"\n  {item,-48} {qty,12:#,0.##} {unit}");
        var csv = CsvPath(db);
        Engine.WriteCsv(csv, bom);
        ed.WriteMessage($"\nBOM saved: {csv}\n");
    }

    // ------------------------------------------------ PVCOMPARE
    [CommandMethod("PVCOMPARE", CommandFlags.Modal)]
    public void PvCompare()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database; Editor ed = doc.Editor;
        var s = LoadSettings(db);
        Engine.AutoString(s);
        var r = ed.GetString(new PromptStringOptions("\nGCR values to compare <0.30,0.35,0.40,0.45>: ") { AllowSpaces = false });
        if (r.Status == PromptStatus.Cancel) return;
        var txt = string.IsNullOrWhiteSpace(r.StringResult) ? "0.30,0.35,0.40,0.45" : r.StringResult;
        var gcrs = txt.Split(',', ';').Select(v => double.Parse(v.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        List<List<P>> bounds, excl;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            bounds = OnLayer(tr, ms, BoundaryLayer).Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).ToList();
            excl = OnLayer(tr, ms, ExclusionLayer).Select(id => Vertices((Polyline)tr.GetObject(id, OpenMode.ForRead))).ToList();
        }
        if (bounds.Count == 0) { ed.WriteMessage($"\nPVCOMPARE uses polylines on layer {BoundaryLayer}.\n"); return; }
        var terrain = s.TerrainFile != "" ? Terrain.LoadAsc(s.TerrainFile) : null;
        var weather = s.WeatherFile != "" ? Weather.LoadPvgisTmy(s.WeatherFile) : Weather.ClearSkyYear(s.Latitude, s.Longitude);
        ed.WriteMessage($"\n  GCR   units      kWp      MWh/yr  kWh/kWp{(weather.ClearSky ? "   (clear-sky: set WeatherFile for P50)" : "")}");
        foreach (var b in bounds)
            foreach (var c in Engine.Compare(b, excl, s, gcrs, terrain, weather))
                ed.WriteMessage($"\n  {c.Gcr:0.00} {c.Units,7} {c.Kwp,9:0} {c.Mwh,11:0} {c.SpecYield,8:0}");
        ed.WriteMessage("\n");
    }

    // ------------------------------------------------ PVCLEAR
    [CommandMethod("PVCLEAR", CommandFlags.Modal)]
    public void PvClear()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        int n = ClearGenerated(doc.Database);
        doc.Editor.WriteMessage($"\nErased {n} generated PV objects (boundary/exclusions kept).\n");
    }

    // ------------------------------------------------ helpers
    static List<ObjectId> OnLayer(Transaction tr, BlockTableRecord ms, string layer) =>
        ms.Cast<ObjectId>().Where(id => id.ObjectClass.DxfName == "LWPOLYLINE")
          .Where(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)).ToList();

    // ponytail: arc segments (bulges) are treated as straight chords
    static List<P> Vertices(Polyline pl)
    {
        var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => { var v = pl.GetPoint2dAt(i); return (v.X, v.Y); }).ToList();
        if (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    static int ClearGenerated(Database db)
    {
        int n = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
            if (Generated.ContainsKey(e.Layer.ToUpperInvariant())) { e.UpgradeOpen(); e.Erase(); n++; }
        }
        tr.Commit();
        return n;
    }

    static void EnsureLayers(Transaction tr, Database db)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (name, color) in Generated.Append(new(BoundaryLayer, 3)).Append(new(ExclusionLayer, 1)))
        {
            if (lt.Has(name)) continue;
            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
        }
    }

    static void Add(Transaction tr, BlockTableRecord ms, Entity e, string layer)
    {
        e.Layer = layer; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
    }

    static void DrawBomTable(Transaction tr, BlockTableRecord ms, Database db, List<(string Item, double Qty, string UnitName)> bom, Point3d at)
    {
        var t = new Table { TableStyle = db.Tablestyle, Position = at };
        t.SetSize(bom.Count + 2, 3);
        t.SetRowHeight(4); t.Columns[0].Width = 70; t.Columns[1].Width = 25; t.Columns[2].Width = 12;
        t.Cells[0, 0].TextString = "PV BILL OF MATERIALS";
        string[] head = ["Item", "Qty", "Unit"];
        for (int c = 0; c < 3; c++) t.Cells[1, c].TextString = head[c];
        for (int r = 0; r < bom.Count; r++)
        {
            t.Cells[r + 2, 0].TextString = bom[r].Item;
            t.Cells[r + 2, 1].TextString = bom[r].Qty.ToString("#,0.#");
            t.Cells[r + 2, 2].TextString = bom[r].UnitName;
        }
        for (int r = 0; r < bom.Count + 2; r++) for (int c = 0; c < 3; c++) t.Cells[r, c].TextHeight = 1.8;
        Add(tr, ms, t, "PV_BOM");
    }

    static string CsvPath(Database db)
    {
        var dwg = db.Filename;
        bool saved = !string.IsNullOrEmpty(dwg) && File.Exists(dwg) && !dwg.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase);
        return saved ? Path.Combine(Path.GetDirectoryName(dwg)!, Path.GetFileNameWithoutExtension(dwg) + "_bom.csv")
                     : Path.Combine(Path.GetTempPath(), "pvlayout_bom.csv");
    }

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
    public void Text(double x, double y, double h, string text, string layer) =>
        Add(new DBText { Position = new Point3d(x, y, 0), Height = h, TextString = text }, layer);
}
