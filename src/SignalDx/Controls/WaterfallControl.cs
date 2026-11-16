using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SignalDx.Controls;

public sealed class FrequencyPickEventArgs : EventArgs
{
    public required int Hz { get; init; }
    public required bool SetRx { get; init; }
    public required bool SetTx { get; init; }
    public required bool SnapToDecode { get; init; }
}

public sealed class WaterfallControl : FrameworkElement
{
    readonly WriteableBitmap _bitmap;
    readonly int _width;
    readonly int _height;
    readonly byte[] _pixels;
    readonly float[] _column;
    float _floor = -90;
    float _ceil = -20;

    bool _dragging;
    bool _dragSetsTx;
    int? _hoverHz;
    int? _previewHz;

    public static readonly DependencyProperty TxHzProperty =
        DependencyProperty.Register(nameof(TxHz), typeof(int), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(1500, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RxHzProperty =
        DependencyProperty.Register(nameof(RxHz), typeof(int), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(1500, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LockTxToRxProperty =
        DependencyProperty.Register(nameof(LockTxToRx), typeof(bool), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChannelWidthHzProperty =
        DependencyProperty.Register(nameof(ChannelWidthHz), typeof(double), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaletteKindProperty =
        DependencyProperty.Register(nameof(PaletteKind), typeof(WaterfallPaletteKind), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(WaterfallPaletteKind.Classic, OnPaletteChanged));

    public WaterfallControl()
    {
        _width = 800;
        _height = 280;
        _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
        _pixels = new byte[_width * _height * 4];
        _column = new float[_width];
        FillBackground();

        Focusable = true;
        Cursor = Cursors.Cross;
        ToolTip = "Left-click: set Rx (+ Tx if locked)\nRight-click / Shift+click: set Tx only\nDrag to slide the channel";
    }

    static void OnPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaterfallControl wf)
            wf.FillBackground();
    }

    public event EventHandler<FrequencyPickEventArgs>? FrequencyPicked;

    public int TxHz
    {
        get => (int)GetValue(TxHzProperty);
        set => SetValue(TxHzProperty, value);
    }

    public int RxHz
    {
        get => (int)GetValue(RxHzProperty);
        set => SetValue(RxHzProperty, value);
    }

    public bool LockTxToRx
    {
        get => (bool)GetValue(LockTxToRxProperty);
        set => SetValue(LockTxToRxProperty, value);
    }

    public double ChannelWidthHz
    {
        get => (double)GetValue(ChannelWidthHzProperty);
        set => SetValue(ChannelWidthHzProperty, value);
    }

    public WaterfallPaletteKind PaletteKind
    {
        get => (WaterfallPaletteKind)GetValue(PaletteKindProperty);
        set => SetValue(PaletteKindProperty, value);
    }

    public int SampleRate { get; set; } = 48000;
    public int FftSize { get; set; } = 2048;
    public double MinHz { get; set; } = 200;
    public double MaxHz { get; set; } = 3000;
    public double SnapHz { get; set; } = 6.25;

    public void PushSpectrum(IReadOnlyList<float> magnitudesDb)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PushSpectrum(magnitudesDb), DispatcherPriority.Render);
            return;
        }

        MapColumn(magnitudesDb);
        ScrollUp();
        WriteColumn(_height - 1, _column);
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _dragging = true;
        _dragSetsTx = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        ApplyPointer(e.GetPosition(this), setRx: !_dragSetsTx, setTx: _dragSetsTx || LockTxToRx, snap: true);
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _dragging = true;
        _dragSetsTx = true;
        ApplyPointer(e.GetPosition(this), setRx: false, setTx: true, snap: true);
        e.Handled = true;
        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        _hoverHz = XToHz(pos.X);

        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyPointer(pos, setRx: !_dragSetsTx, setTx: _dragSetsTx || LockTxToRx, snap: false);
        }
        else if (_dragging && e.RightButton == MouseButtonState.Pressed)
        {
            ApplyPointer(pos, setRx: false, setTx: true, snap: false);
        }
        else
        {
            _previewHz = _hoverHz;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            ApplyPointer(e.GetPosition(this), setRx: !_dragSetsTx, setTx: _dragSetsTx || LockTxToRx, snap: true);
            EndDrag();
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            ApplyPointer(e.GetPosition(this), setRx: false, setTx: true, snap: true);
            EndDrag();
            e.Handled = true;
        }
        base.OnMouseRightButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!_dragging)
        {
            _hoverHz = null;
            _previewHz = null;
            InvalidateVisual();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        EndDrag();
        base.OnLostMouseCapture(e);
    }

    void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    void ApplyPointer(Point pos, bool setRx, bool setTx, bool snap)
    {
        int hz = XToHz(pos.X);
        hz = Snap(hz);
        _previewHz = hz;
        FrequencyPicked?.Invoke(this, new FrequencyPickEventArgs
        {
            Hz = hz,
            SetRx = setRx,
            SetTx = setTx,
            SnapToDecode = snap
        });
        InvalidateVisual();
    }

    int XToHz(double x)
    {
        if (ActualWidth <= 0) return RxHz;
        double t = Math.Clamp(x / ActualWidth, 0, 1);
        return (int)Math.Round(MinHz + t * (MaxHz - MinHz));
    }

