// Pure layout + electrical engine: no AutoCAD references (runs headless in test\EngineCheck).
using System.ComponentModel;
using P = (double X, double Y);

namespace PVLocal;

public partial class Settings
{
    [Category("1 Mounting"), Description("Tracker = single-axis, N-S axis. Fixed = E-W rows facing south.")]
    public MountType Mount { get; set; } = MountType.Tracker;
    [Category("1 Mounting"), Description("Strings on a full tracker/table.")] public int StringsPerUnit { get; set; } = 3;
    [Category("1 Mounting"), Description("Partial trackers allowed down to this many strings.")] public int MinStringsPerUnit { get; set; } = 1;
    [Category("1 Mounting"), Description("Fixed only: modules stacked in portrait (2 = 2P).")] public int RowsOfModules { get; set; } = 1;
    [Category("1 Mounting"), Description("Fixed only: tilt in degrees.")] public double TiltDeg { get; set; } = 25;
    [Category("1 Mounting"), Description("Tracker only: drive/motor gap per unit (m).")] public double MotorGap { get; set; } = 0.6;
    [Category("1 Mounting"), Description("Tracker only: max rotation (deg).")] public double MaxTrackerAngle { get; set; } = 60;
    [Category("1 Mounting"), Description("Tracker only: backtracking to avoid row-to-row shading.")] public bool Backtracking { get; set; } = true;

    [Category("2 Module"), Description("Long side (m).")] public double ModuleLength { get; set; } = 2.278;
    [Category("2 Module"), Description("Short side (m).")] public double ModuleWidth { get; set; } = 1.134;
    [Category("2 Module"), Description("Gap between modules (m).")] public double ModuleGap { get; set; } = 0.02;
    [Category("2 Module")] public double ModulePowerW { get; set; } = 550;
    [Category("2 Module")] public double ModuleVoc { get; set; } = 49.6;
    [Category("2 Module")] public double ModuleVmp { get; set; } = 41.8;
    [Category("2 Module")] public double ModuleImp { get; set; } = 13.16;
    [Category("2 Module"), Description("%/°C (negative).")] public double TempCoeffVocPct { get; set; } = -0.27;
    [Category("2 Module"), Description("%/°C (negative).")] public double TempCoeffPmaxPct { get; set; } = -0.35;
    [Category("2 Module")] public double ModuleNoctC { get; set; } = 45;
    [Category("2 Module")] public int ModulesPerString { get; set; } = 27;

    [Category("3 Layout"), Description("Ground coverage ratio; sets row pitch.")] public double Gcr { get; set; } = 0.35;
    [Category("3 Layout"), Description("Gap between consecutive units in a row (m).")] public double UnitGap { get; set; } = 1.0;
    [Category("3 Layout"), Description("Distance kept from boundary and exclusions (m).")] public double Setback { get; set; } = 10.0;
    [Category("3 Layout"), Description("Packing search step (m). Smaller = denser, slower.")] public double SearchStep { get; set; } = 1.0;
    [Category("3 Layout"), Description("Stop placing once this DC capacity is reached (kWp). 0 = fill the site.")] public double MaxDcKwp { get; set; } = 0;

    [Category("4 Piles & terrain"), Description("DEM file (ESRI ASCII grid .asc). Empty = flat ground at z=0.")] public string TerrainFile { get; set; } = "";
    [Category("4 Piles & terrain"), Description("Grid cell (m) when building the DEM from a mesh / points in the drawing (Fetch mesh).")] public double TerrainCell { get; set; } = 2.0;
    [Category("4 Piles & terrain")] public double PileSpacing { get; set; } = 7.0;
    [Category("4 Piles & terrain"), Description("Minimum reveal: ground to torque tube / lowest beam (m).")] public double PileMinReveal { get; set; } = 1.3;
    [Category("4 Piles & terrain"), Description("Piles above this reveal are flagged (m).")] public double PileMaxReveal { get; set; } = 2.5;
    [Category("4 Piles & terrain"), Description("Bin width for the pile length schedule (m).")] public double PileBin { get; set; } = 0.25;
    [Category("4 Piles & terrain"), Description("Max slope along a unit before it is flagged (%).")] public double MaxUnitSlopePct { get; set; } = 10;

