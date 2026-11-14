using System.Text.Json;
using SignalDx.Models;

namespace SignalDx.Services;

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly string _path;

    public SettingsStore(string? path = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx");
        Directory.CreateDirectory(dir);
        _path = path ?? Path.Combine(dir, "settings.json");
    }

    public StationSettings Load()
    {
        if (!File.Exists(_path)) return new StationSettings();
        try
        {
            return JsonSerializer.Deserialize<StationSettings>(File.ReadAllText(_path))
                   ?? new StationSettings();
        }
        catch
        {
            return new StationSettings();
        }
    }

    public void Save(StationSettings settings)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
