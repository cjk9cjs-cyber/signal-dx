using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SignalDx.Controls;

public sealed class WaterfallControl : FrameworkElement
{
    readonly WriteableBitmap _bitmap;
    readonly int _width;
    readonly int _height;
    readonly byte[] _pixels;
    readonly float[] _column;
    float _floor = -90;
    float _ceil = -20;

    public static readonly DependencyProperty TxHzProperty =
        DependencyProperty.Register(nameof(TxHz), typeof(int), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(1500, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RxHzProperty =
        DependencyProperty.Register(nameof(RxHz), typeof(int), typeof(WaterfallControl),
            new FrameworkPropertyMetadata(1500, FrameworkPropertyMetadataOptions.AffectsRender));

    public WaterfallControl()
    {
        _width = 800;
        _height = 280;
        _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
        _pixels = new byte[_width * _height * 4];
        _column = new float[_width];
        FillBackground();
    }

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

    public int SampleRate { get; set; } = 48000;
    public int FftSize { get; set; } = 2048;
    public double MinHz { get; set; } = 200;
    public double MaxHz { get; set; } = 3000;

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

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        dc.DrawImage(_bitmap, rect);

        double span = MaxHz - MinHz;
        if (span <= 0 || ActualWidth <= 0) return;

        double txX = (TxHz - MinHz) / span * ActualWidth;
        double rxX = (RxHz - MinHz) / span * ActualWidth;

        var txPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x3F)), 1.5);
        var rxPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3F, 0xC0, 0xE0)), 1.5);
        dc.DrawLine(txPen, new Point(txX, 0), new Point(txX, ActualHeight));
        dc.DrawLine(rxPen, new Point(rxX, 0), new Point(rxX, ActualHeight));
    }

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
            var c = Palette(n);
            int i = (y * _width + x) * 4;
            _pixels[i] = c.B;
            _pixels[i + 1] = c.G;
            _pixels[i + 2] = c.R;
            _pixels[i + 3] = 255;
        }
    }

    void FillBackground()
    {
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int i = (y * _width + x) * 4;
                _pixels[i] = 12;
                _pixels[i + 1] = 14;
                _pixels[i + 2] = 18;
                _pixels[i + 3] = 255;
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
    }

    static Color Palette(float t)
    {
        // Deep navy → teal → amber → white (radio waterfall feel)
        if (t < 0.25f)
        {
            float u = t / 0.25f;
            return Lerp(Color.FromRgb(8, 12, 28), Color.FromRgb(20, 70, 110), u);
        }
        if (t < 0.55f)
        {
            float u = (t - 0.25f) / 0.30f;
            return Lerp(Color.FromRgb(20, 70, 110), Color.FromRgb(40, 160, 120), u);
        }
        if (t < 0.8f)
        {
            float u = (t - 0.55f) / 0.25f;
            return Lerp(Color.FromRgb(40, 160, 120), Color.FromRgb(220, 170, 40), u);
        }
        float v = (t - 0.8f) / 0.2f;
        return Lerp(Color.FromRgb(220, 170, 40), Color.FromRgb(255, 250, 230), v);
    }

    static Color Lerp(Color a, Color b, float t) =>
        Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}
