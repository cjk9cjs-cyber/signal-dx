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
    const double ScaleBarHeight = 26;

    readonly WriteableBitmap _bitmap;
    readonly int _width;
    readonly int _height;
    readonly byte[] _pixels;
    readonly float[] _column;
    float _noiseFloor = -85;
    float _signalCeil = -25;

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

    double WaterfallHeight => Math.Max(1, ActualHeight - ScaleBarHeight);

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
        double wfH = WaterfallHeight;
        dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, wfH));

        double span = MaxHz - MinHz;
        if (span <= 0 || ActualWidth <= 0) return;

        DrawChannel(dc, RxHz, Color.FromArgb(55, 0x3F, 0xC0, 0xE0), Color.FromRgb(0x3F, 0xC0, 0xE0), "RX", wfH);
        if (!LockTxToRx || Math.Abs(TxHz - RxHz) > 1)
            DrawChannel(dc, TxHz, Color.FromArgb(45, 0xE0, 0x6C, 0x3F), Color.FromRgb(0xE0, 0x6C, 0x3F), "TX", wfH);
        else
            DrawCenterLine(dc, TxHz, new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x3F)), 1.2), wfH);

        if (_previewHz is int preview && (!_dragging || preview != RxHz))
        {
            double x = HzToX(preview);
            var hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0xF0, 0xC1, 0x4A)), 1)
            {
                DashStyle = DashStyles.Dot
            };
            dc.DrawLine(hoverPen, new Point(x, 0), new Point(x, wfH));

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

        DrawFrequencyScaleBar(dc);
    }

    void DrawFrequencyScaleBar(DrawingContext dc)
    {
        double y0 = WaterfallHeight;
        double h = Math.Max(0, ActualHeight - y0);
        if (h < 4 || ActualWidth <= 0) return;

        // Solid bar so you can read where you are on the audio passband.
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x20)), null,
            new Rect(0, y0, ActualWidth, h));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x55)), 1),
            new Point(0, y0), new Point(ActualWidth, y0));

        var minorPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 0x6F, 0x86, 0xA8)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE8)), 1.2);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int min = (int)Math.Ceiling(MinHz / 100.0) * 100;
        int max = (int)Math.Floor(MaxHz / 100.0) * 100;
        for (int hz = min; hz <= max; hz += 100)
        {
            double x = HzToX(hz);
            bool major = hz % 500 == 0;
            double tickTop = major ? y0 + 2 : y0 + 10;
            dc.DrawLine(major ? majorPen : minorPen, new Point(x, tickTop), new Point(x, y0 + h - 2));

            if (!major) continue;

            var ft = new FormattedText(
                hz >= 1000 ? $"{hz / 1000.0:0.###}k" : $"{hz}",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                11,
                new SolidColorBrush(Color.FromRgb(0xE7, 0xEE, 0xF8)),
                dip);
            double lx = Math.Clamp(x - ft.Width / 2, 2, Math.Max(2, ActualWidth - ft.Width - 2));
            dc.DrawText(ft, new Point(lx, y0 + 3));
        }

        // Current RX marker triangle on the scale bar.
        double rxX = HzToX(RxHz);
        var rxBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xC0, 0xE0));
        var rxGeo = new StreamGeometry();
        using (var ctx = rxGeo.Open())
        {
            ctx.BeginFigure(new Point(rxX, y0 + 1), true, true);
            ctx.LineTo(new Point(rxX - 5, y0 + 9), true, false);
            ctx.LineTo(new Point(rxX + 5, y0 + 9), true, false);
        }
        rxGeo.Freeze();
        dc.DrawGeometry(rxBrush, null, rxGeo);

        if (!LockTxToRx || Math.Abs(TxHz - RxHz) > 1)
        {
            double txX = HzToX(TxHz);
            var txBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x3F));
            var txGeo = new StreamGeometry();
            using (var ctx = txGeo.Open())
            {
                ctx.BeginFigure(new Point(txX, y0 + h - 1), true, true);
                ctx.LineTo(new Point(txX - 5, y0 + h - 9), true, false);
                ctx.LineTo(new Point(txX + 5, y0 + h - 9), true, false);
            }
            txGeo.Freeze();
            dc.DrawGeometry(txBrush, null, txGeo);
        }
    }

    void DrawChannel(DrawingContext dc, int centerHz, Color fill, Color edge, string tag, double wfH)
    {
        double half = ChannelWidthHz / 2.0;
        double x0 = HzToX(centerHz - half);
        double x1 = HzToX(centerHz + half);
        if (x1 < x0) (x0, x1) = (x1, x0);

        var band = new Rect(x0, 0, Math.Max(1, x1 - x0), wfH);
        dc.DrawRectangle(new SolidColorBrush(fill), null, band);
        DrawCenterLine(dc, centerHz, new Pen(new SolidColorBrush(edge), 1.6), wfH);

        var ft = new FormattedText(
            $"{tag} {FormatHz(centerHz)}",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"),
            11,
            new SolidColorBrush(edge),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double lx = Math.Clamp(HzToX(centerHz) + 5, 0, Math.Max(0, ActualWidth - ft.Width - 4));
        double ly = tag == "TX" ? Math.Max(18, wfH - ft.Height - 6) : 18;
        dc.DrawText(ft, new Point(lx, ly));
    }

    void DrawCenterLine(DrawingContext dc, int hz, Pen pen, double wfH)
    {
        double x = HzToX(hz);
        dc.DrawLine(pen, new Point(x, 0), new Point(x, wfH));
    }

    static string FormatHz(int hz) => $"{hz} Hz";

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsInfinity(availableSize.Width) ? _width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _height : availableSize.Height);

    void MapColumn(IReadOnlyList<float> magnitudesDb)
    {
        int bins = magnitudesDb.Count;
        float sum = 0;
        float peak = -120;
        for (int x = 0; x < _width; x++)
        {
            double hz = MinHz + (MaxHz - MinHz) * x / (_width - 1);
            double bin = hz * FftSize / SampleRate;
            int b0 = Math.Clamp((int)bin, 0, bins - 1);
            int b1 = Math.Clamp(b0 + 1, 0, bins - 1);
            float t = (float)(bin - b0);
            // Light 3-bin average to thicken weak FT8 traces.
            int bm = Math.Clamp(b0 - 1, 0, bins - 1);
            int bp = Math.Clamp(b1 + 1, 0, bins - 1);
            float db =
                0.15f * magnitudesDb[bm] +
                0.35f * magnitudesDb[b0] +
                0.35f * (magnitudesDb[b0] * (1 - t) + magnitudesDb[b1] * t) +
                0.15f * magnitudesDb[bp];
            _column[x] = db;
            sum += db;
            if (db > peak) peak = db;
        }

        float avg = sum / _width;
        // Noise hugs the average; ceiling tracks peaks with slow decay so bright traces pop.
        _noiseFloor = _noiseFloor * 0.97f + avg * 0.03f;
        _signalCeil = Math.Max(peak, _signalCeil * 0.985f + peak * 0.015f);
        // Keep a useful window so quiet bands still show contrast.
        if (_signalCeil - _noiseFloor < 18f)
            _signalCeil = _noiseFloor + 18f;
        if (_signalCeil - _noiseFloor > 55f)
            _noiseFloor = _signalCeil - 55f;
    }

    void ScrollUp()
    {
        int stride = _width * 4;
        Buffer.BlockCopy(_pixels, stride, _pixels, 0, (_height - 1) * stride);
    }

    void WriteColumn(int y, float[] column)
    {
        float range = Math.Max(18f, _signalCeil - _noiseFloor);
        for (int x = 0; x < _width; x++)
        {
            float linear = Math.Clamp((column[x] - _noiseFloor) / range, 0f, 1f);
            // Gamma < 1 lifts weak signals out of the noise floor.
            float n = (float)Math.Pow(linear, 0.55);
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
