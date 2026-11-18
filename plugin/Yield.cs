// Hourly energy yield. Sun position (NOAA), 1-axis tracking with backtracking, 2D row-to-row shading,
// isotropic sky, ASHRAE IAM, NOCT cell temperature, inverter efficiency + clipping, wiring from the cable sizing.
// ponytail: no terrain horizon / 3D ray-tracing, no bifacial, no electrical shading mismatch. Good to ~3-5% vs PVsyst
// on flat-ish sites; for bankable P50 export to PVsyst.
using System.Globalization;

namespace PVLocal;

public record WeatherHour(int DayOfYear, double HourUtc, double Ghi, double Dni, double Dhi, double TempC);

public class Weather
{
    public List<WeatherHour> Hours = new();
    public bool ClearSky;

    /// PVGIS TMY CSV export (time(UTC),T2m,RH,G(h),Gb(n),Gd(h),...). A downloaded file - no runtime web access.
    public static Weather LoadPvgisTmy(string path)
    {
        var w = new Weather();
        string[]? cols = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("time(UTC)")) { cols = line.Split(','); continue; }
            if (cols == null) continue;
            var f = line.Split(',');
            if (f.Length != cols.Length || f[0].Length < 13 || !char.IsDigit(f[0][0])) { if (w.Hours.Count > 0) break; continue; }
            double V(string name) => double.Parse(f[Array.IndexOf(cols, name)], CultureInfo.InvariantCulture);
            int month = int.Parse(f[0].Substring(4, 2)), day = int.Parse(f[0].Substring(6, 2)), hour = int.Parse(f[0].Substring(9, 2));
            int doy = new DateTime(2023, month, day).DayOfYear;
            w.Hours.Add(new(doy, hour + 0.5, V("G(h)"), V("Gb(n)"), V("Gd(h)"), V("T2m")));
        }
        if (w.Hours.Count < 8000) throw new InvalidDataException($"PVGIS TMY: read {w.Hours.Count} hours, expected 8760");
        return w;
    }

    /// Cloudless-sky year (Meinel DNI). Upper bound only: use a TMY file for P50.
    public static Weather ClearSkyYear(double lat, double lon)
    {
        var w = new Weather { ClearSky = true };
        for (int d = 1; d <= 365; d++)
            for (int h = 0; h < 24; h++)
            {
                var (zen, _) = Sun.Position(d, h + 0.5, lat, lon);
                double cz = Math.Cos(zen);
                if (cz <= 0.01) { w.Hours.Add(new(d, h + 0.5, 0, 0, 0, 15)); continue; }
                double dni = 1353 * Math.Pow(0.7, Math.Pow(1 / cz, 0.678));
                double dhi = 0.1 * dni;
                w.Hours.Add(new(d, h + 0.5, dni * cz + dhi, dni, dhi, 15 + 10 * Math.Sin((d - 110) / 365.0 * 2 * Math.PI)));
            }
        return w;
    }
}

public static class Sun
{
    /// NOAA solar position. Returns (zenith, azimuth from north clockwise), radians.
    public static (double Zenith, double Azimuth) Position(int doy, double hourUtc, double latDeg, double lonDeg)
    {
        double g = 2 * Math.PI / 365 * (doy - 1 + (hourUtc - 12) / 24);
        double eqt = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                    + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        double ha = ((hourUtc * 60 + eqt + 4 * lonDeg) / 4 - 180) * Math.PI / 180;
        double lat = latDeg * Math.PI / 180;
        double cz = Mx.Clamp(Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha), -1, 1);
        double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat)) + Math.PI;
        return (Math.Acos(cz), az);
    }
}

public class YieldResult
{
    public double DcKwp, EnergyKwh, P90Kwh, SpecificYield, PR, PoaKwhM2;
    public bool ClearSky;
    public List<(string Name, double Pct)> LossesPct = new();
}

public static class Yield
{
    /// Unshaded fraction of a row: rows of width W tilted beta, pitch P, sun profile elevation alpha (in the plane across rows).
    public static double ShadedFraction(double alpha, double beta, double gcr)
    {
        if (alpha <= 0) return 1;
        double unshaded = Math.Sin(alpha) / gcr / Math.Sin(alpha + beta);   // in units of row width
        return Mx.Clamp(1 - unshaded, 0, 1);
    }

    /// Tracker rotation (rad, + = facing east) for N-S horizontal axis, with optional backtracking.
    public static double TrackerAngle(double sx, double sz, Settings s)
    {
        double ideal = Math.Atan2(sx, sz);
        double w = ideal;
        if (s.Backtracking)
        {
            double c = Math.Abs(Math.Cos(ideal)) / s.Gcr;
            if (c < 1) w = ideal - Math.Sign(ideal) * Math.Acos(c);
        }
        double max = s.MaxTrackerAngle * Math.PI / 180;
        return Mx.Clamp(w, -max, max);
    }

