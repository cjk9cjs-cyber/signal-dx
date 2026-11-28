using SignalDx.Models;

namespace SignalDx.Ft8;

/// <summary>
/// Suggests full callsigns for a hashed <c>&lt;...&gt;</c> decode by looking at resolved calls
/// heard on the same audio frequency. Suggestions are guesses; the operator confirms the call.
/// </summary>
public static class HashedCallHint
{
    public const int HzTolerance = 3;

    public static bool IsUnresolved(DecodeHit hit) =>
        hit.Message?.Contains("<...>", StringComparison.Ordinal) == true;

    /// <param name="recent">Recent decodes, newest first.</param>
    public static IReadOnlyList<string> Suggest(DecodeHit target, IEnumerable<DecodeHit> recent, string myCall, int max = 3)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in recent)
        {
            if (hit.Message is null) continue;
            if (!string.Equals(hit.Mode, target.Mode, StringComparison.OrdinalIgnoreCase)) continue;
            if (Math.Abs(hit.Hz - target.Hz) > HzTolerance) continue;

            string? call = Ft8MessageBuilder.Parse(hit.Message).DxCall;
            if (string.IsNullOrWhiteSpace(call) || call.StartsWith('<')) continue;
            if (call.Equals(myCall?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(call)) continue;

            result.Add(call.ToUpperInvariant());
            if (result.Count >= max) break;
        }
        return result;
    }
}
