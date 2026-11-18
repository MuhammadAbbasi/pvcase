// Headless checks for the PV engine (no CAD needed): dotnet run --project test\EngineCheck
//  A) parity with pvlayout.py prototype   B) 5 MW sloped-site acceptance test (steps 1-6)
using PVLocal;
using P = (double X, double Y);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}"); if (!ok) fails++; }

// ---------------------------------------------------------------- A) parity
Console.WriteLine("A) parity with Python prototype (116 units / 19 inverters on 300x300 site)");
{
    List<P> b = [(0, 0), (300, 0), (300, 300), (0, 300)];
    List<List<P>> ex = [[(100, 100), (160, 100), (160, 160), (100, 160)]];
    foreach (var m in new[] { MountType.Tracker, MountType.Fixed })
    {
        var r = Engine.Run(b, ex, new Settings { Mount = m });
        Check(r.Units.Count == 116 && r.Inverters.Count == 19, $"{m}: {r.Units.Count} units, {r.Inverters.Count} inverters");
    }
}

// ---------------------------------------------------------------- B) acceptance
Console.WriteLine("\nB) 5 MW sloped-terrain acceptance test");
var dir = Path.Combine(Path.GetTempPath(), "pvlocal_accept"); Directory.CreateDirectory(dir);

// (1) boundary + DEM + wetland
List<P> site = [(0, 0), (430, 0), (470, 200), (400, 380), (0, 360)];
List<P> wetland = [(180, 150), (240, 140), (270, 190), (230, 240), (170, 220)];
var demPath = Path.Combine(dir, "site_dem.asc");
// 5-7% slope to the north-east, rolling hills, and a drainage ditch that forces tall piles
Terrain.SaveAsc(demPath, -20, -20, 5, 110, 90, (x, y) =>
    100 + 0.06 * y + 0.03 * x + 1.5 * Math.Sin(x / 40) * Math.Cos(y / 55) - 3.0 * Math.Exp(-((x - 60) * (x - 60) + (y - 200) * (y - 200)) / 500));
var dem = Terrain.LoadAsc(demPath);
var s = new Settings { Mount = MountType.Tracker, Gcr = 0.35, MaxSystemVoltage = 1500, MaxDcKwp = 5000, TerrainFile = demPath };
Console.WriteLine("(1) import boundary, DEM, wetland");
Check(dem.Cols == 110 && dem.Rows == 90, $"DEM loaded {dem.Cols}x{dem.Rows}, z({100},{100}) = {dem.At(100, 100):0.00} m");
Check(Math.Abs(dem.At(100, 100) - (100 + 6 + 3 + 1.5 * Math.Sin(2.5) * Math.Cos(100 / 55.0))) < 0.05, "DEM bilinear sample matches source surface (±5 cm)");

Console.WriteLine("(2) populate with 1500 V single-axis trackers @ GCR 0.35");
var s28 = new Settings { ModulesPerString = 28 };
var note = Engine.AutoString(s28);
Check(note != null && Engine.StringVocCold(s28) <= 1500, $"auto-string: {note}");
Engine.AutoString(s);
var d = Engine.Run(site, [wetland], s, null, dem);
double kwp = d.Units.Sum(u => u.Strings) * s.ModulesPerString * s.ModulePowerW / 1000;
Check(kwp is > 4900 and <= 5000, $"DC capacity {kwp:0} kWp (target 5000)");
Check(Math.Abs(Engine.RowPitch(s) - s.ModuleLength / 0.35) < 1e-9, $"row pitch {Engine.RowPitch(s):0.00} m = collector / GCR");
Check(Engine.StringVocCold(s) <= 1500, $"string Voc @ {s.SiteMinTempC}°C = {Engine.StringVocCold(s):0} V <= 1500 V (max {Engine.MaxModulesPerString(s)} mod/string)");
bool clearOfWetland = d.Units.All(u => !Engine.SegHitsPoly((u.X0 - s.Setback, u.Y0), (u.X1 + s.Setback, u.Y0), wetland)
    && Engine.RectOk(u.X0 - s.Setback, u.Y0 - s.Setback, u.X1 + s.Setback, u.Y1 + s.Setback, site, [wetland]));
Check(clearOfWetland, $"all {d.Units.Count} trackers inside boundary and >= {s.Setback} m from wetland");

