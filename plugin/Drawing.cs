// What gets drawn, independent of how: AutoCAD plugin (AcadCanvas) or command script for locked OEM CADs (ScriptCanvas).
using System.Globalization;
using System.Text;
using P = (double X, double Y);

namespace PVLocal;

public interface ICanvas
{
    void Pline(IEnumerable<P> pts, bool closed, string layer, double width = 0);
    void Circle(double x, double y, double z, double r, string layer);
    void Line(double x1, double y1, double z1, double x2, double y2, double z2, string layer);
    void Text(double x, double y, double h, string text, string layer);
}

public static class Drawing
{
    public const string BoundaryLayer = "PV_BOUNDARY", ExclusionLayer = "PV_EXCLUSION";

    /// Generated layers (wiped before each run) -> ACI colour.
    public static readonly Dictionary<string, short> Generated = new()
    {
        ["PV_UNITS"] = 5, ["PV_STRINGS"] = 8, ["PV_PILES"] = 9, ["PV_INVERTERS"] = 6,
        ["PV_CABLE_DC"] = 30, ["PV_CABLE_AC"] = 2, ["PV_SUBSTATION"] = 1, ["PV_TEXT"] = 7, ["PV_BOM"] = 7,
        ["PV_PILES_FLAGGED"] = 1, ["PV_TUBES"] = 4, ["PV_SLD"] = 7,
    };

    static P[] Box(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    public static void Layout(ICanvas c, Design d, Settings s)
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
            c.Pline(u.DcRoute, false, "PV_CABLE_DC");
        }
        foreach (var inv in d.Inverters)
        {
            var (x, y) = inv.Pos;
            c.Pline(Box(x - 1, y - 0.5, x + 1, y + 0.5), true, "PV_INVERTERS");
            c.Text(x + 1.5, y, 1.5, $"{inv.Id} ({inv.Strings} str)", "PV_TEXT");
            c.Pline(inv.AcRoute, false, "PV_CABLE_AC");
        }
        if (d.Substation is { } sub)
        {
            c.Pline(Box(sub.X - 5, sub.Y - 3, sub.X + 5, sub.Y + 3), true, "PV_SUBSTATION");
            c.Text(sub.X - 5, sub.Y + 4, 2.5, "SUBSTATION", "PV_TEXT");
        }
    }

    /// Single-line diagram: transformer -> AC busbar -> inverters -> string groups.
    public static void Sld(ICanvas c, Design d, Settings s, double x0, double y0)
    {
        const double sp = 28;
        int n = d.Inverters.Count;
        if (n == 0) return;
        double busY = y0 - 25, width = (n - 1) * sp;
        void T(double x, double y, string txt, double ht = 2.0) => c.Text(x, y, ht, txt, "PV_SLD");

        T(x0, y0 + 14, $"SINGLE LINE DIAGRAM - {d.Units.Sum(u => u.Strings) * s.ModulesPerString * s.ModulePowerW / 1000:0.0} kWp DC / {n * s.InverterKw:0} kW AC", 3);
        double tx = x0 + width / 2;
        c.Circle(tx, y0 - 4, 0, 5, "PV_SLD");    // transformer: two circles
        c.Circle(tx, y0 - 11, 0, 5, "PV_SLD");
        T(tx + 7, y0 - 6, $"MV TRANSFORMER  LV {s.InverterAcVoltage:0} V");
        c.Pline([(tx, y0 - 16), (tx, busY)], false, "PV_SLD");
        c.Pline([(x0, busY), (x0 + width, busY)], false, "PV_SLD", 0.6);
        T(x0 + width + 3, busY - 1, $"AC BUS {s.InverterAcVoltage:0} V");
        for (int i = 0; i < n; i++)
        {
            var inv = d.Inverters[i];
            double x = x0 + i * sp, top = busY - 8;
            c.Pline([(x, busY), (x, top)], false, "PV_SLD");
            T(x + 0.8, busY - 5, $"3x{inv.AcSection} mm2 {inv.AcDropPct:0.0}%", 1.2);
            c.Pline(Box(x - 6, top - 10, x + 6, top), true, "PV_SLD");
            c.Pline([(x - 6, top - 10), (x + 6, top)], false, "PV_SLD");    // inverter symbol: box with diagonal
            T(x - 5.5, top - 2.5, "~", 2); T(x + 3.5, top - 9, "=", 2);
            T(x - 6, top - 13, inv.Id, 1.8);
            T(x - 6, top - 15.5, $"{s.InverterKw:0} kW", 1.4);
            c.Pline([(x, top - 10), (x, top - 20)], false, "PV_SLD");
            var secs = string.Join("/", inv.Units.Select(u => u.DcSection).Distinct().OrderBy(v => v));
            T(x - 6, top - 23, $"{inv.Strings} str x {s.ModulesPerString}", 1.4);
            T(x - 6, top - 25.5, $"DC {secs} mm2", 1.4);
            T(x - 6, top - 28, $"{inv.Units.Count} {(s.Mount == MountType.Tracker ? "trk" : "tbl")}", 1.4);
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
}

/// Writes an AutoCAD command script (.scr). Runs in any AutoCAD-based CAD, including OEM products that block
/// plugins and LISP (e.g. CADPILLAR): SCRIPT is a standard user command, no code is loaded.
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

    // TEXT reads the rest of the line as the string; height prompt exists because the Standard style has height 0
    public void Text(double x, double y, double h, string text, string layer) =>
        L(layer).Append($"_.TEXT {Pt(x, y)} {F(h)} 0 {Ascii(text)}\n");

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
