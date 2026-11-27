using System.Windows;

namespace SignalDx;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{AppInfo.Version}";
        CreatorText.Text = $"By {AppInfo.CreatorCall}";
    }
}
