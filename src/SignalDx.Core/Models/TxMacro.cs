namespace SignalDx.Models;

public enum TxMacroKind
{
    Cq,
    Report,
    RReport,
    Rrr,
    SeventyThree,
    FreeText
}

public sealed class TxMacro
{
    public required TxMacroKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Template { get; init; }
}
