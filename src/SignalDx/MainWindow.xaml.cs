using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SignalDx.Models;
using SignalDx.ViewModels;

namespace SignalDx;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly DispatcherTimer _persistTimer;
    bool _layoutRestored;
    bool _applyingLayout;
    bool _hasPaneCache;
    bool _hasWindowCache;
    double _cacheLeft, _cacheTop, _cacheWidth, _cacheHeight;
    bool _cacheMaximized;
    double _cacheWaterfall, _cacheActivity, _cacheLog;
    double _cacheBand, _cacheRx, _cacheMacros;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ShowInTaskbar = true;

        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            FlushLayoutToSettings(saveToDisk: true);
        };

        // Restore before first paint so CenterScreen does not win over saved bounds.
        SourceInitialized += (_, _) => RestoreWindowPlacement();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => OnWindowGeometryChanged();
        LocationChanged += (_, _) => OnWindowGeometryChanged();
        MainLayoutGrid.SizeChanged += (_, _) => OnPaneGeometryChanged();
        ActivityLayoutGrid.SizeChanged += (_, _) => OnPaneGeometryChanged();

        Closing += (_, _) =>
        {
            _persistTimer.Stop();
            CacheWindowGeometry();
            CachePaneGeometry();
            FlushLayoutToSettings(saveToDisk: true);
        };
        Closed += (_, _) => _vm.Dispose();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        // Apply pane shares after the window has its final size.
        Dispatcher.BeginInvoke(() =>
        {
            RestorePaneLayout();
            CacheWindowGeometry();
            CachePaneGeometry();
        }, DispatcherPriority.ApplicationIdle);

        _vm.AttachWaterfall(Waterfall);
        Activate();
        _vm.StartAfterUiReady();
        // Auto-Monitor refreshes the In list — force the saved device back into the combo after bind.
        Dispatcher.BeginInvoke(() => _vm.RestoreAudioDeviceSelectionUi(), DispatcherPriority.ApplicationIdle);
    }

    void OnWindowGeometryChanged()
    {
        if (!IsLoaded || _applyingLayout) return;
        CacheWindowGeometry();
        SchedulePersist();
    }

    void OnPaneGeometryChanged()
    {
        if (!IsLoaded || _applyingLayout) return;
        CachePaneGeometry();
        SchedulePersist();
    }

    void SchedulePersist()
    {
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    void CacheWindowGeometry()
    {
        if (WindowState == WindowState.Minimized) return;

        var bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new Rect(Left, Top, Width, Height);

        if (bounds.Width < 400 || bounds.Height < 300) return;

        _cacheLeft = bounds.Left;
        _cacheTop = bounds.Top;
        _cacheWidth = bounds.Width;
        _cacheHeight = bounds.Height;
        _cacheMaximized = WindowState == WindowState.Maximized;
        _hasWindowCache = true;
    }

    void CachePaneGeometry()
    {
        double wh = MainRowWaterfall.ActualHeight;
        double ah = MainRowActivity.ActualHeight;
        double vSum = wh + ah;

        double bw = ColBand.ActualWidth;
        double rw = ColRx.ActualWidth;
        double mw = ColMacros.ActualWidth;
        double hSum = bw + rw + mw;

        // During close / pre-layout, Actual* can be 0 — keep the last good cache.
        if (vSum < 40 || hSum < 40) return;

        _cacheWaterfall = wh / vSum;
        _cacheActivity = ah / vSum;
        _cacheLog = 0;
        _cacheBand = bw / hSum;
        _cacheRx = rw / hSum;
        _cacheMacros = mw / hSum;
        _hasPaneCache = true;
    }

    void FlushLayoutToSettings(bool saveToDisk)
    {
        if (_hasWindowCache)
        {
            _vm.CaptureWindowPlacement(
                _cacheLeft, _cacheTop, _cacheWidth, _cacheHeight, _cacheMaximized);
        }

        if (_hasPaneCache)
        {
            _vm.CapturePaneLayout(
                _cacheWaterfall, _cacheActivity, _cacheLog,
                _cacheBand, _cacheRx, _cacheMacros);
        }

        if (saveToDisk && (_hasWindowCache || _hasPaneCache))
            _vm.SaveSettingsNow();
    }

    void RestoreWindowPlacement()
    {
        _applyingLayout = true;
        try
        {
            if (!_vm.TryGetWindowPlacement(out double left, out double top, out double width, out double height, out bool maximized))
            {
                CenterOnWorkArea();
                return;
            }

            Width = Math.Clamp(width, MinWidth, SystemParameters.VirtualScreenWidth);
            Height = Math.Clamp(height, MinHeight, SystemParameters.VirtualScreenHeight);

            if (!double.IsNaN(left) && !double.IsNaN(top) && IsOnScreen(left, top, Width, Height))
            {
                Left = left;
                Top = top;
            }
            else
            {
                CenterOnWorkArea();
            }

            if (maximized)
                WindowState = WindowState.Maximized;

            _cacheLeft = Left;
            _cacheTop = Top;
            _cacheWidth = Width;
            _cacheHeight = Height;
            _cacheMaximized = maximized;
            _hasWindowCache = true;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    void CenterOnWorkArea()
    {
        Left = SystemParameters.WorkArea.Left +
               Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
        Top = SystemParameters.WorkArea.Top +
              Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
    }

    void RestorePaneLayout()
    {
        if (_layoutRestored) return;
        if (!_vm.TryGetPaneLayout(
                out double waterfall, out double activity, out double log,
                out double band, out double rx, out double macros))
            return;

        _layoutRestored = true;
        _applyingLayout = true;
        try
        {
            // Log pane removed — fold any saved log share into activity.
            double act = activity + Math.Max(0, log);
            if (waterfall + act < 0.05)
            {
                waterfall = 0.4;
                act = 0.6;
            }

            MainRowWaterfall.Height = new GridLength(waterfall, GridUnitType.Star);
            MainRowActivity.Height = new GridLength(act, GridUnitType.Star);
            ColBand.Width = new GridLength(band, GridUnitType.Star);
            ColRx.Width = new GridLength(rx, GridUnitType.Star);
            ColMacros.Width = new GridLength(macros, GridUnitType.Star);

            _cacheWaterfall = waterfall;
            _cacheActivity = act;
            _cacheLog = 0;
            _cacheBand = band;
            _cacheRx = rx;
            _cacheMacros = macros;
            _hasPaneCache = true;
        }
        finally
        {
            // Allow layout to settle before accepting new cache samples.
            Dispatcher.BeginInvoke(() => _applyingLayout = false, DispatcherPriority.ApplicationIdle);
        }
    }

    static bool IsOnScreen(double left, double top, double width, double height)
    {
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

    void Dial_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        tb.SelectAll();
        e.Handled = true;
    }

    int _bandPresetIndexOnOpen = -1;

    void BandPreset_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox combo)
            _bandPresetIndexOnOpen = combo.SelectedIndex;
    }

    // Apply only on an operator pick; list rebuilds from CAT polls also change SelectedIndex.
    void BandPreset_OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.SelectedIndex < 0 || combo.SelectedIndex == _bandPresetIndexOnOpen) return;
        if (_vm.ApplyBandCommand.CanExecute(null))
            _vm.ApplyBandCommand.Execute(null);
    }

    void ManualDxCall_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_vm.ApplyManualDxCommand.CanExecute(null))
            _vm.ApplyManualDxCommand.Execute(null);
        e.Handled = true;
    }
}
