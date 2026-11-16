using System.Windows.Media;

namespace SignalDx.Controls;

public enum WaterfallPaletteKind
{
    Classic,
    Amber,
    Ice,
    Green,
    Magenta,
    Gray,
    Heat
}

public static class WaterfallPalette
{
    public static IReadOnlyList<string> Names { get; } =
        Enum.GetNames<WaterfallPaletteKind>();

    public static WaterfallPaletteKind Parse(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out WaterfallPaletteKind kind)
            ? kind
            : WaterfallPaletteKind.Classic;

    public static Color Map(WaterfallPaletteKind kind, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return kind switch
        {
            WaterfallPaletteKind.Amber => Multi(
                t,
                Rgb(8, 6, 2), Rgb(60, 30, 4), Rgb(160, 90, 10), Rgb(230, 170, 40), Rgb(255, 245, 200)),
            WaterfallPaletteKind.Ice => Multi(
                t,
                Rgb(2, 6, 18), Rgb(10, 40, 90), Rgb(20, 110, 180), Rgb(120, 210, 255), Rgb(235, 250, 255)),
            WaterfallPaletteKind.Green => Multi(
                t,
                Rgb(2, 10, 4), Rgb(8, 40, 16), Rgb(20, 110, 40), Rgb(90, 210, 80), Rgb(230, 255, 210)),
            WaterfallPaletteKind.Magenta => Multi(
                t,
                Rgb(10, 2, 14), Rgb(50, 10, 70), Rgb(130, 30, 150), Rgb(230, 90, 180), Rgb(255, 230, 250)),
            WaterfallPaletteKind.Gray => Multi(
                t,
                Rgb(6, 6, 8), Rgb(40, 42, 48), Rgb(100, 104, 112), Rgb(180, 184, 190), Rgb(245, 246, 248)),
            WaterfallPaletteKind.Heat => Multi(
                t,
                Rgb(0, 0, 0), Rgb(80, 0, 90), Rgb(200, 20, 20), Rgb(255, 160, 0), Rgb(255, 255, 220)),
            _ => Multi(
                t,
                Rgb(8, 12, 28), Rgb(20, 70, 110), Rgb(40, 160, 120), Rgb(220, 170, 40), Rgb(255, 250, 230)),
        };
    }

    public static Color Background(WaterfallPaletteKind kind) => kind switch
    {
        WaterfallPaletteKind.Amber => Rgb(6, 4, 2),
        WaterfallPaletteKind.Ice => Rgb(2, 6, 14),
        WaterfallPaletteKind.Green => Rgb(2, 8, 4),
        WaterfallPaletteKind.Magenta => Rgb(8, 2, 12),
        WaterfallPaletteKind.Gray => Rgb(6, 6, 8),
        WaterfallPaletteKind.Heat => Rgb(0, 0, 0),
        _ => Rgb(8, 12, 18),
    };

    static Color Multi(float t, params Color[] stops)
    {
        if (stops.Length < 2) return stops[0];
        float scaled = t * (stops.Length - 1);
        int i = Math.Clamp((int)scaled, 0, stops.Length - 2);
        float local = scaled - i;
        return Lerp(stops[i], stops[i + 1], local);
    }

    static Color Lerp(Color a, Color b, float t) =>
        Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
