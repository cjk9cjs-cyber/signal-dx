using System.Text.Json;
using SignalDx.Models;

namespace SignalDx.Logging;

/// <summary>
/// Persists the Wanted list (callsigns / states / countries) under %LocalAppData%\SignalDx\wanted.json.
/// </summary>
public sealed class WantedStore
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly string _path;
    readonly List<WantedEntry> _items = [];
    readonly object _gate = new();

    public WantedStore(string? path = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx");
        Directory.CreateDirectory(dir);
        _path = path ?? Path.Combine(dir, "wanted.json");
        Load();
    }

    public string FilePath => _path;

    public IReadOnlyList<WantedEntry> Snapshot()
    {
        lock (_gate)
            return _items.Select(Clone).ToList();
    }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public bool Add(WantedEntry entry)
    {
        string norm = entry.Normalize();
        if (norm.Length == 0) return false;
        entry.Value = norm;

        lock (_gate)
        {
            if (_items.Any(e => e.Kind == entry.Kind &&
                                string.Equals(e.Normalize(), norm, StringComparison.OrdinalIgnoreCase)))
                return false;
            _items.Add(Clone(entry));
            SaveLocked();
            return true;
        }
    }

    public bool Remove(WantedEntry entry)
    {
        lock (_gate)
        {
            string norm = entry.Normalize();
            int removed = _items.RemoveAll(e =>
                e.Kind == entry.Kind &&
                string.Equals(e.Normalize(), norm, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            SaveLocked();
            return true;
        }
    }

    public void ReplaceAll(IEnumerable<WantedEntry> entries)
    {
        lock (_gate)
        {
            _items.Clear();
            foreach (var e in entries)
            {
                var c = Clone(e);
                c.Value = c.Normalize();
                if (c.Value.Length == 0) continue;
                if (_items.Any(x => x.Kind == c.Kind &&
                                    string.Equals(x.Value, c.Value, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _items.Add(c);
            }
            SaveLocked();
        }
    }

    /// <summary>
    /// Match a decode's transmitter against the wanted list.
    /// Returns a short reason string when matched, otherwise null.
    /// </summary>
    public string? Match(DecodeHit hit)
    {
        if (hit is null) return null;
        string call = (hit.DxCall ?? "").Trim();
        string state = (hit.State ?? "").Trim();
        string country = (hit.Country ?? "").Trim();
        string? iso = Geo.CountryFlag.ToIso(country, state);

        lock (_gate)
        {
            foreach (var e in _items)
            {
                string v = e.Normalize();
                if (v.Length == 0) continue;

                switch (e.Kind)
                {
                    case WantedKind.Call:
                        if (call.Length > 0 &&
                            string.Equals(call, v, StringComparison.OrdinalIgnoreCase))
                            return $"call {v}";
                        break;
                    case WantedKind.State:
                        if (state.Length > 0 &&
                            string.Equals(state, v, StringComparison.OrdinalIgnoreCase))
                            return $"state {v}";
                        break;
                    case WantedKind.Country:
                        if (country.Length > 0 &&
                            (country.Equals(v, StringComparison.OrdinalIgnoreCase) ||
                             country.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                             v.Contains(country, StringComparison.OrdinalIgnoreCase)))
                            return $"country {v}";
                        if (iso is not null &&
                            (iso.Equals(v, StringComparison.OrdinalIgnoreCase) ||
                             v.Equals(iso, StringComparison.OrdinalIgnoreCase)))
                            return $"country {v}";
                        break;
                }
            }
        }

        return null;
    }

    void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            string json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<WantedEntry>>(json, JsonOpts);
            if (list is null) return;
            lock (_gate)
            {
                _items.Clear();
                foreach (var e in list)
                {
                    if (e is null) continue;
                    e.Value = e.Normalize();
                    if (e.Value.Length == 0) continue;
                    _items.Add(e);
                }
            }
        }
        catch
        {
            // keep empty
        }
    }

    void SaveLocked()
    {
        var payload = _items.Select(Clone).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(payload, JsonOpts));
    }

    static WantedEntry Clone(WantedEntry e) =>
        new() { Kind = e.Kind, Value = e.Value ?? "" };
}
