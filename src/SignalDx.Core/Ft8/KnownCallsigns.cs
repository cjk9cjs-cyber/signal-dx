using SignalDx.Data;
using SignalDx.Logging;

namespace SignalDx.Ft8;

/// <summary>
/// Callsigns from the bundled ALLCALL7.TXT list. JTDX uses this list to filter likely
/// false FT8/FT4 decodes; it is built from PSK Reporter spots, so it can miss real rare stations.
/// </summary>
public static class KnownCallsigns
{
    static readonly Lazy<HashSet<string>> Calls = new(Load);

    public static bool Contains(string? callsign)
    {
        var calls = Calls.Value;
        if (calls.Count == 0) return true;

        string call = QrzLookupClient.NormalizeCall(callsign);
        return call.Length > 0 && calls.Contains(call);
    }

    static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in BundledData.Lines("ALLCALL7.TXT"))
        {
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            int comma = line.IndexOf(',');
            string call = (comma >= 0 ? line[..comma] : line).Trim().ToUpperInvariant();
            if (call.Length > 0) set.Add(call);
        }
        return set;
    }
}
