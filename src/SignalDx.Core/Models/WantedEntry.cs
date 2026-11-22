namespace SignalDx.Models;

public enum WantedKind
{
    Call = 0,
    State = 1,
    Country = 2
}

/// <summary>A callsign, US/CA state, or country the operator wants to be alerted for.</summary>
public sealed class WantedEntry
{
    public WantedKind Kind { get; set; } = WantedKind.Call;
    public string Value { get; set; } = "";

    public string KindLabel => Kind switch
    {
        WantedKind.State => "State",
        WantedKind.Country => "Country",
        _ => "Call"
    };

    public string Display => $"{KindLabel}: {Value}";

    public string Normalize() => Kind switch
    {
        WantedKind.Call => (Value ?? "").Trim().ToUpperInvariant(),
        WantedKind.State => (Value ?? "").Trim().ToUpperInvariant(),
        _ => (Value ?? "").Trim()
    };

    public static WantedEntry? TryCreate(WantedKind kind, string? raw)
    {
        string v = (raw ?? "").Trim();
        if (v.Length == 0) return null;

        return kind switch
        {
            WantedKind.Call => new WantedEntry
            {
                Kind = WantedKind.Call,
                Value = v.ToUpperInvariant()
            },
            WantedKind.State => new WantedEntry
            {
                Kind = WantedKind.State,
                Value = v.Length <= 3 ? v.ToUpperInvariant() : v
            },
            WantedKind.Country => new WantedEntry
            {
                Kind = WantedKind.Country,
                Value = v
            },
            _ => null
        };
    }
}
