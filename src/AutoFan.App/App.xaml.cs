using System.Windows;
using AutoFan.Hardware;

namespace AutoFan.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (WatchdogLoop.TryHandle(e.Args))
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        CrashRestore.RestoreIfDirty(new SoftwareControlFlag(), BiosFanRestorer.RestoreAll);

        var window = new MainWindow();
        window.Show();
    }
}
