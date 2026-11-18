// What gets drawn, independent of how: AutoCAD plugin (AcadCanvas) or command script for locked OEM CADs (ScriptCanvas).
using System.Globalization;
using System.Text;
using P = (double X, double Y);

namespace PVLocal;

public enum TextAlign { Left, MiddleLeft, MiddleCenter, MiddleRight }

public interface ICanvas
{
    void Pline(IEnumerable<P> pts, bool closed, string layer, double width = 0);
    void Circle(double x, double y, double z, double r, string layer);
    void Line(double x1, double y1, double z1, double x2, double y2, double z2, string layer);
    /// rotation in degrees CCW; the insertion point is interpreted per alignment
    void Text(double x, double y, double h, string text, string layer, double rotation = 0, TextAlign align = TextAlign.Left);
    /// filled quad (terrain check) with its own ACI colour
    void Fill(P[] quad, short color, string layer);
}

public static class Drawing
{
    public const string BoundaryLayer = "PV_BOUNDARY", ExclusionLayer = "PV_EXCLUSION";

    /// Generated layers -> ACI colour, grouped by the step that creates them (a step clears itself and every later step).
    public static readonly (string Step, Dictionary<string, short> Layers)[] Steps =
    [
        ("layout", new() { ["PV_UNITS"] = 5, ["PV_STRINGS"] = 8, ["PV_PILES"] = 9, ["PV_PILES_FLAGGED"] = 1, ["PV_TUBES"] = 4 }),
        ("dc", new() { ["PV_CABLE_DC"] = 30, ["PV_INVERTERS"] = 6 }),
        ("ac", new() { ["PV_CABLE_AC"] = 2, ["PV_STATIONS"] = 210 }),
        ("mv", new() { ["PV_CABLE_MV"] = 1, ["PV_SUBSTATION"] = 1 }),
        ("report", new() { ["PV_BOM"] = 7, ["PV_SLD"] = 7 }),
    ];
    public const string TerrainLayer = "PV_TERRAIN_CHECK";

    public static readonly Dictionary<string, short> Generated =
        Steps.SelectMany(s => s.Layers).Append(new(TerrainLayer, 8)).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// Layers removed when re-running `step` (that step and everything downstream).
    public static IEnumerable<string> LayersFrom(string step) =>
        Steps.SkipWhile(s => s.Step != step).SelectMany(s => s.Layers.Keys);

