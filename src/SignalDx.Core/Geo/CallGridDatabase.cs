using SignalDx.Data;
using SignalDx.Logging;

namespace SignalDx.Geo;

/// <summary>
/// Callsign → Maidenhead grid from the bundled CALL3.TXT database (WSJT-X / JTDX format).
/// Entries are often 6-character, which places a station far more precisely than the
/// 4-character grid in an FT8 message.
/// </summary>
public static class CallGridDatabase
{
    static readonly Lazy<Dictionary<string, string>> Grids = new(Load);

    /// <summary>Grid for the callsign, preferring a 6-character entry, then the decoded grid, then a 4-character entry.</summary>
    public static string? PreferredGrid(string? callsign, string? decodedGrid)
    {
        string? stored = Lookup(callsign);
        if (stored is { Length: 6 }) return stored;
        if (!string.IsNullOrWhiteSpace(decodedGrid)) return decodedGrid;
        return stored;
    }

    public static string? Lookup(string? callsign)
    {
        string call = QrzLookupClient.NormalizeCall(callsign);
        if (call.Length == 0) return null;
        return Grids.Value.TryGetValue(call, out var grid) ? grid : null;
    }

    static Dictionary<string, string> Load()
    {
        var grids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in BundledData.Lines("CALL3.TXT"))
        {
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            string call = parts[0].Trim().ToUpperInvariant();
            string grid = parts[1].Trim().ToUpperInvariant();
            if (call.Length == 0 || grid.Length is not (4 or 6)) continue;

            if (!grids.TryGetValue(call, out var existing) || grid.Length > existing.Length)
                grids[call] = grid;
        }
        return grids;
    }
}
