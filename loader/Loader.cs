// PV Local loader: the only DLL the CAD loads (and locks). It reads PVLocal.dll from the same folder into memory,
// so PVLocal.dll can be replaced while the CAD runs; PVRELOAD loads the new copy without a restart.
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(PVLocalLoader.Loader))]

namespace PVLocalLoader;

public class Loader : IExtensionApplication
{
    static readonly string Dir = Path.GetDirectoryName(typeof(Loader).Assembly.Location)!;

    public void Initialize()
    {
        Load();
        try
        {
            Autodesk.AutoCAD.Internal.Utils.AddCommand("PVLOCAL_LOADER", "PVRELOAD", "PVRELOAD", CommandFlags.Modal, () =>
            {
                var ok = Load();
                Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage(ok ? "\nPV Local reloaded from " + Path.Combine(Dir, "PVLocal.dll") + "\n" : "\nPV Local reload failed: see %APPDATA%\\PVLocal\\pvlocal.log\n");
            });
        }
        catch (System.Exception) { /* PVRELOAD already registered */ }
    }

    public void Terminate() { }

    /// Load PVLocal.dll from bytes (file stays unlocked) and run its startup (commands + ribbon).
    static bool Load()
    {
        try
        {
            var dll = Path.Combine(Dir, "PVLocal.dll");
            var pdb = Path.ChangeExtension(dll, ".pdb");
            var asm = File.Exists(pdb) ? Assembly.Load(File.ReadAllBytes(dll), File.ReadAllBytes(pdb)) : Assembly.Load(File.ReadAllBytes(dll));
            var app = (IExtensionApplication)Activator.CreateInstance(asm.GetType("PVLocal.App", throwOnError: true)!)!;
            app.Initialize();
            return true;
        }
        catch (System.Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PVLocal", "pvlocal.log"), $"{DateTime.Now:s} loader: {ex}\n"); }
            catch (System.Exception) { }
            return false;
        }
    }
}
