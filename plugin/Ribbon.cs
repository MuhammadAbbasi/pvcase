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

    // name -> action (instance methods of Commands)
    internal static readonly (string Name, string Title, string Tip, Action Run)[] Tools =
    [
        ("PVSETTINGS", "Settings", "Design settings: module, tracker/fixed, GCR, setbacks, electrical, terrain, yield", () => new Commands().PvSettings()),
        ("PVLAYOUT", "Generate\nlayout", "Fill the site (layer PV_BOUNDARY, exclusions on PV_EXCLUSION) with trackers/tables, piles, inverters, cables, BOM and SLD", () => new Commands().PvLayout()),
        ("PVCOMPARE", "Compare\nGCR", "Compare several ground coverage ratios: kWp, MWh/yr, kWh/kWp", () => new Commands().PvCompare()),
        ("PVCLEAR", "Clear\nlayout", "Erase the generated layout (boundary and exclusions are kept)", () => new Commands().PvClear()),
    ];

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
                var run = t.Run;
                Autodesk.AutoCAD.Internal.Utils.AddCommand("PVLOCAL_RT", t.Name, t.Name, CommandFlags.Modal, () => run());
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

        var design = new RibbonPanelSource { Title = "Solar design" };
        design.Items.Add(Button(Tools[0], Icons.Settings));
        design.Items.Add(Button(Tools[1], Icons.Layout));
        design.Items.Add(Button(Tools[2], Icons.Compare));
        var edit = new RibbonPanelSource { Title = "Edit" };
        edit.Items.Add(Button(Tools[3], Icons.Clear));

        tab.Panels.Add(new RibbonPanel { Source = design });
        tab.Panels.Add(new RibbonPanel { Source = edit });
        ribbon.Tabs.Add(tab);
        Log("ribbon tab added");
    }

    static RibbonButton Button((string Name, string Title, string Tip, Action Run) t, ImageSource icon) => new()
    {
        Text = t.Title, Id = "PVLOCAL_" + t.Name, ShowText = true, ShowImage = true,
        LargeImage = icon, Image = icon, Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
        ToolTip = $"{t.Tip}\n\nCommand: {t.Name}", CommandHandler = new Handler(t.Name, t.Run),
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
    static readonly Brush Orange = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0x9A, 0x2E)));
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
}