    public static YieldResult Run(Design d, Settings s, Weather w)
    {
        double dcKwp = d.Units.Sum(u => u.Strings) * s.ModulesPerString * s.ModulePowerW / 1000;
        double acKw = d.Inverters.Count * s.InverterKw;
        double dcWire = d.Units.Count == 0 ? 0 : d.Units.Average(u => u.DcDropPct) * 0.6;   // avg drop at Imp; ~0.6 energy-weighted
        double acWire = d.Inverters.Count == 0 ? 0 : d.Inverters.Average(i => i.AcDropPct) * 0.6;
        bool tracker = s.Mount == MountType.Tracker;
        double tilt = s.TiltDeg * Math.PI / 180;

        double nominal = 0, eShade = 0, eIam = 0, eSoil = 0, eTemp = 0, eMism = 0, eDcW = 0, eInv = 0, eClip = 0, eAc = 0, poaSum = 0;
        foreach (var h in w.Hours)
        {
            if (h.Ghi <= 0) continue;
            var (zen, az) = Sun.Position(h.DayOfYear, h.HourUtc, s.Latitude, s.Longitude);
            double sx = Math.Sin(zen) * Math.Sin(az), sy = Math.Sin(zen) * Math.Cos(az), sz = Math.Cos(zen);
            double nx, ny, nz, beta, fShade;
            if (tracker)
            {
                double rot = TrackerAngle(sx, sz, s);
                (nx, ny, nz, beta) = (Math.Sin(rot), 0, Math.Cos(rot), Math.Abs(rot));
                double profile = Math.PI / 2 - Math.Abs(Math.Atan2(sx, sz));   // sun elevation in the E-W plane
                fShade = sz > 0 ? ShadedFraction(profile, beta, s.Gcr) : 1;
            }
            else
            {
                (nx, ny, nz, beta) = (0, -Math.Sin(tilt), Math.Cos(tilt), tilt);
                double profile = sy < 0 ? Math.Atan2(sz, -sy) : Math.PI / 2;   // sun behind the row -> no front beam anyway
                fShade = sz > 0 ? ShadedFraction(profile, beta, s.Gcr) : 1;
            }
            double cosInc = Math.Max(0, nx * sx + ny * sy + nz * sz);
            double beam = sz > 0 ? h.Dni * cosInc : 0;
            double sky = h.Dhi * (1 + Math.Cos(beta)) / 2, ground = h.Ghi * s.Albedo * (1 - Math.Cos(beta)) / 2;
            double poa = beam + sky + ground;
            poaSum += poa;
            double iam = cosInc > 0 ? Math.Max(0, 1 - s.IamB0 * (1 / cosInc - 1)) : 0;

            // energy cascade (kWh for this hour); each step records its loss
            double e0 = dcKwp * poa / 1000; nominal += e0;
            double e1 = dcKwp * (beam * (1 - fShade) + sky + ground) / 1000; eShade += e0 - e1;
            double e2 = e1 - dcKwp * beam * (1 - fShade) * (1 - iam) / 1000; eIam += e1 - e2;
            double e3 = e2 * (1 - s.SoilingPct / 100); eSoil += e2 - e3;
            double tc = h.TempC + poa * (s.ModuleNoctC - 20) / 800;
            double e4 = e3 * (1 + s.TempCoeffPmaxPct / 100 * (tc - 25)); eTemp += e3 - e4;
            double e5 = e4 * (1 - s.MismatchPct / 100); eMism += e4 - e5;
            double e6 = e5 * (1 - dcWire / 100); eDcW += e5 - e6;
            double e7 = e6 * s.InverterEffPct / 100; eInv += e6 - e7;
            double e8 = Math.Min(e7, acKw); eClip += e7 - e8;
            double e9 = e8 * (1 - acWire / 100); eAc += e8 - e9;
        }
        double energy = nominal - eShade - eIam - eSoil - eTemp - eMism - eDcW - eInv - eClip - eAc;
        double Pct(double x) => nominal > 0 ? x / nominal * 100 : 0;
        return new YieldResult
        {
            DcKwp = dcKwp, EnergyKwh = energy, ClearSky = w.ClearSky,
            P90Kwh = energy * (1 - 1.2816 * s.YieldUncertaintyPct / 100),
            SpecificYield = dcKwp > 0 ? energy / dcKwp : 0,
            PoaKwhM2 = poaSum / 1000,
            PR = nominal > 0 ? energy / nominal : 0,
            LossesPct =
            [
                ("row shading", Pct(eShade)), ("IAM", Pct(eIam)), ("soiling", Pct(eSoil)), ("temperature", Pct(eTemp)),
                ("mismatch", Pct(eMism)), ("DC wiring", Pct(eDcW)), ("inverter efficiency", Pct(eInv)),
                ("inverter clipping", Pct(eClip)), ("AC wiring", Pct(eAc)),
            ],
        };
    }
}