    [Category("5 Electrical")] public int StringsPerInverter { get; set; } = 18;
    [Category("5 Electrical")] public double InverterKw { get; set; } = 300;
    [Category("5 Electrical")] public double InverterAcVoltage { get; set; } = 800;
    [Category("5 Electrical")] public double InverterEffPct { get; set; } = 98.5;
    [Category("5 Electrical"), Description("1000 or 1500 V system.")] public double MaxSystemVoltage { get; set; } = 1500;
    [Category("5 Electrical"), Description("Record low site temperature for Voc check (°C).")] public double SiteMinTempC { get; set; } = -10;
    [Category("5 Electrical"), Description("Max DC voltage drop per string (%).")] public double DcDropMaxPct { get; set; } = 1.0;
    [Category("5 Electrical"), Description("Max AC voltage drop inverter->transformer (%).")] public double AcDropMaxPct { get; set; } = 1.0;
    [Category("5 Electrical"), Description("Ohm*mm²/m. Cu@70°C = 0.0217, Al@70°C = 0.0356.")] public double CableResistivity { get; set; } = 0.0217;
    [Category("5 Electrical")] public double PowerFactor { get; set; } = 1.0;
    [Category("5 Electrical"), Description("Multiplier on routed length: sag, terminations, detours.")] public double CableSlack { get; set; } = 1.05;
    [Category("5 Electrical"), Description("String inverters per MV transformer station.")] public int InvertersPerStation { get; set; } = 10;
    [Category("5 Electrical"), Description("MV collection voltage (V).")] public double MvVoltage { get; set; } = 20000;
    [Category("5 Electrical"), Description("Max MV voltage drop station->substation (%).")] public double MvDropMaxPct { get; set; } = 1.0;
    [Category("5 Electrical"), Description("Ohm*mm²/m for MV cable. Al@90°C = 0.0375, Cu@90°C = 0.0225.")] public double MvCableResistivity { get; set; } = 0.0375;
    [Category("5 Electrical"), Description("Use the picked substation point (set by MV cables / Generate all).")] public bool UseSubstationPoint { get; set; }
    [Category("5 Electrical")] public double SubstationX { get; set; }
    [Category("5 Electrical")] public double SubstationY { get; set; }

    [Category("6 Yield"), Description("Degrees, north positive.")] public double Latitude { get; set; } = 37.5;
    [Category("6 Yield"), Description("Degrees, east positive.")] public double Longitude { get; set; } = 14.0;
    [Category("6 Yield"), Description("PVGIS TMY CSV (download once from re.jrc.ec.europa.eu/pvg_tools). Empty = clear-sky estimate.")] public string WeatherFile { get; set; } = "";
    [Category("6 Yield")] public double Albedo { get; set; } = 0.2;
    [Category("6 Yield")] public double SoilingPct { get; set; } = 2.0;
    [Category("6 Yield")] public double MismatchPct { get; set; } = 1.0;
    [Category("6 Yield"), Description("ASHRAE incidence angle modifier b0.")] public double IamB0 { get; set; } = 0.05;
    [Category("6 Yield"), Description("Inter-annual + model uncertainty (1 sigma, %) for P90.")] public double YieldUncertaintyPct { get; set; } = 5;
}

public partial class Settings
{
    static readonly System.Xml.Serialization.XmlSerializer Xml = new(typeof(Settings));
    public string ToXml() { using var w = new StringWriter(); Xml.Serialize(w, this); return w.ToString(); }
    public static Settings FromXml(string xml) { using var r = new StringReader(xml); return (Settings)Xml.Deserialize(r)!; }
}

public static class Mx
{
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}

public enum MountType { Tracker, Fixed }

public record Pile(double X, double Y, double Ground, double Top) { public double Reveal => Top - Ground; }

public class Unit
{
    public int Col, Strings;
    public double X0, Y0, X1, Y1;
    public List<P> DcRoute = new();
    public string Inverter = "";
    public List<Pile> Piles = new();
    public double SlopePct;
    public double DcSection, DcDropPct;   // mm², worst string on this unit
}

public class Inverter
{
    public string Id = "";
    public P Pos;
    public List<Unit> Units = new();
    public int Strings;
    public List<P> AcRoute = new();
    public double AcSection, AcDropPct;
}

public class Station
{
    public string Id = "";
    public P Pos;
    public List<Inverter> Inverters = new();
    public List<P> MvRoute = new();
    public double Kw, MvSection, MvDropPct;
}

public class Design
{
    public List<Unit> Units = new();
    public List<Inverter> Inverters = new();
    public List<Station> Stations = new();
    public P? Substation;
    public List<string> Warnings = new();
    public double TrenchLength;
}

public static class Engine
{
    public static readonly double[] DcSections = [4, 6, 10, 16];
    public static readonly double[] AcSections = [16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300];
    public static readonly double[] MvSections = [50, 70, 95, 120, 150, 185, 240, 300, 400, 500, 630];

    // ------------------------------------------------ geometry
    public static bool PointInPoly(double x, double y, IList<P> poly)
    {
        bool inside = false;
        for (int i = 0, n = poly.Count; i < n; i++)
        {
            var (x1, y1) = poly[i]; var (x2, y2) = poly[(i + 1) % n];
            if ((y1 > y) != (y2 > y) && x < (x2 - x1) * (y - y1) / (y2 - y1) + x1) inside = !inside;
        }
        return inside;
    }

    // Liang-Barsky: does segment p-q pass through the rect interior?
    public static bool SegHitsRect(P p, P q, double rx0, double ry0, double rx1, double ry1, double eps = 1e-9)
    {
        double x0 = rx0 + eps, y0 = ry0 + eps, x1 = rx1 - eps, y1 = ry1 - eps;
        double dx = q.X - p.X, dy = q.Y - p.Y, t0 = 0, t1 = 1;
        foreach (var (pk, qk) in new[] { (-dx, p.X - x0), (dx, x1 - p.X), (-dy, p.Y - y0), (dy, y1 - p.Y) })
        {
            if (pk == 0) { if (qk < 0) return false; }
            else { double t = qk / pk; if (pk < 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t); }
        }
        return t1 - t0 > eps;
    }

