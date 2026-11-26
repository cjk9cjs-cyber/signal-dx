using System.Windows;
using SignalDx.Services;

namespace SignalDx;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SettingsBackupService.ApplyPendingRestore();
        base.OnStartup(e);
    }
}
