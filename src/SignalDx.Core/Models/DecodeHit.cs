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
    /// <summary>Message is addressed to my call (someone calling / working me).</summary>
    public bool IsToMe { get; set; }
    /// <summary>Row involves the DX call currently being worked.</summary>
    public bool IsWorking { get; set; }
    public double? DistanceMiles { get; init; }

    /// <summary>CQ or 73/RR73 — same band-activity background highlight.</summary>
    public bool IsCqStyleHighlight
    {
        get
        {
            if (IsCq) return true;
            string msg = Message ?? "";
            if (msg.StartsWith('→') || msg.StartsWith("->", System.StringComparison.Ordinal))
                msg = msg.TrimStart('→', '-', ' ').Trim();
            var parts = msg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string last = parts[^1];
            return last.Equals("73", System.StringComparison.OrdinalIgnoreCase) ||
                   last.Equals("RR73", System.StringComparison.OrdinalIgnoreCase);
        }
    }

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
