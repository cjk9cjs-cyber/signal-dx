using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SignalDx.Converters;

/// <summary>
/// RX/Band row colors:
/// Yellow — other station answered / called us (IsToMe).
/// Green — our own outbound TX row, or a row transmitted by the station we are in a directed QSO with right now.
/// Muted — previously logged / worked station (also struck through in the row style).
/// Bindings: IsToMe, hit.DxCall, Message, ActiveQsoCall (empty when not in a directed QSO), IsPreviouslyWorked.
/// </summary>
public sealed class WorkingCallBrushConverter : IMultiValueConverter
{
    public static readonly SolidColorBrush Yellow = Create("#FFD400");
    public static readonly SolidColorBrush Green = Create("#4FCF8A");
    public static readonly SolidColorBrush Worked = Create("#6B7C92");
    public static readonly SolidColorBrush Normal = Create("#E7EEF8");

    static SolidColorBrush Create(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        b.Freeze();
        return b;
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 5) return Normal;

        bool isToMe = values[0] is true;
        string hitDx = (values[1] as string ?? "").Trim();
        string message = values[2] as string ?? "";
        string activeQso = (values[3] as string ?? "").Trim();
        bool previouslyWorked = values[4] is true;

        // Their answer / call to us stays yellow (even if worked before).
        if (isToMe)
            return Yellow;

        // Our outbound TX (CQ or call to a station) — green.
        if (message.StartsWith('→') || message.StartsWith("->", StringComparison.Ordinal))
            return Green;

        // Only the transmitter we are working in a live directed QSO. Not message text, not a stale DX box.
        if (activeQso.Length > 0 && hitDx.Equals(activeQso, StringComparison.OrdinalIgnoreCase))
            return Green;

        if (previouslyWorked)
            return Worked;

        return Normal;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
