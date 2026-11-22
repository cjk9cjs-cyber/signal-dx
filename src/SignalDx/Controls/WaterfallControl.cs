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
    const int MinBitmapWidth = 800;
    const int MaxBitmapWidth = 2800;
    const int MinBitmapHeight = 320;
    const int MaxBitmapHeight = 640;
    /// <summary>dB above noise before a pixel leaves the solid background (FT8/FT4).</summary>
    const float SignalGateDb = 6.5f;
    /// <summary>Lower gate so weak WSPR traces (−28 dB class) still paint.</summary>
    const float WsprSignalGateDb = 3.0f;
    /// <summary>Horizontal time-mark period (15 FT8, 7 FT4, 120 WSPR-2).</summary>
    int _slotSeconds = 15;
    bool _wsprDisplay;

    WriteableBitmap _bitmap;
    int _width;
    int _height;
    byte[] _pixels;
    float[] _column;
    float[] _display;
    float _noiseFloor = -90;
    float _signalCeil = -25;
    long? _lastSlotIndex;
    readonly List<(int AgeRows, DateTime SlotLocal)> _timeMarks = new();

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
        _width = 1920;
        _height = 480;
        _bitmap = CreateBitmap(_width, _height);
        _pixels = new byte[_width * _height * 4];
        _column = new float[_width];
        _display = new float[_width];
        FillBackground();

        Focusable = true;
        Cursor = Cursors.Cross;
        // Nearest-neighbor keeps FT8 traces as crisp pixel columns on a solid field.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        ToolTip = "Left-click: set Rx (+ Tx if locked)\nRight-click / Shift+click: set Tx only\nDrag to slide the channel";
        SizeChanged += (_, _) => EnsureBitmapMatchesLayout();
        Loaded += (_, _) => EnsureBitmapMatchesLayout();
    }

    static WriteableBitmap CreateBitmap(int w, int h) =>
        new(w, h, 96, 96, PixelFormats.Bgra32, null);

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
    public int FftSize { get; set; } = 4096;
    public double MinHz { get; set; } = 200;
    public double MaxHz { get; set; } = 3000;
    public double SnapHz { get; set; } = 6.25;

    public int SlotSeconds
    {
        get => _slotSeconds;
        set => _slotSeconds = Math.Max(1, value);
    }

    /// <summary>WSPR-2 display: interpolated narrow band, soft traces, 2-min marks.</summary>
    public bool WsprDisplayMode
    {
        get => _wsprDisplay;
        set
        {
            if (_wsprDisplay == value) return;
            _wsprDisplay = value;
            ToolTip = value
                ? "WSPR-2 passband (1400–1600 Hz)\nLeft-click: set Tx/Rx\nRight-click / Shift: Tx only"
                : "Left-click: set Rx (+ Tx if locked)\nRight-click / Shift+click: set Tx only\nDrag to slide the channel";
        }
    }

    float ActiveGateDb => _wsprDisplay ? WsprSignalGateDb : SignalGateDb;

    /// <summary>Clear history and AGC when switching modes / bands.</summary>
    public void ResetDisplay()
    {
        _noiseFloor = -90;
        _signalCeil = -25;
        FillBackground(resetTimeMarks: true);
    }

    public void PushSpectrum(IReadOnlyList<float> magnitudesDb)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PushSpectrum(magnitudesDb), DispatcherPriority.Render);
            return;
        }

        EnsureBitmapMatchesLayout();
        MapColumn(magnitudesDb);
        BuildDisplayColumn();

        var now = DateTime.Now;
        long slot = SlotIndex(now);
        bool slotBoundary = _lastSlotIndex is long prev && prev != slot;
        _lastSlotIndex = slot;
        AgeTimeMarks();

        ScrollUp();
        WriteColumn(_height - 1, _display);
        if (slotBoundary)
        {
            var aligned = AlignToSlot(now);
            // WSPR slots are already 2 minutes — every mark is a major boundary.
            bool major = _wsprDisplay || IsMinuteBoundary(aligned);
            PaintTimeLine(_height - 1, major);
            _timeMarks.Add((0, aligned));
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        InvalidateVisual();
    }

    long SlotIndex(DateTime local) =>
        local.Ticks / (TimeSpan.TicksPerSecond * (long)_slotSeconds);

    DateTime AlignToSlot(DateTime local)
    {
        long sec = local.Ticks / TimeSpan.TicksPerSecond;
        long alignedSec = sec - (sec % _slotSeconds);
        return new DateTime(alignedSec * TimeSpan.TicksPerSecond, local.Kind);
    }

    static bool IsMinuteBoundary(DateTime slotLocal) => slotLocal.Second == 0;

    void AgeTimeMarks()
    {
        for (int i = _timeMarks.Count - 1; i >= 0; i--)
        {
            var (age, stamp) = _timeMarks[i];
            age++;
            if (age >= _height)
                _timeMarks.RemoveAt(i);
            else
                _timeMarks[i] = (age, stamp);
        }
    }

    void ClearTimeMarks()
    {
        _timeMarks.Clear();
        _lastSlotIndex = null;
    }

    void PaintTimeLine(int y, bool minute)
    {
        // Dim gray for :15/:30/:45; brighter for :00 so minute ticks stand out.
        byte r = minute ? (byte)0xC8 : (byte)0x7A;
        byte g = minute ? (byte)0xD4 : (byte)0x8C;
        byte b = minute ? (byte)0xE8 : (byte)0xA0;
        int row = y * _width * 4;
        for (int x = 0; x < _width; x++)
        {
            int i = row + x * 4;
            // Lighten toward the marker so strong traces still peek through a bit.
            _pixels[i] = (byte)Math.Min(255, (_pixels[i] * 2 + b * 3) / 5);
            _pixels[i + 1] = (byte)Math.Min(255, (_pixels[i + 1] * 2 + g * 3) / 5);
            _pixels[i + 2] = (byte)Math.Min(255, (_pixels[i + 2] * 2 + r * 3) / 5);
            _pixels[i + 3] = 255;
        }
    }

    void EnsureBitmapMatchesLayout()
    {
        if (ActualWidth < 32 || ActualHeight < 32) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Match on-screen pixels 1:1 so the solid background doesn't get stretched into blocks.
        int wantW = (int)Math.Clamp(Math.Round(ActualWidth * dpi), MinBitmapWidth, MaxBitmapWidth);
        int wantH = (int)Math.Clamp(Math.Round(Math.Max(WaterfallHeight, 180) * dpi), MinBitmapHeight, MaxBitmapHeight);
        if ((wantW & 1) != 0) wantW++;
        if ((wantH & 1) != 0) wantH++;

        if (wantW == _width && wantH == _height) return;

        var oldPixels = _pixels;
        int oldW = _width;
        int oldH = _height;

        _width = wantW;
        _height = wantH;
        _bitmap = CreateBitmap(_width, _height);
        _pixels = new byte[_width * _height * 4];
        _column = new float[_width];
        _display = new float[_width];
        FillBackground(resetTimeMarks: false);

        // Preserve as much history as possible when resizing.
        int copyH = Math.Min(oldH, _height);
        int copyW = Math.Min(oldW, _width);
        for (int y = 0; y < copyH; y++)
        {
            int srcY = oldH - copyH + y;
            int dstY = _height - copyH + y;
            Buffer.BlockCopy(
                oldPixels, (srcY * oldW) * 4,
                _pixels, (dstY * _width) * 4,
                copyW * 4);
        }
        // Drop marks that scrolled off the shorter bitmap.
        for (int i = _timeMarks.Count - 1; i >= 0; i--)
        {
            if (_timeMarks[i].AgeRows >= _height)
                _timeMarks.RemoveAt(i);
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
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
        // Draw onto a solid backdrop so empty/noise areas never show through as checker/pixel dirt.
        var bg = WaterfallPalette.Background(PaletteKind);
        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, ActualWidth, wfH));
        dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, wfH));
        DrawTimeMarkLabels(dc, wfH);

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

    void DrawTimeMarkLabels(DrawingContext dc, double wfH)
    {
        if (_timeMarks.Count == 0 || _height <= 1 || ActualWidth <= 0) return;

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var brush = new SolidColorBrush(Color.FromArgb(210, 0xD0, 0xDC, 0xEC));
        brush.Freeze();
        var minuteBrush = new SolidColorBrush(Color.FromArgb(230, 0xF0, 0xF4, 0xFA));
        minuteBrush.Freeze();
        var shadow = new SolidColorBrush(Color.FromArgb(160, 0x05, 0x08, 0x10));
        shadow.Freeze();

        foreach (var (ageRows, stamp) in _timeMarks)
        {
            int bitmapY = _height - 1 - ageRows;
            if (bitmapY < 0 || bitmapY >= _height) continue;

            double y = (bitmapY + 0.5) / _height * wfH;
            bool minute = _wsprDisplay || IsMinuteBoundary(stamp);
            // WSPR 2-minute marks: HH:mm is enough; FT8 keeps seconds.
            string text = _wsprDisplay ? stamp.ToString("HH:mm") : stamp.ToString("HH:mm:ss");
            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                minute ? 11 : 10,
                minute ? minuteBrush : brush,
                dip);

            double ty = Math.Clamp(y - ft.Height - 1, 1, Math.Max(1, wfH - ft.Height - 1));
            // Soft backdrop so labels stay readable over bright traces.
            dc.DrawRectangle(shadow, null, new Rect(3, ty - 1, ft.Width + 6, ft.Height + 2));
            dc.DrawText(ft, new Point(6, ty));
        }
    }

    void DrawFrequencyScaleBar(DrawingContext dc)
    {
        double y0 = WaterfallHeight;
        double h = Math.Max(0, ActualHeight - y0);
        if (h < 4 || ActualWidth <= 0) return;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x20)), null,
            new Rect(0, y0, ActualWidth, h));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x55)), 1),
            new Point(0, y0), new Point(ActualWidth, y0));

        var minorPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 0x6F, 0x86, 0xA8)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE8)), 1.2);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // WSPR ~200 Hz span → 20 Hz minors / 100 Hz majors; FT8 keeps 100 / 500.
        double spanHz = Math.Max(1, MaxHz - MinHz);
        int minorStep = _wsprDisplay || spanHz <= 400 ? 20 : 100;
        int majorStep = _wsprDisplay || spanHz <= 400 ? 100 : 500;
        int min = (int)Math.Ceiling(MinHz / minorStep) * minorStep;
        int max = (int)Math.Floor(MaxHz / minorStep) * minorStep;
        for (int hz = min; hz <= max; hz += minorStep)
        {
            double x = HzToX(hz);
            bool major = hz % majorStep == 0;
            double tickTop = major ? y0 + 2 : y0 + 10;
            dc.DrawLine(major ? majorPen : minorPen, new Point(x, tickTop), new Point(x, y0 + h - 2));

            if (!major) continue;

            string label = _wsprDisplay
                ? $"{hz}"
                : hz >= 1000 ? $"{hz / 1000.0:0.###}k" : $"{hz}";
            var ft = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                11,
                new SolidColorBrush(Color.FromRgb(0xE7, 0xEE, 0xF8)),
                dip);
            double lx = Math.Clamp(x - ft.Width / 2, 2, Math.Max(2, ActualWidth - ft.Width - 2));
            dc.DrawText(ft, new Point(lx, y0 + 3));
        }

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
            double.IsInfinity(availableSize.Height) ? _height + ScaleBarHeight : availableSize.Height);

    void MapColumn(IReadOnlyList<float> magnitudesDb)
    {
        int bins = magnitudesDb.Count;
        if (bins < 2) return;

        float peak = -120;
        // Sample every Nth pixel for a cheap noise estimate (ignore the strongest bins).
        float noiseAcc = 0;
        int noiseN = 0;
        for (int x = 0; x < _width; x++)
        {
            double hz = MinHz + (MaxHz - MinHz) * x / Math.Max(1, _width - 1);
            double bin = hz * FftSize / (double)SampleRate;
            float db = _wsprDisplay
                ? SampleBinInterpolated(magnitudesDb, bin)
                : magnitudesDb[Math.Clamp((int)Math.Round(bin), 0, bins - 1)];
            _column[x] = db;
            if (db > peak) peak = db;
            if ((x & 3) == 0)
            {
                noiseAcc += db;
                noiseN++;
            }
        }

        // Narrow WSPR band: light 3-tap smooth so FFT bins don't paint as fat blocks.
        if (_wsprDisplay && _width >= 3)
        {
            float prev = _column[0];
            for (int x = 1; x < _width - 1; x++)
            {
                float cur = _column[x];
                float next = _column[x + 1];
                float sm = (prev + cur * 2f + next) * 0.25f;
                prev = cur;
                _column[x] = sm;
                if (sm > peak) peak = sm;
            }
        }

        // Robust-ish floor: average of subsampled bins, then bias a bit below so noise stays blank.
        float gate = ActiveGateDb;
        float avg = noiseN > 0 ? noiseAcc / noiseN : peak - 25f;
        float targetFloor = avg - (_wsprDisplay ? 2.5f : 1.5f);
        _noiseFloor = _noiseFloor * 0.92f + targetFloor * 0.08f;
        _signalCeil = Math.Max(peak, _signalCeil * 0.97f + peak * 0.03f);
        if (_signalCeil < _noiseFloor + gate + 12f)
            _signalCeil = _noiseFloor + gate + 12f;
        float maxDyn = _wsprDisplay ? 35f : 45f;
        if (_signalCeil - _noiseFloor > maxDyn)
            _noiseFloor = _signalCeil - maxDyn;
    }

    static float SampleBinInterpolated(IReadOnlyList<float> magnitudesDb, double bin)
    {
        int bins = magnitudesDb.Count;
        if (bin <= 0) return magnitudesDb[0];
        if (bin >= bins - 1) return magnitudesDb[bins - 1];
        int i0 = (int)bin;
        float t = (float)(bin - i0);
        return magnitudesDb[i0] * (1f - t) + magnitudesDb[i0 + 1] * t;
    }

    /// <summary>
    /// Gate noise to empty. FT8 keeps sharp pixel columns; WSPR paints soft continuous intensity.
    /// </summary>
    void BuildDisplayColumn()
    {
        float gate = _noiseFloor + ActiveGateDb;
        if (_wsprDisplay)
        {
            for (int x = 0; x < _width; x++)
            {
                float v = _column[x];
                _display[x] = v >= gate ? v : float.NegativeInfinity;
            }
            return;
        }

        for (int x = 0; x < _width; x++)
        {
            float v = _column[x];
            if (v < gate)
            {
                _display[x] = float.NegativeInfinity; // → solid background
                continue;
            }

            float left = x > 0 ? _column[x - 1] : v;
            float right = x + 1 < _width ? _column[x + 1] : v;
            // Prefer ridge pixels so FT8 tones read as thin vertical lines.
            bool localPeak = v >= left && v >= right;
            bool nearPeak = v >= left - 1.2f && v >= right - 1.2f;
            if (localPeak)
                _display[x] = v;
            else if (nearPeak && v >= gate + 2f)
                _display[x] = v - 1.5f; // dim side of a trace
            else if (v >= gate + 5f)
                _display[x] = v - 3f; // strong energy that isn't a sharp peak
            else
                _display[x] = float.NegativeInfinity;
        }
    }

    void ScrollUp()
    {
        int stride = _width * 4;
        Buffer.BlockCopy(_pixels, stride, _pixels, 0, (_height - 1) * stride);
    }

    void WriteColumn(int y, float[] column)
    {
        float gate = ActiveGateDb;
        float range = Math.Max(14f, _signalCeil - (_noiseFloor + gate));
        var bg = WaterfallPalette.Background(PaletteKind);
        double gamma = _wsprDisplay ? 0.85 : 0.72;
        for (int x = 0; x < _width; x++)
        {
            Color c;
            float v = column[x];
            if (float.IsNegativeInfinity(v) || v < _noiseFloor + gate)
            {
                c = bg;
            }
            else
            {
                float linear = Math.Clamp((v - (_noiseFloor + gate)) / range, 0f, 1f);
                float n = (float)Math.Pow(linear, gamma);
                // WSPR: gentler floor so soft traces don't jump to neon.
                float mapped = _wsprDisplay
                    ? Math.Clamp(0.10f + 0.90f * n, 0f, 1f)
                    : Math.Clamp(0.18f + 0.82f * n, 0f, 1f);
                c = WaterfallPalette.Map(PaletteKind, mapped);
            }

            int i = (y * _width + x) * 4;
            _pixels[i] = c.B;
            _pixels[i + 1] = c.G;
            _pixels[i + 2] = c.R;
            _pixels[i + 3] = 255;
        }
    }

    void FillBackground(bool resetTimeMarks = true)
    {
        var bg = WaterfallPalette.Background(PaletteKind);
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = bg.B;
            _pixels[i + 1] = bg.G;
            _pixels[i + 2] = bg.R;
            _pixels[i + 3] = 255;
        }
        if (resetTimeMarks)
            ClearTimeMarks();
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        InvalidateVisual();
    }
}