    double HzToX(double hz)
    {
        double span = MaxHz - MinHz;
        if (span <= 0 || ActualWidth <= 0) return 0;
        return (hz - MinHz) / span * ActualWidth;
    }

    int Snap(int hz)
    {
        if (SnapHz <= 0) return Math.Clamp(hz, (int)MinHz, (int)MaxHz);
        double snapped = Math.Round(hz / SnapHz) * SnapHz;
        return (int)Math.Clamp(Math.Round(snapped), MinHz, MaxHz);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        dc.DrawImage(_bitmap, rect);

        double span = MaxHz - MinHz;
        if (span <= 0 || ActualWidth <= 0) return;

        DrawChannel(dc, RxHz, Color.FromArgb(55, 0x3F, 0xC0, 0xE0), Color.FromRgb(0x3F, 0xC0, 0xE0), "RX");
        if (!LockTxToRx || Math.Abs(TxHz - RxHz) > 1)
            DrawChannel(dc, TxHz, Color.FromArgb(45, 0xE0, 0x6C, 0x3F), Color.FromRgb(0xE0, 0x6C, 0x3F), "TX");
        else
            DrawCenterLine(dc, TxHz, new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x3F)), 1.2));

        if (_previewHz is int preview && (!_dragging || preview != RxHz))
        {
            double x = HzToX(preview);
            var hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0xF0, 0xC1, 0x4A)), 1)
            {
                DashStyle = DashStyles.Dot
            };
            dc.DrawLine(hoverPen, new Point(x, 0), new Point(x, ActualHeight));

            var label = FormatHz(preview);
            var ft = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                11,
                new SolidColorBrush(Color.FromRgb(0xF0, 0xC1, 0x4A)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double lx = Math.Clamp(x + 6, 0, Math.Max(0, ActualWidth - ft.Width - 4));
            dc.DrawText(ft, new Point(lx, 4));
        }

        // Scale ticks
        var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0x8F, 0xA3, 0xBF)), 1);
        for (int hz = 500; hz <= 2500; hz += 500)
        {
            double x = HzToX(hz);
            dc.DrawLine(tickPen, new Point(x, ActualHeight - 8), new Point(x, ActualHeight));
        }
    }

    void DrawChannel(DrawingContext dc, int centerHz, Color fill, Color edge, string tag)
    {
        double half = ChannelWidthHz / 2.0;
        double x0 = HzToX(centerHz - half);
        double x1 = HzToX(centerHz + half);
        if (x1 < x0) (x0, x1) = (x1, x0);

        var band = new Rect(x0, 0, Math.Max(1, x1 - x0), ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(fill), null, band);
        DrawCenterLine(dc, centerHz, new Pen(new SolidColorBrush(edge), 1.6));

        var ft = new FormattedText(
            $"{tag} {FormatHz(centerHz)}",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"),
            11,
            new SolidColorBrush(edge),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double lx = Math.Clamp(HzToX(centerHz) + 5, 0, Math.Max(0, ActualWidth - ft.Width - 4));
        double ly = tag == "TX" ? ActualHeight - ft.Height - 6 : 18;
        dc.DrawText(ft, new Point(lx, ly));
    }

    void DrawCenterLine(DrawingContext dc, int hz, Pen pen)
    {
        double x = HzToX(hz);
        dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
    }

    static string FormatHz(int hz) => $"{hz} Hz";

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsInfinity(availableSize.Width) ? _width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _height : availableSize.Height);

    void MapColumn(IReadOnlyList<float> magnitudesDb)
    {
        int bins = magnitudesDb.Count;
        for (int x = 0; x < _width; x++)
        {
            double hz = MinHz + (MaxHz - MinHz) * x / (_width - 1);
            double bin = hz * FftSize / SampleRate;
            int b0 = Math.Clamp((int)bin, 0, bins - 1);
            int b1 = Math.Clamp(b0 + 1, 0, bins - 1);
            float t = (float)(bin - b0);
            float db = magnitudesDb[b0] * (1 - t) + magnitudesDb[b1] * t;
            _column[x] = db;
            _floor = Math.Min(_floor * 0.999f + db * 0.001f, db);
            _ceil = Math.Max(_ceil * 0.999f + db * 0.001f, db);
        }
    }

    void ScrollUp()
    {
        int stride = _width * 4;
        Buffer.BlockCopy(_pixels, stride, _pixels, 0, (_height - 1) * stride);
    }

    void WriteColumn(int y, float[] column)
    {
        float range = Math.Max(20f, _ceil - _floor);
        for (int x = 0; x < _width; x++)
        {
            float n = Math.Clamp((column[x] - _floor) / range, 0f, 1f);
            var c = WaterfallPalette.Map(PaletteKind, n);
            int i = (y * _width + x) * 4;
            _pixels[i] = c.B;
            _pixels[i + 1] = c.G;
            _pixels[i + 2] = c.R;
            _pixels[i + 3] = 255;
        }
    }

    void FillBackground()
    {
        var bg = WaterfallPalette.Background(PaletteKind);
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int i = (y * _width + x) * 4;
                _pixels[i] = bg.B;
                _pixels[i + 1] = bg.G;
                _pixels[i + 2] = bg.R;
                _pixels[i + 3] = 255;
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        InvalidateVisual();
    }
}
