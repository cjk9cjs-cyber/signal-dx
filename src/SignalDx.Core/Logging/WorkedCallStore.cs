using SignalDx.Models;

namespace SignalDx.Logging;

/// <summary>
/// Running text file of previously logged DX callsigns (one per line).
/// Used to strikethrough / recolor Band Activity rows for stations already worked.
/// </summary>
public sealed class WorkedCallStore
{
    readonly string _path;
    readonly HashSet<string> _calls = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    public WorkedCallStore(string? path = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx");
        Directory.CreateDirectory(dir);
        _path = path ?? Path.Combine(dir, "worked-calls.txt");
        Load();
    }

    public string FilePath => _path;

    public int Count
    {
        get { lock (_gate) return _calls.Count; }
    }

    public bool Contains(string? callsign)
    {
        string call = Normalize(callsign);
        if (call.Length == 0) return false;
        lock (_gate) return _calls.Contains(call);
    }

    /// <summary>Append a logged DX call if new. Returns true when the file grew.</summary>
    public bool Add(string? callsign)
    {
        string call = Normalize(callsign);
        if (call.Length == 0) return false;

        lock (_gate)
        {
            if (!_calls.Add(call))
                return false;
            File.AppendAllText(_path, call + Environment.NewLine);
            return true;
        }
    }

    /// <summary>Seed from existing QSO log entries (first run / upgrade).</summary>
    public void MergeFromLog(IEnumerable<QsoEntry> entries)
    {
        bool dirty = false;
        lock (_gate)
        {
            foreach (var e in entries)
            {
                string call = Normalize(e.DxCall);
                if (call.Length == 0) continue;
                if (_calls.Add(call))
                    dirty = true;
            }

            if (dirty)
                RewriteLocked();
        }
    }

    void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            foreach (var line in File.ReadLines(_path))
            {
                string call = Normalize(line);
                if (call.Length > 0)
                    _calls.Add(call);
            }
        }
        catch
        {
            // Keep empty set if corrupt.
        }
    }

    void RewriteLocked()
    {
        var lines = _calls.OrderBy(c => c, StringComparer.Ordinal).Select(c => c + Environment.NewLine);
        File.WriteAllText(_path, string.Concat(lines));
    }

    static string Normalize(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return "";
        string c = callsign.Trim().ToUpperInvariant();
        // Drop portable suffixes for matching (W1ABC/P → W1ABC).
        int slash = c.IndexOf('/');
        if (slash > 0)
        {
            string right = c[(slash + 1)..];
            if (right is "P" or "M" or "QRP" or "A" or "B" or "MM" or "AM" or "LH")
                c = c[..slash];
        }
        return c;
    }
}
