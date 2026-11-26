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

    /// <summary>Seed directed-CQ macros on first run (null CustomMacros in settings).</summary>
    public static IReadOnlyList<CustomMacroEntry> DefaultCustomMacros { get; } =
    [
        new() { Label = "CQ DX", Template = "CQ DX {mycall} {mygrid}" },
        new() { Label = "CQ EU", Template = "CQ EU {mycall} {mygrid}" },
    ];

    public static TxMacro ToTxMacro(CustomMacroEntry entry) =>
        new()
        {
            Kind = TxMacroKind.Custom,
            Label = string.IsNullOrWhiteSpace(entry.Label) ? "Custom" : entry.Label.Trim(),
            Template = (entry.Template ?? "").Trim(),
            CustomId = entry.Id
        };

    public static string Build(string template, StationSettings station, string dxCall, string snr)
    {
        return template
            .Replace("{mycall}", station.MyCall.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{mygrid}", station.MyGrid.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{dxcall}", string.IsNullOrWhiteSpace(dxCall) ? "DXCALL" : dxCall.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{snr}", string.IsNullOrWhiteSpace(snr) ? "+00" : snr.Trim(), StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    /// <summary>
    /// Parse FT8 text. DxCall is always the transmitting station (second callsign / CQ call),
    /// never the station being addressed — so Band Activity flags match the signal on air.
    /// CQ CALL GRID → CALL.  CALLED CALLER GRID/SNR → CALLER.
    /// </summary>
    public static (string? DxCall, string? DxGrid, bool IsCq) Parse(string message)
    {
        // Strip local TX arrow if present.
        string msg = (message ?? "").Trim();
        if (msg.StartsWith('→') || msg.StartsWith("->", StringComparison.Ordinal))
            msg = msg.TrimStart('→', '-', '>', ' ').Trim();

        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, null, false);

        bool isCq = parts[0] is "CQ" or "QRZ" or "DE";
        if (isCq)
        {
            string? call = parts.Length > 1 ? UnwrapHashed(parts[1]) : null;
            // Directed CQ: CQ DX K1ABC FN42
            if (parts.Length >= 4 && call is "DX" or "NA" or "EU" or "AS" or "SA" or "OC" or "AF" or "POTA" or "SOTA")
                call = UnwrapHashed(parts[2]);
            if (!LooksLikeCall(call))
                call = null;
            string? grid = LooksLikeGrid(parts[^1]) ? parts[^1] : null;
            return (call, grid, parts[0].Equals("CQ", StringComparison.OrdinalIgnoreCase));
        }

        // Standard QSO: CALLED CALLER [grid|report|…] — caller (2nd) is the transmitter.
        string? dx = UnwrapHashed(parts.Length >= 2 ? parts[1] : parts[0]);
        if (!LooksLikeCall(dx))
            dx = null;
        string? g = parts.Length >= 3 && LooksLikeGrid(parts[^1]) ? parts[^1] : null;
        return (dx, g, false);
    }

    /// <summary>"&lt;K1ABC&gt;" (hash resolved by the decoder) becomes "K1ABC"; "&lt;...&gt;" stays unresolved.</summary>
    public static string? UnwrapHashed(string? s)
    {
        if (s is null) return null;
        s = s.Trim();
        if (s.Length > 2 && s[0] == '<' && s[^1] == '>')
            return s[1..^1].Trim();
        return s;
    }

    /// <summary>True if token looks like a callsign (not a report / 73 / grid).</summary>
    public static bool LooksLikeCall(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length is < 3 or > 18) return false;
        if (s.StartsWith('<')) return false; // hashed / unresolved
        if (s is "CQ" or "QRZ" or "DE" or "DX" or "RRR" or "RR73" or "73") return false;
        if (LooksLikeGrid(s)) return false;
        if (s[0] is '+' or '-') return false; // SNR report
        if (char.IsDigit(s[0]) && !s.Any(char.IsLetter)) return false; // bare number, not a call (5R8IC, 3Y0X, 9A1A are calls)
        if (s.StartsWith('R') && s.Length >= 2 && (s[1] is '+' or '-' || char.IsDigit(s[1])))
            return false;
        // Must contain a digit (standard call) or slash compound.
        return s.Any(char.IsDigit) || s.Contains('/');
    }

    static bool LooksLikeGrid(string s)
    {
        if (s.Length is not (4 or 6)) return false;
        if (s.Equals("RR73", StringComparison.OrdinalIgnoreCase))
            return false;
        return char.IsLetter(s[0]) && char.IsLetter(s[1])
               && char.IsDigit(s[2]) && char.IsDigit(s[3]);
    }
}
