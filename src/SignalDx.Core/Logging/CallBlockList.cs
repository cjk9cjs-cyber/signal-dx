namespace SignalDx.Logging;

/// <summary>
/// Blocked callsign matching. An entry is an exact call (K1ABC, also matches K1ABC/P and W4/K1ABC)
/// or a wildcard: K1* (starts with), *XYZ (ends with).
/// </summary>
public static class CallBlockList
{
    public static string? NormalizeEntry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim().ToUpperInvariant().Replace(" ", "");
        v = v.Trim('<', '>');
        string core = v.Trim('*');
        if (core.Length < 2) return null;
        foreach (char c in core)
        {
            if (!(char.IsLetterOrDigit(c) || c == '/'))
                return null;
        }
        return v;
    }

    public static bool IsBlocked(IEnumerable<string>? entries, string? call)
    {
        if (entries is null || string.IsNullOrWhiteSpace(call)) return false;
        string full = call.Trim().ToUpperInvariant().Trim('<', '>');
        if (full.Length == 0) return false;

        var candidates = new List<string> { full };
        if (full.Contains('/'))
            candidates.AddRange(full.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 3));

        foreach (string raw in entries)
        {
            string? e = NormalizeEntry(raw);
            if (e is null) continue;

            bool starts = e.EndsWith('*');
            bool ends = e.StartsWith('*');
            string core = e.Trim('*');

            foreach (string c in candidates)
            {
                if (starts && ends)
                {
                    if (c.Contains(core, StringComparison.Ordinal)) return true;
                }
                else if (starts)
                {
                    if (c.StartsWith(core, StringComparison.Ordinal)) return true;
                }
                else if (ends)
                {
                    if (c.EndsWith(core, StringComparison.Ordinal)) return true;
                }
                else if (c.Equals(core, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
