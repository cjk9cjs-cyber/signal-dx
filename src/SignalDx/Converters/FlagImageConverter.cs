using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SignalDx.Converters;

/// <summary>
/// ISO 3166-1 alpha-2 → embedded PNG flag under Assets/Flags/{iso}.png.
/// Windows often cannot render emoji flags, so we use real images.
/// </summary>
public sealed class FlagImageConverter : IValueConverter
{
    static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iso || iso.Length != 2)
            return null;

        return Cache.GetOrAdd(iso.ToLowerInvariant(), Load);
    }

    static BitmapImage? Load(string isoLower)
    {
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/Assets/Flags/{isoLower}.png",
                UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 40;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
