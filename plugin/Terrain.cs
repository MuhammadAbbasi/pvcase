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

    /// Bilinear height; outside the grid clamps to the edge.
    public double At(double x, double y)
    {
        double fx = Mx.Clamp((x - X0) / Cell, 0, Cols - 1.000001), fy = Mx.Clamp((y - Y0) / Cell, 0, Rows - 1.000001);
        int c = (int)fx, r = (int)fy; double dx = fx - c, dy = fy - r;
        return Z[r, c] * (1 - dx) * (1 - dy) + Z[r, c + 1] * dx * (1 - dy) + Z[r + 1, c] * (1 - dx) * dy + Z[r + 1, c + 1] * dx * dy;
    }
}
