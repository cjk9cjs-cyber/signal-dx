using SignalDx.Models;

namespace SignalDx.Ft8;

public static class Ft8MessageBuilder
{
    public static IReadOnlyList<TxMacro> DefaultMacros { get; } =
    [
        new() { Kind = TxMacroKind.Cq, Label = "CQ", Template = "CQ {mycall} {mygrid}" },
        new() { Kind = TxMacroKind.Report, Label = "Tx 1", Template = "{dxcall} {mycall} {mygrid}" },
        new() { Kind = TxMacroKind.RReport, Label = "Tx 2", Template = "{dxcall} {mycall} {snr}" },
        new() { Kind = TxMacroKind.Rrr, Label = "Tx 3", Template = "{dxcall} {mycall} R{snr}" },
        new() { Kind = TxMacroKind.SeventyThree, Label = "Tx 4", Template = "{dxcall} {mycall} RR73" },
        new() { Kind = TxMacroKind.FreeText, Label = "Tx 5", Template = "{dxcall} {mycall} 73" },
    ];

    public static string Build(string template, StationSettings station, string dxCall, string snr)
    {
        return template
            .Replace("{mycall}", station.MyCall.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{mygrid}", station.MyGrid.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{dxcall}", string.IsNullOrWhiteSpace(dxCall) ? "DXCALL" : dxCall.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{snr}", string.IsNullOrWhiteSpace(snr) ? "+00" : snr.Trim(), StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    public static (string? DxCall, string? DxGrid, bool IsCq) Parse(string message)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, null, false);

        bool isCq = parts[0].Equals("CQ", StringComparison.OrdinalIgnoreCase);
        if (isCq)
        {
            string? call = parts.Length > 1 ? parts[1] : null;
            // Directed CQ: CQ DX K1ABC FN42
            if (parts.Length >= 4 && call is "DX" or "NA" or "EU" or "AS" or "SA" or "OC" or "AF")
                call = parts[2];
            string? grid = LooksLikeGrid(parts[^1]) ? parts[^1] : null;
            return (call, grid, true);
        }

        string? dx = parts[0];
        string? g = parts.Length >= 3 && LooksLikeGrid(parts[^1]) ? parts[^1] : null;
        return (dx, g, false);
    }

    static bool LooksLikeGrid(string s) =>
        s.Length is 4 or 6
        && char.IsLetter(s[0]) && char.IsLetter(s[1])
        && char.IsDigit(s[2]) && char.IsDigit(s[3]);
}