Console.WriteLine("(3) piles > 2.5 m flagged + distribution");
var piles = d.Units.SelectMany(u => u.Piles).ToList();
Check(piles.All(p => p.Reveal >= s.PileMinReveal - 1e-6), $"all {piles.Count} piles >= min reveal {s.PileMinReveal} m");
int flagged = piles.Count(p => p.Reveal > 2.5);
Check(flagged > 0 && d.Warnings.Any(w => w.Contains($"{flagged} piles exceed")), $"{flagged} piles > 2.5 m flagged in warnings");
var bins = Engine.PileBins(d, s);
foreach (var (k, n) in bins) Console.WriteLine($"         {k:0.00}-{k + s.PileBin:0.00} m : {n,5} {new string('#', Math.Max(1, n * 60 / piles.Count))}");
Check(bins.Values.Sum() == piles.Count, "pile distribution sums to pile count");

Console.WriteLine("(4) stringing, DC/AC routing to central transformer, voltage drop, SLD");
Check(d.Units.All(u => u.Inverter != "") && d.Inverters.Sum(i => i.Strings) == d.Units.Sum(u => u.Strings), "every string assigned to exactly one inverter");
Check(d.Inverters.All(i => i.Strings <= s.StringsPerInverter), $"no inverter above {s.StringsPerInverter} strings");
bool routesClear = d.Units.All(u => !Engine.RouteHits(u.DcRoute, [wetland])) && d.Inverters.All(i => !Engine.RouteHits(i.AcRoute, [wetland]))
    && d.Stations.All(t => !Engine.RouteHits(t.MvRoute, [wetland]));
Check(routesClear, "no DC/AC/MV route crosses the wetland");
Check(d.Stations.Count == (d.Inverters.Count + s.InvertersPerStation - 1) / s.InvertersPerStation
    && d.Stations.SelectMany(t => t.Inverters).Distinct().Count() == d.Inverters.Count, $"{d.Stations.Count} transformer stations, every inverter on exactly one");
Check(d.Stations.All(t => t.MvDropPct <= s.MvDropMaxPct && t.MvRoute.Count >= 2), $"MV feeders to substation, max drop {d.Stations.Max(t => t.MvDropPct):0.00}% <= {s.MvDropMaxPct}%");
// electrical steps read trackers back from the drawing: same rectangles -> same strings and same wiring
var back2 = Engine.FromRects(d.Units.Select(u => (u.X0, u.Y0, u.X1, u.Y1)), s, dem);
Engine.Wire(back2, [wetland], s, null);
Check(back2.Units.Sum(u => u.Strings) == d.Units.Sum(u => u.Strings) && back2.Inverters.Count == d.Inverters.Count && back2.Stations.Count == d.Stations.Count,
    "trackers read back from drawing -> same strings, inverters, stations");
Check(Engine.MaxTotalDrop(d) < 2.0, $"worst DC+AC voltage drop {Engine.MaxTotalDrop(d):0.00}% < 2%");
Check(d.TrenchLength > 0 && d.TrenchLength < d.Units.Sum(u => Engine.RouteLen(u.DcRoute)) + d.Inverters.Sum(i => Engine.RouteLen(i.AcRoute)),
    $"trench {d.TrenchLength:0} m (shared runs merged)");
Console.WriteLine($"         SLD: TRANSFORMER <- {d.Inverters.Count} x INV {s.InverterKw} kW <- {d.Units.Sum(u => u.Strings)} strings x {s.ModulesPerString} mod");
Console.WriteLine("         (SLD drawing itself is drawn by PVLAYOUT in CAD; topology verified above)");

