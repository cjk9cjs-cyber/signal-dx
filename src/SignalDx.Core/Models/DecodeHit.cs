namespace SignalDx.Models;

public sealed class DecodeHit
{
    public required DateTime Utc { get; init; }
    public required string Mode { get; init; }
    public required int Hz { get; init; }
    public required double SnrDb { get; init; }
    public required double Dt { get; init; }
    public required string Message { get; init; }
    public string? DxCall { get; init; }
    public string? DxGrid { get; init; }
    public string? Country { get; init; }
    public string? State { get; init; }
    public bool IsNew { get; init; }
    public bool IsCq { get; init; }
    public double? DistanceMiles { get; init; }

    public string DistanceText =>
        DistanceMiles is double mi ? $"{mi:0}" : "";

    public string CountryText =>
        !string.IsNullOrEmpty(State) && Country is "USA" or "United States"
            ? $"USA · {State}"
            : !string.IsNullOrEmpty(State) && Country is "Alaska" or "Hawaii" or "Puerto Rico"
                ? $"{Country} · {State}"
                : Country ?? "";

    public string StateText => State ?? "";
}