    static P[] Box(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    /// Labels in the corridor between rows: vertical for trackers (N-S corridors), horizontal for fixed tables.
    static (double Rot, double Height) Corridor(Settings s) =>
        (s.Mount == MountType.Tracker ? 90 : 0, Math.Max(0.4, Math.Min(1.2, (Engine.RowPitch(s) - Engine.CollectorWidth(s)) * 0.3)));

    public static void Mechanical(ICanvas c, Design d, Settings s)
    {
        foreach (var u in d.Units)
        {
            c.Pline(Box(u.X0, u.Y0, u.X1, u.Y1), true, "PV_UNITS");
            for (int i = 1; i < u.Strings; i++)   // string divisions
            {
                if (s.Mount == MountType.Tracker) { double y = u.Y0 + i * (u.Y1 - u.Y0) / u.Strings; c.Pline([(u.X0, y), (u.X1, y)], false, "PV_STRINGS"); }
                else { double x = u.X0 + i * (u.X1 - u.X0) / u.Strings; c.Pline([(x, u.Y0), (x, u.Y1)], false, "PV_STRINGS"); }
            }
            // piles: plan circle + 3D post from ground to structure; flagged ones on their own red layer
            foreach (var p in u.Piles)
            {
                string layer = p.Reveal > s.PileMaxReveal + 1e-9 ? "PV_PILES_FLAGGED" : "PV_PILES";
                c.Circle(p.X, p.Y, p.Ground, 0.15, layer);
                c.Line(p.X, p.Y, p.Ground, p.X, p.Y, p.Top, layer);
            }
            // torque tube / beam: one 3D line per pile line along the unit
            foreach (var line in u.Piles.GroupBy(p => s.Mount == MountType.Tracker ? Math.Round(p.X, 3) : Math.Round(p.Y, 3)))
            {
                var ps = line.ToList(); var a = ps[0]; var b = ps[ps.Count - 1];
                c.Line(a.X, a.Y, a.Top, b.X, b.Y, b.Top, "PV_TUBES");
            }
        }
    }

    public static void Dc(ICanvas c, Design d, Settings s)
    {
        var (rot, h) = Corridor(s);
        bool tr = s.Mount == MountType.Tracker;
        foreach (var u in d.Units) c.Pline(u.DcRoute, false, "PV_CABLE_DC");
        foreach (var inv in d.Inverters)
        {
            var (x, y) = inv.Pos;
            c.Pline(tr ? Box(x - 0.5, y - 1, x + 0.5, y + 1) : Box(x - 1, y - 0.5, x + 1, y + 0.5), true, "PV_INVERTERS");
            // label starts just past the box and runs along the corridor
            c.Text(tr ? x : x + 1.5, tr ? y + 1.5 : y, h, $"{inv.Id}  {inv.Strings} str", "PV_INVERTERS", rot, TextAlign.MiddleLeft);
        }
    }

    public static void Ac(ICanvas c, Design d, Settings s)
    {
        var (rot, h) = Corridor(s);
        bool tr = s.Mount == MountType.Tracker;
        foreach (var inv in d.Inverters) c.Pline(inv.AcRoute, false, "PV_CABLE_AC");
        foreach (var t in d.Stations)
        {
            var (x, y) = t.Pos;
            c.Pline(tr ? Box(x - 1.2, y - 3, x + 1.2, y + 3) : Box(x - 3, y - 1.2, x + 3, y + 1.2), true, "PV_STATIONS");
            c.Text(tr ? x : x - 3.5, tr ? y - 3.5 : y, h * 1.2, $"{t.Id}  {t.Kw / 1000:0.0} MVA", "PV_STATIONS", rot, TextAlign.MiddleRight);   // ends just outside the box, away from the inverters
        }
    }

    public static void Mv(ICanvas c, Design d, Settings s)
    {
        foreach (var t in d.Stations) c.Pline(t.MvRoute, false, "PV_CABLE_MV", 0.3);
        if (d.Substation is { } sub)
        {
            c.Pline(Box(sub.X - 5, sub.Y - 3, sub.X + 5, sub.Y + 3), true, "PV_SUBSTATION");
            c.Text(sub.X, sub.Y + 5, 2.5, "SUBSTATION", "PV_SUBSTATION", 0, TextAlign.MiddleCenter);
        }
    }

    public static void All(ICanvas c, Design d, Settings s) { Mechanical(c, d, s); Dc(c, d, s); Ac(c, d, s); Mv(c, d, s); }

    /// Single-line diagram: substation -> MV bus -> transformer stations -> LV bus -> inverters -> strings.
    public static void Sld(ICanvas c, Design d, Settings s, double x0, double y0)
    {
        const double sp = 26, gap = 18, L = 1.4;
        if (d.Inverters.Count == 0) return;
        void T(double x, double y, string txt, double ht = L) => c.Text(x, y, ht, txt, "PV_SLD", 0, TextAlign.MiddleCenter);
        void Ln(double xa, double ya, double xb, double yb, double w = 0) => c.Pline([(xa, ya), (xb, yb)], false, "PV_SLD", w);
        void Tx(double x, double y)   // transformer symbol: two overlapping circles
        { c.Circle(x, y - 4, 0, 4.5, "PV_SLD"); c.Circle(x, y - 10, 0, 4.5, "PV_SLD"); }

        var stations = d.Stations.Count > 0 ? d.Stations : [new Station { Id = "TS", Inverters = d.Inverters, Kw = d.Inverters.Count * s.InverterKw }];
        double width = stations.Sum(t => (t.Inverters.Count - 1) * sp) + gap * (stations.Count - 1);
        double mvY = y0 - 30, mid = x0 + width / 2;
        double dc = d.Units.Sum(u => u.Strings) * s.ModulesPerString * s.ModulePowerW / 1000;

        c.Text(x0, y0 + 16, 3, $"SINGLE LINE DIAGRAM  -  {dc / 1000:0.00} MWp DC / {d.Inverters.Count * s.InverterKw / 1000:0.00} MW AC", "PV_SLD", 0, TextAlign.MiddleLeft);
        c.Pline(Box(mid - 9, y0 - 4, mid + 9, y0 + 6), true, "PV_SLD");
        T(mid, y0 + 1, "SUBSTATION", 2);
        Ln(mid, y0 - 4, mid, mvY);
        Ln(x0, mvY, x0 + width, mvY, 0.6);
        c.Text(x0 + width + 3, mvY, 1.8, $"MV BUS {s.MvVoltage / 1000:0.#} kV", "PV_SLD", 0, TextAlign.MiddleLeft);

        double x = x0;
        foreach (var t in stations)
        {
            double w = (t.Inverters.Count - 1) * sp, tx = x + w / 2, top = mvY - 6, lvY = top - 26;
            Ln(tx, mvY, tx, top);
            if (t.MvSection > 0) c.Text(tx + 1, mvY - 3, 1.1, $"MV 3x{t.MvSection} mm2  {t.MvDropPct:0.00}%", "PV_SLD", 0, TextAlign.MiddleLeft);
            Tx(tx, top);
            c.Text(tx + 6, top - 7, 1.6, $"{t.Id}  {t.Kw / 1000:0.0} MVA", "PV_SLD", 0, TextAlign.MiddleLeft);
            Ln(tx, top - 14.5, tx, lvY);
            Ln(x - 4, lvY, x + w + 4, lvY, 0.4);
            for (int i = 0; i < t.Inverters.Count; i++)
            {
                var inv = t.Inverters[i];
                double ix = x + i * sp, ib = lvY - 10;
                Ln(ix, lvY, ix, ib);
                T(ix, lvY - 5, $"3x{inv.AcSection} mm2", 1.0);
                c.Pline(Box(ix - 5, ib - 9, ix + 5, ib), true, "PV_SLD");
                Ln(ix - 5, ib - 9, ix + 5, ib);   // inverter symbol: box with diagonal
                T(ix - 2.8, ib - 2.2, "~", 1.8); T(ix + 2.8, ib - 6.8, "=", 1.8);
                T(ix, ib - 11.5, inv.Id, 1.5);
                T(ix, ib - 13.8, $"{s.InverterKw:0} kW", 1.2);
                Ln(ix, ib - 15.5, ix, ib - 19);
                var secs = string.Join("/", inv.Units.Select(u => u.DcSection).Distinct().OrderBy(v => v));
                T(ix, ib - 21, $"{inv.Strings} str x {s.ModulesPerString}", 1.2);
                T(ix, ib - 23, $"DC {secs} mm2", 1.2);
            }
            x += w + gap;
        }
    }

    /// BOM as plain text lines (script output; the plugin uses a native table instead).
    public static void BomText(ICanvas c, List<(string Item, double Qty, string UnitName)> bom, double x, double y)
    {
        c.Text(x, y, 3, "PV BILL OF MATERIALS", "PV_BOM");
        for (int i = 0; i < bom.Count; i++)
        {
            double ry = y - 6 - i * 3;
            c.Text(x, ry, 1.8, bom[i].Item.Trim(), "PV_BOM");
            c.Text(x + 75, ry, 1.8, $"{bom[i].Qty:#,0.##} {bom[i].UnitName}", "PV_BOM");
        }
    }

    /// Slope map: green <= half the limit, yellow <= limit, red above. Returns share of area per class.
    public static (double Ok, double Warn, double Bad) TerrainCheck(ICanvas c, List<(double X0, double Y0, double X1, double Y1, double SlopePct)> cells, Settings s, double legendX, double legendY)
    {
        int ok = 0, warn = 0, bad = 0;
        foreach (var k in cells)
        {
            short col = k.SlopePct <= s.MaxUnitSlopePct / 2 ? (short)3 : k.SlopePct <= s.MaxUnitSlopePct ? (short)2 : (short)1;
            if (col == 3) ok++; else if (col == 2) warn++; else bad++;
            c.Fill(Box(k.X0, k.Y0, k.X1, k.Y1), col, TerrainLayer);
        }
        int n = Math.Max(1, cells.Count);
        string axis = s.Mount == MountType.Tracker ? "N-S (tracker axis)" : "E-W (table axis)";
        c.Text(legendX, legendY, 2.5, $"TERRAIN CHECK - slope {axis}", TerrainLayer, 0, TextAlign.MiddleLeft);
        (short Col, string Txt)[] legend =
        [
            (3, $"<= {s.MaxUnitSlopePct / 2:0.#}%   {100.0 * ok / n:0}% of area"),
            (2, $"<= {s.MaxUnitSlopePct:0.#}%   {100.0 * warn / n:0}% of area"),
            (1, $">  {s.MaxUnitSlopePct:0.#}%   {100.0 * bad / n:0}% of area"),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double ly = legendY - 6 - i * 5;
            c.Fill(Box(legendX, ly - 1.5, legendX + 3, ly + 1.5), legend[i].Col, TerrainLayer);
            c.Text(legendX + 5, ly, 1.8, legend[i].Txt, TerrainLayer, 0, TextAlign.MiddleLeft);
        }
        return (100.0 * ok / n, 100.0 * warn / n, 100.0 * bad / n);
    }
}

/// Writes an AutoCAD command script (.scr). Runs in any AutoCAD-based CAD: SCRIPT is a standard user command.
public class ScriptCanvas : ICanvas
{
    // grouped per layer so the script switches layer once per group
    readonly Dictionary<string, StringBuilder> byLayer = new();
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Pt(double x, double y) => $"_non {F(x)},{F(y)}";
    static string Pt(double x, double y, double z) => $"_non {F(x)},{F(y)},{F(z)}";
    StringBuilder L(string layer) => byLayer.TryGetValue(layer, out var sb) ? sb : byLayer[layer] = new StringBuilder();

