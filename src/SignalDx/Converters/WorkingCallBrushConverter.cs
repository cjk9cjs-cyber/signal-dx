using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SignalDx.Converters;

/// <summary>
/// RX/Band row colors:
/// Yellow — other station answered / called us (IsToMe).
/// Green — we are calling CQ or a specific transmitter (our TX / working call).
/// Muted — previously logged / worked station (also struck through in the row style).
/// Bindings: IsToMe, IsWorking, hit.DxCall, Message, window DxCall, IsPreviouslyWorked.
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
        bool isWorking = values[1] is true;
        string hitDx = values[2] as string ?? "";
        string message = values[3] as string ?? "";
        string workDx = (values[4] as string ?? "").Trim();
        bool previouslyWorked = values.Length >= 6 && values[5] is true;

        // Their answer / call to us stays yellow (even if worked before).
        if (isToMe)
            return Yellow;

        // Our outbound TX (CQ or call to a station) — green.
        if (message.StartsWith('→') || message.StartsWith("->", StringComparison.Ordinal))
            return Green;

        // Station we are working / calling — green until they answer (IsToMe).
        if (isWorking)
            return Green;

        if (!string.IsNullOrEmpty(workDx) &&
            (hitDx.Equals(workDx, StringComparison.OrdinalIgnoreCase) ||
             message.Contains(workDx, StringComparison.OrdinalIgnoreCase)))
            return Green;

        if (previouslyWorked)
            return Worked;

        return Normal;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
