// Startup: PV Local ribbon tab + runtime command registration.
// Needed for OEM CADs (CADPILLAR) that load the plugin but ignore [CommandMethod] commands: the ribbon buttons run
// the code directly in command context, so they work even where typed commands are blocked.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(PVLocal.App))]

namespace PVLocal;

public class App : IExtensionApplication
{
    static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PVLocal", "pvlocal.log");

    public static void Log(string msg)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.AppendAllText(LogPath, $"{DateTime.Now:s} {msg}\n"); }
        catch (System.Exception) { /* logging must never break CAD startup */ }
    }

    public record Tool(string Name, string Panel, string Title, string Tip, Action Run, Func<ImageSource> Icon);

    // ribbon order = workflow order (same as PVcase: site -> design -> electrical)
    internal static readonly Tool[] Tools =
    [
        new("PVAREA", "Site", "Site\narea", "Select the closed polylines that bound the plant (moved to layer PV_BOUNDARY)", () => new Commands().PvArea(), () => Icons.Area),
        new("PVEXCLUDE", "Site", "Exclusion\nzones", "Select roads, wetlands, buildings, buffers as no-build areas (layer PV_EXCLUSION)", () => new Commands().PvExclude(), () => Icons.Exclude),
        new("PVMESH", "Site", "Fetch\nmesh", "Build the terrain from the drawing: 3D faces, meshes, points, survey blocks, contours. Saved as a DEM and used for piles", () => new Commands().PvMesh(), () => Icons.Mesh),
        new("PVTERRAIN", "Site", "Terrain\ncheck", "Slope map of the site along the tracker/table axis: green ok, yellow near the limit, red too steep", () => new Commands().PvTerrain(), () => Icons.Terrain),
        new("PVSETTINGS", "Design", "Settings", "Design settings: module, tracker/fixed, GCR, setbacks, piles, electrical, MV, yield", () => new Commands().PvSettings(), () => Icons.Settings),
        new("PVLAYOUT", "Design", "Generate\nlayout", "Fill the site area with trackers/tables and piles (on terrain if fetched)", () => new Commands().PvLayout(), () => Icons.Layout),
        new("PVCOMPARE", "Design", "Compare\nGCR", "Compare several ground coverage ratios: kWp, MWh/yr, kWh/kWp", () => new Commands().PvCompare(), () => Icons.Compare),
        new("PVDC", "Electrical", "DC\ncables", "String inverters and DC string cables for the trackers in the drawing (sized for voltage drop)", () => new Commands().PvDc(), () => Icons.Cable(Icons.Orange)),
        new("PVAC", "Electrical", "AC\ncables", "Transformer stations and LV AC cables inverter -> station", () => new Commands().PvAc(), () => Icons.Cable(Icons.Yellow)),
        new("PVMV", "Electrical", "MV\ncables", "Pick the substation, MV feeders station -> substation, trench", () => new Commands().PvMv(), () => Icons.Cable(Icons.Red)),
        new("PVBOM", "Electrical", "BOM\n+ SLD", "Bill of materials table + CSV, single line diagram, energy yield", () => new Commands().PvBom(), () => Icons.Bom),
        new("PVPLANT", "Plant", "Generate\nall", "Layout + DC + AC + MV + BOM/SLD in one go", () => new Commands().PvPlant(), () => Icons.Plant),
        new("PVCLEAR", "Plant", "Clear", "Erase everything generated (site area and exclusions are kept)", () => new Commands().PvClear(), () => Icons.Clear),
    ];

    /// Every entry point catches: an exception escaping a runtime-registered command can take the CAD down.
    static void Safe(string name, Action run)
    {
        try { run(); }
        catch (System.Exception ex)
        {
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nPV Local {name} error: {ex.Message}\n");
            Log($"{name}: {ex}");
        }
    }

    public void Initialize()
    {
        Log($"Initialize in {System.Diagnostics.Process.GetCurrentProcess().ProcessName}");
        try { RegisterMissingCommands(); } catch (System.Exception ex) { Log("commands: " + ex.Message); }
        try { UiHook(); } catch (System.Exception ex) { Log("ribbon: " + ex.Message); }   // no ribbon in accoreconsole
    }

    public void Terminate() { }

    static void RegisterMissingCommands()
    {
        foreach (var t in Tools)
        {
            if (Autodesk.AutoCAD.Internal.Utils.IsCommandDefined(t.Name)) { Log($"{t.Name}: defined"); continue; }
            try
            {
                var tool = t;
                Autodesk.AutoCAD.Internal.Utils.AddCommand("PVLOCAL_RT", t.Name, t.Name, CommandFlags.Modal, () => Safe(tool.Name, tool.Run));
                Log($"{t.Name}: added at runtime -> {Autodesk.AutoCAD.Internal.Utils.IsCommandDefined(t.Name)}");
            }
            catch (System.Exception ex) { Log($"{t.Name}: AddCommand failed: {ex.Message}"); }
        }
    }

    static void UiHook()
    {
        if (ComponentManager.Ribbon == null) ComponentManager.ItemInitialized += OnRibbonReady;
        else AddTab();
        // a workspace switch rebuilds the ribbon from CUI: put the tab back
        AcApp.SystemVariableChanged += (_, e) => { if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase)) AddTab(); };
    }

    static void OnRibbonReady(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        ComponentManager.ItemInitialized -= OnRibbonReady;
        AddTab();
    }

    static void AddTab()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon == null || ribbon.FindTab("PVLOCAL_TAB") != null) return;
        var tab = new RibbonTab { Title = "PV Local", Id = "PVLOCAL_TAB" };
        foreach (var g in Tools.GroupBy(t => t.Panel))
        {
            var src = new RibbonPanelSource { Title = g.Key };
            foreach (var t in g) src.Items.Add(Button(t));
            tab.Panels.Add(new RibbonPanel { Source = src });
        }
        ribbon.Tabs.Add(tab);
        Log("ribbon tab added");
    }

    static RibbonButton Button(Tool t) => new()
    {
        Text = t.Title, Id = "PVLOCAL_" + t.Name, ShowText = true, ShowImage = true,
        LargeImage = t.Icon(), Image = t.Icon(), Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
        ToolTip = $"{t.Tip}\n\nCommand: {t.Name}", CommandHandler = new Handler(t.Name, () => Safe(t.Name, t.Run)),
    };

    sealed class Handler(string name, Action run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (Autodesk.AutoCAD.Internal.Utils.IsCommandDefined(name))
                doc.SendStringToExecute(name + " ", true, false, true);   // normal path: undo + repeat behave as usual
            else
                // typed commands blocked (OEM): run the same code in command context
                _ = AcApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    try { run(); }
                    catch (System.Exception ex) { doc.Editor.WriteMessage($"\nPV Local error: {ex.Message}\n"); Log($"{name}: {ex}"); }
                    return Task.CompletedTask;
                }, null);
        }
    }
}

