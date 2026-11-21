namespace SignalDx.Models;

public enum TxMacroKind
{
    Cq,
    Report,
    RReport,
    Rrr,
    SeventyThree,
    FreeText,
    Custom
}

public sealed class TxMacro
{
    public required TxMacroKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Template { get; init; }
    /// <summary>Stable id for persisted custom macros (null for built-in).</summary>
    public string? CustomId { get; init; }
}

/// <summary>User-defined TX macro (e.g. CQ DX / CQ EU), persisted in settings.</summary>
public sealed class CustomMacroEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Template { get; set; } = "";
}