Console.WriteLine("    terrain from a CAD mesh (Fetch mesh)");
{
    // triangulate the analytic surface at 10 m, rebuild a 2 m DEM from the triangles, compare with the surface
    Func<double, double, double> surf = (px, py) => 100 + 0.06 * py + 0.03 * px;
    var tris = new List<(double X, double Y, double Z)[]>();
    for (double gx = 0; gx < 300; gx += 10) for (double gy = 0; gy < 300; gy += 10)
    {
        (double, double, double) V(double a, double b) => (a, b, surf(a, b));
        tris.Add([V(gx, gy), V(gx + 10, gy), V(gx + 10, gy + 10)]); tris.Add([V(gx, gy), V(gx + 10, gy + 10), V(gx, gy + 10)]);
    }
    var mesh = Terrain.FromSamples(tris, [], 0, 0, 300, 300, 2);
    double err = Enumerable.Range(0, 50).Max(k => Math.Abs(mesh.At(7 + k * 5.3, 11 + k * 5.1) - surf(7 + k * 5.3, 11 + k * 5.1)));
    Check(err < 0.01, $"DEM from {tris.Count} mesh triangles matches the surface (max error {err * 100:0.0} cm)");
    var gap = Terrain.FromSamples([], [(0, 0, 10), (300, 300, 40)], 0, 0, 300, 300, 5);
    Check(gap.At(10, 10) == 10 && gap.At(290, 290) == 40, "gaps filled from nearest survey point");
    var slope = Engine.TerrainCheck(mesh, [(0, 0), (300, 0), (300, 300), (0, 300)], 10, s);
    Check(slope.All(c => Math.Abs(c.SlopePct - 6) < 0.1), $"terrain check: N-S slope {slope[0].SlopePct:0.0}% (surface 6%) on {slope.Count} cells");
}

Console.WriteLine("(5) shading + annual yield (clear-sky year: no TMY file in test)");
var w = Weather.ClearSkyYear(s.Latitude, s.Longitude);
var y = Yield.Run(d, s, w);
Check(y.SpecificYield is > 1700 and < 3000, $"specific yield {y.SpecificYield:0} kWh/kWp (clear-sky bound)");
Check(y.PR is > 0.75 and < 0.92, $"PR {y.PR * 100:0.0}%");
Check(y.LossesPct.First(l => l.Name == "row shading").Pct < 0.5, $"backtracking keeps row shading ~0 ({y.LossesPct[0].Pct:0.00}%)");
var noBt = Yield.Run(d, new Settings { Mount = MountType.Tracker, Gcr = 0.35, Backtracking = false }, w);
Check(noBt.LossesPct[0].Pct > 1, $"without backtracking row shading = {noBt.LossesPct[0].Pct:0.0}% (shading model active)");
var cmp = Engine.Compare(site, [wetland], new Settings { TerrainFile = demPath }, [0.30, 0.35, 0.45], dem, w);
foreach (var c in cmp) Console.WriteLine($"         GCR {c.Gcr:0.00}: {c.Units,4} trackers {c.Kwp,7:0} kWp {c.Mwh,7:0} MWh {c.SpecYield,5:0} kWh/kWp");
Check(cmp[2].Kwp > cmp[0].Kwp, "scenario compare: higher GCR -> more kWp");

Console.WriteLine("(6) BOM CSV export and totals vs model");
var bom = Engine.Bom(d, s, y);
var csv = Path.Combine(dir, "bom.csv");
Engine.WriteCsv(csv, bom);
var sep = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
var back = File.ReadAllLines(csv).Skip(1).Select(l => l.Split(new[] { sep }, StringSplitOptions.None)).Where(f => !f[0].StartsWith("WARNING")).ToDictionary(f => f[0], f => double.Parse(f[1]));
Check(back[$"modules {s.ModulePowerW} W"] == d.Units.Sum(u => u.Strings) * s.ModulesPerString, $"CSV modules = model ({back[$"modules {s.ModulePowerW} W"]})");
Check(back["piles total"] == piles.Count, $"CSV piles = model ({piles.Count})");
Check(back.Where(kv => kv.Key.StartsWith("  piles reveal")).Sum(kv => kv.Value) == piles.Count, "CSV pile bins sum = pile total");
double dcCsv = back.Where(kv => kv.Key.StartsWith("DC string cable")).Sum(kv => kv.Value);
Check(Math.Abs(dcCsv - d.Units.Sum(u => Engine.DcCableLen(u, s))) < 5, $"CSV DC cable {dcCsv:0} m = model");
Check(back[$"string inverters {s.InverterKw} kW"] == d.Inverters.Count, "CSV inverters = model");
Console.WriteLine($"         {csv}");
foreach (var (i, q, u) in bom) Console.WriteLine($"         {i,-48} {q,12:#,0.##} {u}");

Console.WriteLine(fails == 0 ? "\nALL CHECKS PASSED" : $"\n{fails} CHECK(S) FAILED");
return fails == 0 ? 0 : 1;
