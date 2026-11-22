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
                Background(kind), Rgb(60, 30, 4), Rgb(160, 90, 10), Rgb(230, 170, 40), Rgb(255, 245, 200)),
            WaterfallPaletteKind.Ice => Multi(
                t,
                Background(kind), Rgb(10, 40, 90), Rgb(20, 110, 180), Rgb(120, 210, 255), Rgb(235, 250, 255)),
            WaterfallPaletteKind.Green => Multi(
                t,
                Background(kind), Rgb(8, 40, 16), Rgb(20, 110, 40), Rgb(90, 210, 80), Rgb(230, 255, 210)),
            WaterfallPaletteKind.Magenta => Multi(
                t,
                Background(kind), Rgb(50, 10, 70), Rgb(130, 30, 150), Rgb(230, 90, 180), Rgb(255, 230, 250)),
            WaterfallPaletteKind.Gray => Multi(
                t,
                Background(kind), Rgb(40, 42, 48), Rgb(100, 104, 112), Rgb(180, 184, 190), Rgb(245, 246, 248)),
            WaterfallPaletteKind.Heat => Multi(
                t,
                Background(kind), Rgb(80, 0, 90), Rgb(200, 20, 20), Rgb(255, 160, 0), Rgb(255, 255, 220)),
            _ => Multi(
                t,
                Background(kind), Rgb(18, 55, 95), Rgb(35, 140, 115), Rgb(210, 165, 40), Rgb(255, 250, 230)),
        };
    }

    /// <summary>Solid noise-floor color — kept uniform so the waterfall doesn't look grainy.</summary>
    public static Color Background(WaterfallPaletteKind kind) => kind switch
    {
        WaterfallPaletteKind.Amber => Rgb(4, 3, 1),
        WaterfallPaletteKind.Ice => Rgb(1, 4, 12),
        WaterfallPaletteKind.Green => Rgb(1, 6, 3),
        WaterfallPaletteKind.Magenta => Rgb(6, 1, 10),
        WaterfallPaletteKind.Gray => Rgb(4, 4, 6),
        WaterfallPaletteKind.Heat => Rgb(0, 0, 0),
        _ => Rgb(5, 8, 14),
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
