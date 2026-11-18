// pvlocal.exe - PV layout for CADs that block plugins and LISP (CADPILLAR) or any CAD that reads AutoCAD scripts.
//   1. In CAD: closed polylines on PV_BOUNDARY (site) and PV_EXCLUSION (no-build), then DXFOUT -> site.dxf
//   2. pvlocal site.dxf            (or drag site.dxf onto pvlocal.exe)
//   3. In CAD: SCRIPT site_pvlayout.scr  -> draws the layout into the open drawing (rerun replaces it)
// Settings: pvlocal_settings.xml next to the DXF (created with defaults on first run; edit it, rerun).
using System.Globalization;
using System.Runtime.InteropServices;
using PVLocal;
using P = (double X, double Y);

int code = 0;
try { code = Run(args); }
catch (Exception ex) { Console.Error.WriteLine($"\nERROR: {ex.Message}"); code = 1; }
// keep the window open when started by double-click / drag-and-drop (we own the console)
if (GetConsoleProcessList(new uint[2], 2) == 1) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
return code;

static int Run(string[] args)
{
    if (args.Length < 1 || !File.Exists(args[0]))
    {
        Console.WriteLine("usage: pvlocal <site.dxf> [settings.xml]\n" +
            "  site.dxf: closed polylines on layers PV_BOUNDARY and PV_EXCLUSION (DXFOUT from CAD)");
        return 1;
    }
    var dxf = Path.GetFullPath(args[0]);
    var dir = Path.GetDirectoryName(dxf)!;
    var settingsPath = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(dir, "pvlocal_settings.xml");
    if (!File.Exists(settingsPath))
    {
        File.WriteAllText(settingsPath, new Settings().ToXml());
        Console.WriteLine($"Created default settings: {settingsPath}  (edit and rerun to change the design)");
    }
    var s = Settings.FromXml(File.ReadAllText(settingsPath));
    if (Engine.AutoString(s) is { } note) Console.WriteLine($"Auto-string: {note}");

    string Rel(string f) => Path.IsPathRooted(f) ? f : Path.Combine(Path.GetDirectoryName(settingsPath)!, f);
    var terrain = s.TerrainFile != "" ? Terrain.LoadAsc(Rel(s.TerrainFile)) : null;
    var weather = s.WeatherFile != "" ? Weather.LoadPvgisTmy(Rel(s.WeatherFile)) : Weather.ClearSkyYear(s.Latitude, s.Longitude);

    var polys = DxfReader.Polylines(dxf);
    var boundaries = polys.GetValueOrDefault(Drawing.BoundaryLayer) ?? [];
    var exclusions = polys.GetValueOrDefault(Drawing.ExclusionLayer) ?? [];
    if (boundaries.Count == 0)
        throw new InvalidDataException($"no closed polyline on layer {Drawing.BoundaryLayer}. Layers with polylines: {string.Join(", ", polys.Keys)}");
    Console.WriteLine($"Site: {boundaries.Count} boundary, {exclusions.Count} exclusion polylines, terrain: {(terrain == null ? "flat" : "DEM")}");

    var all = new Design();
    var designs = boundaries.Select(b => Engine.Run(b, exclusions, s, null, terrain)).ToList();
    foreach (var d in designs)
    {
        all.Units.AddRange(d.Units); all.Inverters.AddRange(d.Inverters); all.Stations.AddRange(d.Stations);
        all.Warnings.AddRange(d.Warnings); all.TrenchLength += d.TrenchLength;
    }
    for (int i = 0; i < all.Inverters.Count; i++) { all.Inverters[i].Id = $"INV-{i + 1:00}"; foreach (var u in all.Inverters[i].Units) u.Inverter = all.Inverters[i].Id; }
    for (int i = 0; i < all.Stations.Count; i++) all.Stations[i].Id = $"TS-{i + 1:00}";

    var y = Yield.Run(all, s, weather);
    var bom = Engine.Bom(all, s, y);

    var canvas = new ScriptCanvas();
    foreach (var d in designs) Drawing.All(canvas, d, s);
    double right = boundaries.SelectMany(p => p).Max(p => p.X), top = boundaries.SelectMany(p => p).Max(p => p.Y);
    Drawing.BomText(canvas, bom, right + 30, top);
    Drawing.Sld(canvas, all, s, right + 160, top);

    var stem = Path.Combine(dir, Path.GetFileNameWithoutExtension(dxf));
    File.WriteAllText(stem + "_pvlayout.scr", canvas.Build(), System.Text.Encoding.ASCII);
    Engine.WriteCsv(stem + "_bom.csv", bom);

    foreach (var (item, qty, unit) in bom) Console.WriteLine($"  {item,-48} {qty,12:#,0.##} {unit}");
    Console.WriteLine($"\nScript: {stem}_pvlayout.scr   -> in CAD type SCRIPT and pick it");
    Console.WriteLine($"BOM:    {stem}_bom.csv");
    return 0;
}

[DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] list, uint count);

/// Minimal ASCII DXF reader: LWPOLYLINE and POLYLINE in model space, grouped by layer.
static class DxfReader
{
    public static Dictionary<string, List<List<P>>> Polylines(string path)
    {
        var lines = File.ReadAllLines(path);
        var res = new Dictionary<string, List<List<P>>>(StringComparer.OrdinalIgnoreCase);
        bool inEntities = false;
        string? kind = null, layer = null; bool paper = false, inVertex = false; List<P>? cur = null; double? x = null;

        void Flush()
        {
            if (cur != null && layer != null && !paper && cur.Count >= 3)
            {
                if (cur[0] == cur[cur.Count - 1]) cur.RemoveAt(cur.Count - 1);
                if (!res.TryGetValue(layer, out var l)) res[layer] = l = new();
                l.Add(cur);
            }
            cur = null; kind = null; layer = null; paper = false; inVertex = false;
        }

        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            string c = lines[i].Trim(), v = lines[i + 1].Trim();
            if (c == "0")
            {
                if (v == "SECTION" && i + 3 < lines.Length && lines[i + 3].Trim() == "ENTITIES") { inEntities = true; continue; }
                if (v == "ENDSEC") { Flush(); inEntities = false; continue; }
                if (!inEntities) continue;
                if (v == "VERTEX" && kind == "POLYLINE") { x = null; inVertex = true; continue; }   // vertices belong to the open POLYLINE
                Flush();
                if (v is "LWPOLYLINE" or "POLYLINE") { kind = v; cur = new(); }
                continue;
            }
            if (cur == null) continue;
            if (c == "8" && layer == null) layer = v;
            else if (c == "67" && v == "1") paper = true;
            else if (c == "10") x = double.Parse(v, CultureInfo.InvariantCulture);
            else if (c == "20" && x is { } xx)
            {
                // POLYLINE header carries a dummy point: only VERTEX records count
                if (kind == "LWPOLYLINE" || inVertex) cur.Add((xx, double.Parse(v, CultureInfo.InvariantCulture)));
                x = null;
            }
        }
        Flush();
        return res;

    }
}
