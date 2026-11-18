// DEM terrain: ESRI ASCII grid (.asc) - the plain-text DEM format every GIS (QGIS, Global Mapper, Civil 3D) exports.
namespace PVLocal;

public class Terrain
{
    public int Cols, Rows;
    public double X0, Y0, Cell;      // lower-left cell CENTRE
    public double[,] Z = new double[0, 0];   // [row from south, col]

    public static Terrain LoadAsc(string path)
    {
        var lines = File.ReadAllLines(path);
        var head = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        for (; i < lines.Length; i++)
        {
            var parts = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && char.IsLetter(parts[0][0])) head[parts[0]] = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            else break;
        }
        var t = new Terrain { Cols = (int)head["ncols"], Rows = (int)head["nrows"], Cell = head["cellsize"] };
        t.X0 = head.TryGetValue("xllcenter", out var xc) ? xc : head["xllcorner"] + t.Cell / 2;
        t.Y0 = head.TryGetValue("yllcenter", out var yc) ? yc : head["yllcorner"] + t.Cell / 2;
        double nodata = head.TryGetValue("nodata_value", out var nd) ? nd : -9999;
        var vals = string.Join(" ", lines.Skip(i)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (vals.Length != t.Cols * t.Rows) throw new InvalidDataException($"DEM: expected {t.Cols * t.Rows} values, got {vals.Length}");
        if (vals.Any(v => v == nodata)) throw new InvalidDataException("DEM has NODATA cells inside the grid; fill them in GIS first");
        t.Z = new double[t.Rows, t.Cols];
        for (int r = 0; r < t.Rows; r++)          // file is written north row first
            for (int c = 0; c < t.Cols; c++) t.Z[t.Rows - 1 - r, c] = vals[r * t.Cols + c];
        return t;
    }

    public static void SaveAsc(string path, double x0, double y0, double cell, int cols, int rows, Func<double, double, double> z)
    {
        using var w = new StreamWriter(path);
        w.WriteLine(FormattableString.Invariant($"ncols {cols}\nnrows {rows}\nxllcenter {x0}\nyllcenter {y0}\ncellsize {cell}\nNODATA_value -9999"));
        for (int r = rows - 1; r >= 0; r--)
            w.WriteLine(string.Join(" ", Enumerable.Range(0, cols).Select(c => z(x0 + c * cell, y0 + r * cell).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
    }

    public void SaveAsc(string path) =>
        SaveAsc(path, X0, Y0, Cell, Cols, Rows, (x, y) => Z[(int)Math.Round((y - Y0) / Cell), (int)Math.Round((x - X0) / Cell)]);

    /// DEM from a CAD terrain model: triangles (3D faces / meshes) sampled exactly, the rest filled from the
    /// nearest sample point (survey points, contour vertices, triangle corners).
    // ponytail: nearest-point fill for gaps (no Delaunay of loose points); fine for dense survey data
    public static Terrain FromSamples(List<(double X, double Y, double Z)[]> tris, List<(double X, double Y, double Z)> pts,
        double minX, double minY, double maxX, double maxY, double cell)
    {
        int cols = (int)Math.Ceiling((maxX - minX) / cell) + 1, rows = (int)Math.Ceiling((maxY - minY) / cell) + 1;
        if ((long)cols * rows > 6_000_000) throw new InvalidDataException($"grid {cols}x{rows} too big: increase TerrainCell");
        var samples = pts.Concat(tris.SelectMany(t => t)).ToList();
        if (samples.Count == 0) throw new InvalidDataException("no 3D faces, meshes, points or contours with elevation selected");
        var t = new Terrain { Cols = cols, Rows = rows, X0 = minX, Y0 = minY, Cell = cell, Z = new double[rows, cols] };

        double bs = cell * 16;   // bucket size for the spatial index
        int bc = (int)((maxX - minX) / bs) + 1, br = (int)((maxY - minY) / bs) + 1;
        int Bx(double x) => Math.Max(0, Math.Min(bc - 1, (int)((x - minX) / bs)));
        int By(double y) => Math.Max(0, Math.Min(br - 1, (int)((y - minY) / bs)));
        var triB = new List<int>?[br, bc];
        for (int k = 0; k < tris.Count; k++)
        {
            var tr = tris[k];
            for (int r = By(tr.Min(v => v.Y)); r <= By(tr.Max(v => v.Y)); r++)
                for (int c = Bx(tr.Min(v => v.X)); c <= Bx(tr.Max(v => v.X)); c++)
                    (triB[r, c] ??= new()).Add(k);
        }
        var ptB = new List<int>?[br, bc];
        for (int k = 0; k < samples.Count; k++)
            if (samples[k].X >= minX - bs && samples[k].X <= maxX + bs && samples[k].Y >= minY - bs && samples[k].Y <= maxY + bs)
                (ptB[By(samples[k].Y), Bx(samples[k].X)] ??= new()).Add(k);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double x = minX + c * cell, y = minY + r * cell;
                t.Z[r, c] = InTriangle(x, y) ?? Nearest(x, y);
            }
        return t;

        double? InTriangle(double x, double y)
        {
            var list = triB[By(y), Bx(x)];
            if (list == null) return null;
            foreach (var k in list)
            {
                var (a, b, cc) = (tris[k][0], tris[k][1], tris[k][2]);
                double den = (b.Y - cc.Y) * (a.X - cc.X) + (cc.X - b.X) * (a.Y - cc.Y);
                if (Math.Abs(den) < 1e-12) continue;
                double l1 = ((b.Y - cc.Y) * (x - cc.X) + (cc.X - b.X) * (y - cc.Y)) / den;
                double l2 = ((cc.Y - a.Y) * (x - cc.X) + (a.X - cc.X) * (y - cc.Y)) / den, l3 = 1 - l1 - l2;
                if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * a.Z + l2 * b.Z + l3 * cc.Z;
            }
            return null;
        }

        double Nearest(double x, double y)
        {
            int bx = Bx(x), by = By(y);
            double best = double.MaxValue, z = 0;
            for (int ring = 0; ring < Math.Max(bc, br); ring++)
            {
                for (int r = by - ring; r <= by + ring; r++)
                    for (int c = bx - ring; c <= bx + ring; c++)
                    {
                        if (r < 0 || c < 0 || r >= br || c >= bc || (Math.Abs(r - by) != ring && Math.Abs(c - bx) != ring)) continue;
                        foreach (var k in ptB[r, c] ?? [])
                        {
                            double d2 = (samples[k].X - x) * (samples[k].X - x) + (samples[k].Y - y) * (samples[k].Y - y);
                            if (d2 < best) { best = d2; z = samples[k].Z; }
                        }
                    }
                if (best < double.MaxValue && Math.Sqrt(best) < ring * bs) break;   // nothing closer in outer rings
            }
            return best < double.MaxValue ? z : samples.MinBy(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)).Z;
        }
    }

    /// Bilinear height; outside the grid clamps to the edge.
    public double At(double x, double y)
    {
        double fx = Mx.Clamp((x - X0) / Cell, 0, Cols - 1.000001), fy = Mx.Clamp((y - Y0) / Cell, 0, Rows - 1.000001);
        int c = (int)fx, r = (int)fy; double dx = fx - c, dy = fy - r;
        return Z[r, c] * (1 - dx) * (1 - dy) + Z[r, c + 1] * dx * (1 - dy) + Z[r + 1, c] * (1 - dx) * dy + Z[r + 1, c + 1] * dx * dy;
    }
}
