using System.Windows;
using System.Windows.Threading;
using SignalDx.Services;

namespace SignalDx;

public partial class App : Application
{
    static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromSeconds(5);

    protected override void OnStartup(StartupEventArgs e)
    {
        SettingsBackupService.ApplyPendingRestore();
        base.OnStartup(e);

        var splash = new SplashWindow();
        splash.Show();
        splash.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var shownAt = DateTime.UtcNow;
        var main = new MainWindow();
        MainWindow = main;
        main.Loaded += async (_, _) =>
        {
            var remaining = SplashMinimumDuration - (DateTime.UtcNow - shownAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);
            splash.Close();
            main.Activate();
        };
        main.Show();
    }
}