    public void Pline(IEnumerable<P> pts, bool closed, string layer, double width = 0)
    {
        var p = pts.ToList();
        if (p.Count < 2) return;
        var sb = L(layer).Append("_.PLINE ").Append(Pt(p[0].X, p[0].Y));
        sb.Append($" _W {F(width)} {F(width)}");   // always set: PLINE remembers the last width
        foreach (var q in p.Skip(1)) sb.Append(' ').Append(Pt(q.X, q.Y));
        sb.Append(closed ? " _C\n" : " \n");
    }

    public void Circle(double x, double y, double z, double r, string layer) =>
        L(layer).Append($"_.CIRCLE {Pt(x, y, z)} {F(r)}\n");

    public void Line(double x1, double y1, double z1, double x2, double y2, double z2, string layer) =>
        L(layer).Append($"_.LINE {Pt(x1, y1, z1)} {Pt(x2, y2, z2)} \n");

    // TEXT reads the rest of the line as the string; height prompt exists because PV_TEXT style has height 0
    public void Text(double x, double y, double h, string text, string layer, double rotation = 0, TextAlign align = TextAlign.Left)
    {
        string j = align switch { TextAlign.MiddleLeft => "_J _ML ", TextAlign.MiddleCenter => "_J _MC ", TextAlign.MiddleRight => "_J _MR ", _ => "" };
        L(layer).Append($"_.TEXT {j}{Pt(x, y)} {F(h)} {F(rotation)} {Ascii(text)}\n");
    }