    static double Cross(P o, P a, P b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    static bool SegsCross(P a, P b, P c, P d) =>
        Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0;

    /// Segment enters the polygon interior (endpoint inside, or proper crossing of an edge).
    public static bool SegHitsPoly(P a, P b, IList<P> poly)
    {
        P mid = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        if (PointInPoly(a.X, a.Y, poly) || PointInPoly(b.X, b.Y, poly) || PointInPoly(mid.X, mid.Y, poly)) return true;
        for (int i = 0; i < poly.Count; i++) if (SegsCross(a, b, poly[i], poly[(i + 1) % poly.Count])) return true;
        return false;
    }

    public static bool RouteHits(List<P> route, List<List<P>> polys) =>
        route.Zip(route.Skip(1)).Any(s => polys.Any(p => SegHitsPoly(s.First, s.Second, p)));

    static bool PolyClearOfRect(IList<P> poly, double x0, double y0, double x1, double y1)
    {
        for (int i = 0; i < poly.Count; i++)
            if (SegHitsRect(poly[i], poly[(i + 1) % poly.Count], x0, y0, x1, y1)) return false;
        return true;
    }

    static P[] Corners(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    public static bool RectOk(double x0, double y0, double x1, double y1, IList<P> boundary, List<List<P>> exclusions)
    {
        var c = Corners(x0, y0, x1, y1);
        if (!c.All(p => PointInPoly(p.X, p.Y, boundary))) return false;
        if (!PolyClearOfRect(boundary, x0, y0, x1, y1)) return false;
        foreach (var ex in exclusions)
            if (c.Any(p => PointInPoly(p.X, p.Y, ex)) || !PolyClearOfRect(ex, x0, y0, x1, y1)) return false;
        return true;
    }

    public static double PolyArea(IList<P> p) =>
        Math.Abs(Enumerable.Range(0, p.Count).Sum(i => p[i].X * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].X * p[i].Y)) / 2;

    static List<P> Swap(IEnumerable<P> pts) => pts.Select(p => (p.Y, p.X)).ToList();
    static double Dist(P a, P b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    public static double RouteLen(List<P> r) => r.Zip(r.Skip(1), Dist).Sum();

    // ------------------------------------------------ layout (local frame: unit long axis = +y)
    public static (double Length, double Width) UnitDims(Settings s, int nStrings)
    {
        int mods = nStrings * s.ModulesPerString;
        if (s.Mount == MountType.Tracker)
            return (mods * (s.ModuleWidth + s.ModuleGap) + s.MotorGap, s.ModuleLength);
        int perRow = (int)Math.Ceiling(mods / (double)s.RowsOfModules);
        return (perRow * (s.ModuleWidth + s.ModuleGap),
                s.RowsOfModules * (s.ModuleLength + s.ModuleGap) * Math.Cos(s.TiltDeg * Math.PI / 180));
    }

    /// Collector width perpendicular to the row (m): module length(s) across the tracker / table slope.
    public static double CollectorWidth(Settings s) =>
        s.Mount == MountType.Tracker ? s.ModuleLength : s.RowsOfModules * (s.ModuleLength + s.ModuleGap);

    public static double RowPitch(Settings s) => CollectorWidth(s) / s.Gcr;

    static List<Unit> Fill(List<P> boundary, List<List<P>> exclusions, Settings s, ref double kwpLeft)
    {
        double sb = s.Setback, pitch = RowPitch(s), stringKw = s.ModulesPerString * s.ModulePowerW / 1000;
        double minX = boundary.Min(p => p.X), maxX = boundary.Max(p => p.X);
        double minY = boundary.Min(p => p.Y), maxY = boundary.Max(p => p.Y);
        var options = Enumerable.Range(s.MinStringsPerUnit, Math.Max(0, s.StringsPerUnit - s.MinStringsPerUnit + 1))
            .Reverse().Select(n => (N: n, Dims: UnitDims(s, n))).ToList();
        var units = new List<Unit>();
        if (options.Count == 0) return units;
        double width = options[0].Dims.Width;
        int col = 0;
        for (double x = minX + sb; x + width + sb <= maxX; x += pitch, col++)
        {
            double y = minY + sb;
            while (y < maxY - sb)
            {
                bool placed = false;
                foreach (var (n, (length, _)) in options)
                {
                    if (n * stringKw > kwpLeft + 1e-9) continue;   // capacity cap
                    if (!RectOk(x - sb, y - sb, x + width + sb, y + length + sb, boundary, exclusions)) continue;
                    units.Add(new Unit { Col = col, Strings = n, X0 = x, Y0 = y, X1 = x + width, Y1 = y + length });
                    kwpLeft -= n * stringKw;
                    y += length + s.UnitGap; placed = true; break;
                }
                if (!placed) y += s.SearchStep;   // ponytail: greedy first-fit; optimiser would pack tighter
            }
        }
        return units;
    }

    /// Manhattan route a->b: both L shapes, else shortest 3-leg detour along exclusion bounding-box edges.
    // ponytail: detour candidates = bbox edges + 5 m; a visibility-graph pathfinder handles nested/complex exclusions
    static List<P> RouteAround(P a, P b, List<List<P>> exclusions, List<string> warn, string what)
    {
        List<P> alongFirst = [a, (a.X, b.Y), b], acrossFirst = [a, (b.X, a.Y), b];
        if (!RouteHits(alongFirst, exclusions)) return alongFirst;
        if (!RouteHits(acrossFirst, exclusions)) return acrossFirst;
        const double m = 5;
        var xs = exclusions.SelectMany(e => new[] { e.Min(p => p.X) - m, e.Max(p => p.X) + m }).Append(a.X).Append(b.X).Distinct().ToList();
        var ys = exclusions.SelectMany(e => new[] { e.Min(p => p.Y) - m, e.Max(p => p.Y) + m }).Append(a.Y).Append(b.Y).Distinct().ToList();
        // 3- and 4-leg Manhattan detours along exclusion bounding-box edges, both orders; shortest clear one wins
        var best = xs.SelectMany(x => ys.SelectMany(y => new[]
            {
                new List<P> { a, (x, a.Y), (x, y), (b.X, y), b },
                new List<P> { a, (a.X, y), (x, y), (x, b.Y), b },
            }))
            .Where(r => !RouteHits(r, exclusions)).MinBy(RouteLen);
        if (best != null) return best;
        warn.Add("cable route crosses an exclusion - reroute by hand (layer PV_CABLE_*)");
        return alongFirst;
    }

    /// Slide a point along the corridor (y) to the nearest spot >= 5 m outside every exclusion.
    static double ClearSpot(double x, double y, List<List<P>> exclusions)
    {
        bool Clear(double yy) => exclusions.All(e => !PointInPoly(x, yy, e) && PolyClearOfRect(e, x - 5, yy - 5, x + 5, yy + 5));
        for (double dy = 0; dy < 2000; dy += 1)
        {
            if (Clear(y + dy)) return y + dy;
            if (Clear(y - dy)) return y - dy;
        }
        return y;
    }

    static List<Inverter> Electrical(List<Unit> units, List<List<P>> exclusions, Settings s, List<string> warn)
    {
        // snake order (up one row, down the next) keeps each inverter's units adjacent
        var order = units.OrderBy(u => u.Col).ThenBy(u => u.Col % 2 == 0 ? u.Y0 : -u.Y0).ToList();
        var groups = new List<List<Unit>>(); var cur = new List<Unit>(); int curS = 0;
        foreach (var u in order)
        {
            if (cur.Count > 0 && curS + u.Strings > s.StringsPerInverter) { groups.Add(cur); cur = new(); curS = 0; }
            cur.Add(u); curS += u.Strings;
        }
        if (cur.Count > 0) groups.Add(cur);

        var invs = new List<Inverter>();
        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            // ponytail: inverter at group centroid, snapped into the inter-row corridor
            double cx = g.Average(u => (u.X0 + u.X1) / 2), cy = g.Average(u => (u.Y0 + u.Y1) / 2);
            var near = g.MinBy(u => Math.Abs((u.X0 + u.X1) / 2 - cx))!;
            cx = near.X1 + (RowPitch(s) - (near.X1 - near.X0)) / 2;
            cy = ClearSpot(cx, cy, exclusions);
            var inv = new Inverter { Id = $"INV-{i + 1:00}", Pos = (cx, cy), Units = g, Strings = g.Sum(u => u.Strings) };
            foreach (var u in g)
            {
                P start = ((u.X0 + u.X1) / 2, cy > (u.Y0 + u.Y1) / 2 ? u.Y1 : u.Y0);   // unit end facing the inverter
                u.Inverter = inv.Id;
                u.DcRoute = RouteAround(start, (cx, cy), exclusions, warn, $"DC {inv.Id}");
            }
            invs.Add(inv);
        }
        return invs;
    }

    // ------------------------------------------------ string voltage
    /// Auto-stringing: cap modules/string so cold Voc stays under the system voltage. Returns a note when changed.
    public static string? AutoString(Settings s)
    {
        int max = MaxModulesPerString(s);
        if (s.ModulesPerString <= max) return null;
        var note = $"modules/string {s.ModulesPerString} -> {max} (Voc @ {s.SiteMinTempC}°C must stay <= {s.MaxSystemVoltage} V)";
        s.ModulesPerString = max;
        return note;
    }

    public static double StringVocCold(Settings s) =>
        s.ModulesPerString * s.ModuleVoc * (1 + s.TempCoeffVocPct / 100 * (s.SiteMinTempC - 25));

    public static int MaxModulesPerString(Settings s) =>
        (int)Math.Floor(s.MaxSystemVoltage / (s.ModuleVoc * (1 + s.TempCoeffVocPct / 100 * (s.SiteMinTempC - 25))));

    // ------------------------------------------------ cable sizing (IEC 60364-5-52 style resistive drop)
    public static (double Section, double DropPct) SizeDc(double oneWayM, Settings s)
    {
        double v = s.ModulesPerString * s.ModuleVmp;
        foreach (var a in DcSections)
        {
            double drop = 2 * oneWayM * s.ModuleImp * s.CableResistivity / a / v * 100;
            if (drop <= s.DcDropMaxPct) return (a, drop);
        }
        double big = DcSections[DcSections.Length - 1];
        return (big, 2 * oneWayM * s.ModuleImp * s.CableResistivity / big / v * 100);
    }

    public static (double Section, double DropPct) SizeAc(double lengthM, Settings s)
    {
        double i = s.InverterKw * 1000 / (Math.Sqrt(3) * s.InverterAcVoltage * s.PowerFactor);
        // ponytail: resistive drop only (reactance ignored, fine below ~150 mm²); ampacity not checked
        foreach (var a in AcSections)
        {
            double drop = Math.Sqrt(3) * lengthM * i * s.CableResistivity / a * s.PowerFactor / s.InverterAcVoltage * 100;
            if (drop <= s.AcDropMaxPct) return (a, drop);
        }
        double big = AcSections[AcSections.Length - 1];
        return (big, Math.Sqrt(3) * lengthM * i * s.CableResistivity / big * s.PowerFactor / s.InverterAcVoltage * 100);
    }

    /// Trench = union of all route segments (overlapping runs share one trench).
    public static double TrenchLength(IEnumerable<List<P>> routes)
    {
        var lines = new Dictionary<(bool Vertical, long Key), List<(double A, double B)>>();
        double other = 0;
        foreach (var r in routes)
            foreach (var (a, b) in r.Zip(r.Skip(1)))
            {
                if (Math.Abs(a.X - b.X) < 1e-6) Add((true, (long)Math.Round(a.X * 100)), a.Y, b.Y);
                else if (Math.Abs(a.Y - b.Y) < 1e-6) Add((false, (long)Math.Round(a.Y * 100)), a.X, b.X);
                else other += Dist(a, b);
            }
        double total = other;
        foreach (var iv in lines.Values)
        {
            double end = double.MinValue;
            foreach (var (lo, hi) in iv.OrderBy(v => v.A))
            {
                if (hi <= end) continue;
                total += hi - Math.Max(lo, end); end = hi;
            }
        }
        return total;

        void Add((bool, long) k, double p, double q)
        {
            if (!lines.TryGetValue(k, out var l)) lines[k] = l = new();
            l.Add((Math.Min(p, q), Math.Max(p, q)));
        }
    }

    // ------------------------------------------------ piles on terrain
    public static List<P> PilePositions(Unit u, Settings s, out double[] lineRise)
    {
        bool tr = s.Mount == MountType.Tracker;
        // local frame: along = y for trackers, x for fixed tables
        double a0 = tr ? u.Y0 : u.X0, a1 = tr ? u.Y1 : u.X1, c0 = tr ? u.X0 : u.Y0, c1 = tr ? u.X1 : u.Y1;
        int n = (int)Math.Ceiling((a1 - a0) / s.PileSpacing) + 1;
        double rise = CollectorWidth(s) * Math.Sin(s.TiltDeg * Math.PI / 180) * 0.7;   // front->back pile line height step
        double[] lines = tr ? [(c0 + c1) / 2] : [c0 + 0.15 * (c1 - c0), c1 - 0.15 * (c1 - c0)];
        lineRise = tr ? [0] : [0, rise];   // fixed: back line is higher (south row edge is at low y)
        var res = new List<P>();
        foreach (var l in lines)
            for (int i = 0; i < n; i++) { double a = a0 + i * (a1 - a0) / (n - 1); res.Add(tr ? (l, a) : (a, l)); }
        return res;
    }

    /// Straight structure (rigid tracker / table): best-fit line through ground, lifted so the lowest pile meets min reveal.
    // ponytail: rigid units only; terrain-following trackers (articulated bays) need per-bay fits
    static void SetPiles(Unit u, Settings s, Terrain? t)
    {
        var pts = PilePositions(u, s, out var rise);
        bool tr = s.Mount == MountType.Tracker;
        int perLine = pts.Count / rise.Length;
        var along = pts.Select(p => tr ? p.Y : p.X).ToArray();
        var ground = pts.Select(p => t?.At(p.X, p.Y) ?? 0).ToArray();
        // least squares z = a + b*along, over all lines (minus each line's design rise)
        var zAdj = ground.Select((g, i) => g - rise[i / perLine]).ToArray();
        double mA = along.Average(), mZ = zAdj.Average();
        double sxx = along.Sum(a => (a - mA) * (a - mA)), sxy = along.Select((a, i) => (a - mA) * (zAdj[i] - mZ)).Sum();
        double b = sxx > 0 ? sxy / sxx : 0, a = mZ - b * mA;
        double lift = s.PileMinReveal - Enumerable.Range(0, pts.Count).Min(i => a + b * along[i] + rise[i / perLine] - ground[i]);
        u.Piles = pts.Select((p, i) => new Pile(p.X, p.Y, ground[i], a + b * along[i] + rise[i / perLine] + lift)).ToList();
        u.SlopePct = Math.Abs(b) * 100;
    }

    // ------------------------------------------------ run: Layout (mechanical) + Wire (electrical)
    public static Design Run(List<P> boundary, List<List<P>> exclusions, Settings s, P? substation = null, Terrain? terrain = null)
    {
        var d = Layout(boundary, exclusions, s, terrain);
        Wire(d, exclusions, s, substation);
        return d;
    }

    /// Structures + piles only.
    public static Design Layout(List<P> boundary, List<List<P>> exclusions, Settings s, Terrain? terrain)
    {
        bool fx = s.Mount == MountType.Fixed;
        var b = fx ? Swap(boundary) : boundary;
        var ex = fx ? exclusions.Select(e => Swap(e)).ToList() : exclusions;
        double kwpLeft = s.MaxDcKwp > 0 ? s.MaxDcKwp : double.MaxValue;
        var d = new Design { Units = Fill(b, ex, s, ref kwpLeft) };
        if (fx) foreach (var u in d.Units) SwapUnit(u);
        Mechanical(d, s, terrain);
        return d;
    }

    /// Units read back from the drawing (the user may have deleted or moved some): strings from length, rows from position.
    public static Design FromRects(IEnumerable<(double X0, double Y0, double X1, double Y1)> rects, Settings s, Terrain? terrain)
    {
        bool tr = s.Mount == MountType.Tracker;
        var opts = Enumerable.Range(1, Math.Max(1, s.StringsPerUnit)).Select(n => (N: n, Len: UnitDims(s, n).Length)).ToList();
        var units = rects.Select(r =>
        {
            double len = tr ? r.Y1 - r.Y0 : r.X1 - r.X0;
            return new Unit { X0 = r.X0, Y0 = r.Y0, X1 = r.X1, Y1 = r.Y1, Strings = opts.MinBy(o => Math.Abs(o.Len - len)).N };
        }).ToList();
        // rows/columns: cluster on the cross-axis position (gap > 0.5 m starts a new column)
        int col = -1; double last = double.MinValue;
        foreach (var u in units.OrderBy(u => tr ? u.X0 : u.Y0))
        {
            double k = tr ? u.X0 : u.Y0;
            if (k - last > 0.5) col++;
            u.Col = col; last = k;
        }
        var d = new Design { Units = units };
        Mechanical(d, s, terrain);
        return d;
    }

    static void Mechanical(Design d, Settings s, Terrain? terrain)
    {
        foreach (var u in d.Units) SetPiles(u, s, terrain);
        int highPiles = d.Units.Sum(u => u.Piles.Count(p => p.Reveal > s.PileMaxReveal + 1e-9));
        if (highPiles > 0) d.Warnings.Add($"{highPiles} piles exceed max reveal {s.PileMaxReveal} m (layer PV_PILES_FLAGGED) - consider grading");
        int steep = d.Units.Count(u => u.SlopePct > s.MaxUnitSlopePct);
        if (steep > 0) d.Warnings.Add($"{steep} units steeper than {s.MaxUnitSlopePct}% along their axis");
    }

    static void SwapUnit(Unit u)
    {
        (u.X0, u.Y0, u.X1, u.Y1) = (u.Y0, u.X0, u.Y1, u.X1);
        u.DcRoute = Swap(u.DcRoute);
    }

    /// Electrical: strings -> inverters (DC) -> transformer stations (AC) -> substation (MV); sizing; trench.
    public static void Wire(Design d, List<List<P>> exclusions, Settings s, P? substation)
    {
        bool fx = s.Mount == MountType.Fixed;
        var ex = fx ? exclusions.Select(e => Swap(e)).ToList() : exclusions;
        if (fx) foreach (var u in d.Units) SwapUnit(u);   // local frame: unit axis = +y, corridors run along y
        d.Inverters = Electrical(d.Units, ex, s, d.Warnings);
        d.Stations = Stations(d.Inverters, ex, s, d.Warnings);
        P? sub = substation is { } ss ? (fx ? (ss.Y, ss.X) : ss)
               : d.Stations.Count > 0 ? (d.Stations.Average(t => t.Pos.X), d.Stations.Average(t => t.Pos.Y)) : null;
        if (sub is { } su)
            foreach (var t in d.Stations) t.MvRoute = RouteAround(t.Pos, su, ex, d.Warnings, $"MV {t.Id}");
        if (fx)   // back to global frame
        {
            foreach (var u in d.Units) SwapUnit(u);
            foreach (var i in d.Inverters) { i.Pos = (i.Pos.Y, i.Pos.X); i.AcRoute = Swap(i.AcRoute); }
            foreach (var t in d.Stations) { t.Pos = (t.Pos.Y, t.Pos.X); t.MvRoute = Swap(t.MvRoute); }
            if (sub is { } su2) sub = (su2.Y, su2.X);
        }
        d.Substation = sub;

        foreach (var u in d.Units)
        {
            // each string home-runs from its own position on the unit; worst = farthest string
            double unitLen = s.Mount == MountType.Tracker ? u.Y1 - u.Y0 : u.X1 - u.X0;
            double worst = (RouteLen(u.DcRoute) + unitLen * (u.Strings - 0.5) / u.Strings) * s.CableSlack;
            (u.DcSection, u.DcDropPct) = SizeDc(worst, s);
        }
        foreach (var i in d.Inverters) (i.AcSection, i.AcDropPct) = SizeAc(RouteLen(i.AcRoute) * s.CableSlack, s);
        foreach (var t in d.Stations) (t.MvSection, t.MvDropPct) = SizeMv(RouteLen(t.MvRoute) * s.CableSlack, t.Kw, s);
        d.TrenchLength = TrenchLength(d.Units.Select(u => u.DcRoute).Concat(d.Inverters.Select(i => i.AcRoute)).Concat(d.Stations.Select(t => t.MvRoute)));

        if (StringVocCold(s) > s.MaxSystemVoltage)
            d.Warnings.Add($"String Voc at {s.SiteMinTempC}°C = {StringVocCold(s):0} V > {s.MaxSystemVoltage} V: use <= {MaxModulesPerString(s)} modules/string");
        if (d.Stations.Any(t => t.MvDropPct > s.MvDropMaxPct + 1e-9))
            d.Warnings.Add($"MV voltage drop above {s.MvDropMaxPct}% even at {MvSections[MvSections.Length - 1]} mm² - move the substation or add feeders");
    }

    /// Inverters (already in snake order) grouped into transformer stations; AC from each inverter to its station.
    static List<Station> Stations(List<Inverter> invs, List<List<P>> exclusions, Settings s, List<string> warn)
    {
        var res = new List<Station>();
        int per = Math.Max(1, s.InvertersPerStation);
        for (int k = 0; k * per < invs.Count; k++)
        {
            var g = invs.Skip(k * per).Take(per).ToList();
            // ponytail: station at the group centroid, on the nearest inverter's corridor; no road/access model
            double cx = g.Average(i => i.Pos.X), cy = g.Average(i => i.Pos.Y);
            double x = g.MinBy(i => Math.Abs(i.Pos.X - cx))!.Pos.X;
            var t = new Station { Id = $"TS-{k + 1:00}", Pos = (x, ClearSpot(x, cy, exclusions)), Inverters = g, Kw = g.Count * s.InverterKw };
            foreach (var i in g) i.AcRoute = RouteAround(i.Pos, t.Pos, exclusions, warn, $"AC {i.Id}");
            res.Add(t);
        }
        return res;
    }

    public static (double Section, double DropPct) SizeMv(double lengthM, double kw, Settings s)
    {
        double i = kw * 1000 / (Math.Sqrt(3) * s.MvVoltage * s.PowerFactor);
        double Drop(double a) => Math.Sqrt(3) * lengthM * i * s.MvCableResistivity / a * s.PowerFactor / s.MvVoltage * 100;
        foreach (var a in MvSections) if (Drop(a) <= s.MvDropMaxPct) return (a, Drop(a));
        double big = MvSections[MvSections.Length - 1];
        return (big, Drop(big));
    }

    /// Terrain check: slope along the unit axis (N-S for trackers, E-W for fixed tables) on a grid inside the boundary.
    public static List<(double X0, double Y0, double X1, double Y1, double SlopePct)> TerrainCheck(Terrain t, List<P> boundary, double cell, Settings s)
    {
        var res = new List<(double, double, double, double, double)>();
        double minX = boundary.Min(p => p.X), maxX = boundary.Max(p => p.X), minY = boundary.Min(p => p.Y), maxY = boundary.Max(p => p.Y);
        bool tr = s.Mount == MountType.Tracker;
        for (double x = minX; x < maxX; x += cell)
            for (double y = minY; y < maxY; y += cell)
            {
                double cx = x + cell / 2, cy = y + cell / 2, h = cell / 2;
                if (!PointInPoly(cx, cy, boundary)) continue;
                double slope = tr ? (t.At(cx, cy + h) - t.At(cx, cy - h)) / (2 * h) : (t.At(cx + h, cy) - t.At(cx - h, cy)) / (2 * h);
                res.Add((x, y, x + cell, y + cell, Math.Abs(slope) * 100));
            }
        return res;
    }

    /// Pile schedule: length bins (reveal) -> count. Embedment depth is a geotech input, not modelled.
    public static SortedDictionary<double, int> PileBins(Design d, Settings s)
    {
        var bins = new SortedDictionary<double, int>();
        foreach (var p in d.Units.SelectMany(u => u.Piles))
        {
            double k = Math.Floor(p.Reveal / s.PileBin + 1e-9) * s.PileBin;
            bins[k] = bins.GetValueOrDefault(k) + 1;
        }
        return bins;
    }

    // ------------------------------------------------ report / BOM
    public static List<(string Item, double Qty, string UnitName)> Bom(Design d, Settings s, YieldResult? y = null)
    {
        int strings = d.Units.Sum(u => u.Strings), modules = strings * s.ModulesPerString;
        int full = d.Units.Count(u => u.Strings == s.StringsPerUnit);
        string kind = s.Mount == MountType.Tracker ? "trackers" : "tables";
        double dcKwp = modules * s.ModulePowerW / 1000, acKw = d.Inverters.Count * s.InverterKw;
        var rows = new List<(string, double, string)>
        {
            ($"{kind} (full, {s.StringsPerUnit} strings)", full, "pcs"),
            ($"{kind} (partial)", d.Units.Count - full, "pcs"),
            ("strings", strings, "pcs"),
            ($"modules {s.ModulePowerW} W", modules, "pcs"),
            ("piles total", d.Units.Sum(u => u.Piles.Count), "pcs"),
        };
        foreach (var (bin, n) in PileBins(d, s))
            rows.Add(($"  piles reveal {bin:0.00}-{bin + s.PileBin:0.00} m{(bin + s.PileBin > s.PileMaxReveal + 1e-9 ? " (FLAGGED)" : "")}", n, "pcs"));
        rows.Add(($"string inverters {s.InverterKw} kW", d.Inverters.Count, "pcs"));
        foreach (var g in d.Units.GroupBy(u => u.DcSection).OrderBy(g => g.Key))
            rows.Add(($"DC string cable {g.Key} mm² (+/-)", Math.Round(g.Sum(u => DcCableLen(u, s))), "m"));
        foreach (var g in d.Inverters.GroupBy(i => i.AcSection).OrderBy(g => g.Key))
            rows.Add(($"AC cable 3x{g.Key} mm²", Math.Round(g.Sum(i => RouteLen(i.AcRoute)) * s.CableSlack), "m"));
        if (d.Stations.Count > 0)
            rows.Add(($"transformer stations (<= {s.InvertersPerStation} inverters, {s.MvVoltage / 1000:0.#} kV)", d.Stations.Count, "pcs"));
        foreach (var g in d.Stations.GroupBy(t => t.MvSection).OrderBy(g => g.Key))
            rows.Add(($"MV cable 3x{g.Key} mm²", Math.Round(g.Sum(t => RouteLen(t.MvRoute)) * s.CableSlack), "m"));
        rows.Add(("trench (shared runs merged)", Math.Round(d.TrenchLength), "m"));
        rows.Add(("string Voc at min temp", Math.Round(StringVocCold(s)), "V"));
        rows.Add(("max DC voltage drop", Math.Round(d.Units.Select(u => u.DcDropPct).DefaultIfEmpty().Max(), 2), "%"));
        rows.Add(("max AC voltage drop", Math.Round(d.Inverters.Select(i => i.AcDropPct).DefaultIfEmpty().Max(), 2), "%"));
        rows.Add(("max DC+AC voltage drop", Math.Round(MaxTotalDrop(d), 2), "%"));
        if (d.Stations.Count > 0) rows.Add(("max MV voltage drop", Math.Round(d.Stations.Max(t => t.MvDropPct), 2), "%"));
        rows.Add(("DC capacity", Math.Round(dcKwp, 1), "kWp"));
        rows.Add(("AC capacity", acKw, "kW"));
        rows.Add(("DC/AC ratio", acKw > 0 ? Math.Round(dcKwp / acKw, 3) : 0, ""));
        if (y != null)
        {
            rows.Add(($"energy P50{(y.ClearSky ? " (CLEAR-SKY upper bound)" : "")}", Math.Round(y.EnergyKwh / 1000), "MWh/yr"));
            rows.Add(("energy P90", Math.Round(y.P90Kwh / 1000), "MWh/yr"));
            rows.Add(("specific yield", Math.Round(y.SpecificYield), "kWh/kWp"));
            rows.Add(("performance ratio", Math.Round(y.PR * 100, 1), "%"));
            foreach (var (k, v) in y.LossesPct) rows.Add(($"  loss: {k}", Math.Round(v, 2), "%"));
        }
        foreach (var w in d.Warnings.Distinct()) rows.Add(("WARNING: " + w, 0, ""));
        return rows;
    }

    public static double DcCableLen(Unit u, Settings s)
    {
        double unitLen = s.Mount == MountType.Tracker ? u.Y1 - u.Y0 : u.X1 - u.X0;
        // each string: route + its offset along the unit, x2 polarities
        double sum = Enumerable.Range(0, u.Strings).Sum(k => RouteLen(u.DcRoute) + unitLen * (k + 0.5) / u.Strings);
        return sum * 2 * s.CableSlack;
    }

    public static void WriteCsv(string path, List<(string Item, double Qty, string UnitName)> rows)
    {
        var ci = System.Globalization.CultureInfo.CurrentCulture;
        var sep = ci.TextInfo.ListSeparator;   // ';' on Italian Windows -> Excel opens in columns
        File.WriteAllLines(path, rows.Select(r => $"{r.Item}{sep}{r.Qty.ToString(ci)}{sep}{r.UnitName}").Prepend($"item{sep}qty{sep}unit"),
            new System.Text.UTF8Encoding(true));
    }

    public static double MaxTotalDrop(Design d) =>
        d.Inverters.Select(i => i.AcDropPct + i.Units.Max(u => u.DcDropPct)).DefaultIfEmpty().Max();

    /// Scenario comparison: same site, different GCRs.
    public static List<(double Gcr, int Units, double Kwp, double Mwh, double SpecYield)> Compare(
        List<P> boundary, List<List<P>> exclusions, Settings s, IEnumerable<double> gcrs, Terrain? t, Weather w)
    {
        var res = new List<(double, int, double, double, double)>();
        foreach (var g in gcrs)
        {
            var s2 = Settings.FromXml(s.ToXml());
            s2.Gcr = g;
            var d = Run(boundary, exclusions, s2, null, t);
            var y = Yield.Run(d, s2, w);
            res.Add((g, d.Units.Count, y.DcKwp, y.EnergyKwh / 1000, y.SpecificYield));
        }
        return res;
    }
}
