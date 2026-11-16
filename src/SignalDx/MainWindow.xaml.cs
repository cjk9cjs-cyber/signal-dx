using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SignalDx.Models;
using SignalDx.ViewModels;

namespace SignalDx;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ShowInTaskbar = true;
        RestoreWindowPlacement();
        Loaded += OnLoaded;
        Closing += (_, _) =>
        {
            PersistWindowPlacement();
            _vm.SaveSettingsNow();
        };
        Closed += (_, _) => _vm.Dispose();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _vm.AttachWaterfall(Waterfall);
        Activate();
        _vm.StartAfterUiReady();
    }

    void RestoreWindowPlacement()
    {
        if (!_vm.TryGetWindowPlacement(out double left, out double top, out double width, out double height, out bool maximized))
            return;

        Width = Math.Clamp(width, MinWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Clamp(height, MinHeight, SystemParameters.VirtualScreenHeight);

        if (!double.IsNaN(left) && !double.IsNaN(top) && IsOnScreen(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            // Saved position is off the virtual desktop (e.g. unplugged monitor) — center on primary.
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (maximized)
            Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    void PersistWindowPlacement()
    {
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _vm.CaptureWindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowState == WindowState.Maximized);
    }

    static bool IsOnScreen(double left, double top, double width, double height)
    {
        // Title-bar probe must intersect the virtual desktop (covers multi-monitor).
        var probe = new Rect(left + 8, top + 8, Math.Max(32, width - 16), 48);
        var virtualDesktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        return virtualDesktop.IntersectsWith(probe);
    }

    void Activity_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EnableTxFromGrid(sender);

    void RxActivity_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EnableTxFromGrid(sender);

    void EnableTxFromGrid(object sender)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is DecodeHit hit)
            _vm.EnableTxForStation(hit);
    }
}