    public void Fill(P[] q, short color, string layer) =>   // SOLID takes corners in Z order: 1, 2, 4, 3
        L(layer).Append($"_.SOLID {Pt(q[0].X, q[0].Y)} {Pt(q[1].X, q[1].Y)} {Pt(q[3].X, q[3].Y)} {Pt(q[2].X, q[2].Y)} \n_.CHPROP _L  _C {color} \n");

    // scripts are read with the system code page: keep text ASCII (%%d = AutoCAD degree sign)
    static string Ascii(string t) => new string(t.Replace("\n", " ").Replace("°", "%%d").Replace("²", "2")
        .Select(ch => ch < 128 ? ch : '?').ToArray());

    public string Build()
    {
        var sb = new StringBuilder();
        var gen = Drawing.Generated;
        sb.Append("_.UNDO _BE\n");
        sb.Append("_.-STYLE PV_TEXT arial.ttf 0 1 0 _N _N\n");   // own style with height 0 -> TEXT always asks height
        // create every PV layer, make 0 current, delete the generated ones (wipes the previous run), recreate
        sb.Append("_.-LAYER");
        foreach (var n in gen.Keys.Append(Drawing.BoundaryLayer).Append(Drawing.ExclusionLayer)) sb.Append($" _M {n}");
        sb.Append(" _S 0 \n");
        sb.Append("_.-LAYDEL\n");   // LAYDEL takes the whole line as a layer name: one token per line
        foreach (var n in gen.Keys) sb.Append($"_N\n{n}\n");
        sb.Append("\n_Y\n");
        sb.Append("_.-LAYER");
        foreach (var (n, col) in gen) sb.Append($" _M {n} _C {col} {n}");
        sb.Append($" _C 3 {Drawing.BoundaryLayer} _C 1 {Drawing.ExclusionLayer} \n");
        foreach (var (layer, body) in byLayer)
            sb.Append($"_.-LAYER _S {layer} \n").Append(body);
        sb.Append("_.-LAYER _S 0 \n_.UNDO _E\n_.ZOOM _E\n");
        return sb.ToString();
    }
}
