using System.Text.Json;
using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class QsoLogStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly string _path;
    readonly List<QsoEntry> _entries = [];
    readonly object _gate = new();

    public QsoLogStore(string? path = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx");
        Directory.CreateDirectory(dir);
        _path = path ?? Path.Combine(dir, "qso-log.json");
        Load();
    }

    public IReadOnlyList<QsoEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public void Add(QsoEntry entry)
    {
        lock (_gate)
        {
            _entries.Insert(0, entry);
            Save();
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Id == id);
            Save();
        }
    }

    void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<QsoEntry>>(json);
            if (list is null) return;
            lock (_gate)
            {
                _entries.Clear();
                _entries.AddRange(list);
            }
        }
        catch
        {
            // Keep empty log if corrupt.
        }
    }

    void Save()
    {
        var snapshot = _entries.ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }
}