/// 32x32 ribbon icons drawn in code (no image files to ship).
static class Icons
{
    static ImageSource Draw(Action<DrawingContext> paint)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen()) paint(dc);
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v); bmp.Freeze();
        return bmp;
    }
    static readonly Brush Blue = Frozen(new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0xD2)));
    public static readonly Brush Orange = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0x9A, 0x2E)));
    public static readonly Brush Yellow = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xD0, 0x2A)));
    public static readonly Brush Red = Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x3C, 0x3C)));
    static readonly Brush Green = Frozen(new SolidColorBrush(Color.FromRgb(0x3C, 0xB4, 0x4B)));
    static readonly Brush Grey = Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC0)));
    static readonly Pen Line = Frozen(new Pen(Brushes.White, 1));
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    // solar array: 3 rows of panels with a sun
    public static readonly ImageSource Layout = Draw(dc =>
    {
        dc.DrawEllipse(Orange, null, new Point(25, 6), 5, 5);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                dc.DrawRectangle(Blue, Line, new Rect(2 + c * 8.5, 13 + r * 6.3, 8, 5.5));
    });
    // gear
    public static readonly ImageSource Settings = Draw(dc =>
    {
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawRectangle(Grey, null, new Rect(16 + 12 * Math.Cos(a) - 3, 16 + 12 * Math.Sin(a) - 3, 6, 6));
        }
        dc.DrawEllipse(Grey, null, new Point(16, 16), 10, 10);
        dc.DrawEllipse(Brushes.Transparent, new Pen(Blue, 3), new Point(16, 16), 4, 4);
    });
    // bar chart
    public static readonly ImageSource Compare = Draw(dc =>
    {
        double[] h = [12, 20, 26, 17];
        for (int i = 0; i < h.Length; i++) dc.DrawRectangle(i == 2 ? Orange : Blue, null, new Rect(3 + i * 7, 30 - h[i], 5, h[i]));
    });
    // panel with a red cross
    public static readonly ImageSource Clear = Draw(dc =>
    {
        dc.DrawRectangle(Blue, Line, new Rect(3, 8, 26, 16));
        var red = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x3C, 0x3C)), 4);
        dc.DrawLine(red, new Point(8, 6), new Point(24, 26)); dc.DrawLine(red, new Point(24, 6), new Point(8, 26));
    });

    static Geometry Poly(params double[] xy)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(xy[0], xy[1]), true, true);
            for (int i = 2; i < xy.Length; i += 2) c.LineTo(new Point(xy[i], xy[i + 1]), true, false);
        }
        g.Freeze();
        return g;
    }
    // site boundary: green outlined polygon
    public static readonly ImageSource Area = Draw(dc =>
        dc.DrawGeometry(Frozen(new SolidColorBrush(Color.FromArgb(70, 0x3C, 0xB4, 0x4B))), new Pen(Green, 2.5), Poly(3, 8, 20, 3, 29, 14, 25, 29, 6, 27)));
    // exclusion: red hatched polygon
    public static readonly ImageSource Exclude = Draw(dc =>
    {
        var red = new Pen(Red, 2);
        dc.DrawGeometry(null, red, Poly(5, 6, 27, 6, 27, 26, 5, 26));
        for (int i = 0; i < 4; i++) dc.DrawLine(new Pen(Red, 1.5), new Point(5 + i * 7, 26), new Point(12 + i * 7, 6));
    });
    // triangulated terrain mesh
    public static readonly ImageSource Mesh = Draw(dc =>
    {
        var pen = new Pen(Grey, 1.4);
        Point[] p = [new(3, 26), new(12, 14), new(20, 22), new(29, 10), new(10, 4), new(24, 3), new(29, 28), new(15, 30)];
        int[][] tri = [[0, 1, 2], [1, 2, 3], [1, 4, 5], [1, 5, 3], [2, 3, 6], [0, 2, 7], [2, 6, 7]];
        foreach (var t in tri) dc.DrawGeometry(Frozen(new SolidColorBrush(Color.FromArgb(60, 0x2D, 0x7D, 0xD2))), pen, Poly(p[t[0]].X, p[t[0]].Y, p[t[1]].X, p[t[1]].Y, p[t[2]].X, p[t[2]].Y));
    });
    // slope bands green / yellow / red
    public static readonly ImageSource Terrain = Draw(dc =>
    {
        dc.DrawGeometry(Green, null, Poly(2, 30, 30, 30, 30, 24, 2, 28));
        dc.DrawGeometry(Yellow, null, Poly(2, 28, 30, 24, 30, 15, 2, 22));
        dc.DrawGeometry(Red, null, Poly(2, 22, 30, 15, 30, 5, 2, 16));
    });
    // cable: coloured S-curve between two terminals
    public static ImageSource Cable(Brush b) => Draw(dc =>
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(new Point(5, 26), false, false); c.BezierTo(new Point(30, 26), new Point(2, 6), new Point(27, 6), true, false); }
        dc.DrawGeometry(null, new Pen(b, 4), g);
        dc.DrawRectangle(Grey, null, new Rect(1, 23, 7, 7)); dc.DrawRectangle(Grey, null, new Rect(24, 2, 7, 7));
    });
    // table
    public static readonly ImageSource Bom = Draw(dc =>
    {
        dc.DrawRectangle(Brushes.White, new Pen(Grey, 1), new Rect(3, 3, 26, 26));
        dc.DrawRectangle(Blue, null, new Rect(3, 3, 26, 6));
        for (int i = 1; i < 4; i++) dc.DrawLine(new Pen(Grey, 1), new Point(3, 9 + i * 5), new Point(29, 9 + i * 5));
        dc.DrawLine(new Pen(Grey, 1), new Point(18, 9), new Point(18, 29));
    });
    // full plant: panels + lightning
    public static readonly ImageSource Plant = Draw(dc =>
    {
        for (int r = 0; r < 2; r++) for (int c = 0; c < 3; c++) dc.DrawRectangle(Blue, Line, new Rect(1 + c * 8, 16 + r * 7, 7.5, 6));
        dc.DrawGeometry(Yellow, null, Poly(22, 1, 14, 13, 19, 13, 15, 23, 27, 9, 21, 9, 26, 1));
    });
}
