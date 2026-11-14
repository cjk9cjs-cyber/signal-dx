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
    public bool IsNew { get; init; }
    public bool IsCq { get; init; }
}
