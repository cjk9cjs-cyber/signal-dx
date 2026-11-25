using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SignalDx.Cat;

public sealed record HamlibModel(int Id, string Manufacturer, string Model, string Status, string Macro)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Manufacturer)
        ? $"{Id}: {Model}"
        : $"{Id}: {Manufacturer} {Model}";

    public string Hint => string.IsNullOrWhiteSpace(Status) ? Macro : Status;
}

public static class HamlibCatalog
{
    static readonly object Gate = new();
    static IReadOnlyList<HamlibModel>? _cache;

    public static IReadOnlyList<HamlibModel> GetModels(string? hamlibHintPath = null, bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (_cache is not null && !forceRefresh)
                return _cache;

            // A normal launch uses the bundled list. Shelling out to rigctl -l here runs on the
            // UI thread before the window exists, and an unread stderr pipe can block forever.
            var models = forceRefresh ? LoadFromRigctl(hamlibHintPath) : [];
            if (models.Count == 0)
                models = LoadFromEmbedded();
            if (models.Count == 0)
                models = LoadFromBundledFile();

            // HRD is not a Hamlib backend — inject a virtual selector entry (WSJT-X style).
            if (models.All(m => m.Id != HrdCatController.ModelId))
            {
                models.Insert(0, new HamlibModel(
                    HrdCatController.ModelId,
                    "Ham Radio Deluxe",
                    "HRD Rig Control",
                    "Stable",
                    "RIG_MODEL_HRD_VIRTUAL"));
            }

            _cache = models
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .OrderBy(m => m.Id == HrdCatController.ModelId ? -1 : m.Id)
                .ToList();
            return _cache;
        }
    }

    public static HamlibModel? Find(int id, string? hamlibHintPath = null) =>
        GetModels(hamlibHintPath).FirstOrDefault(m => m.Id == id);

    public static IReadOnlyList<HamlibModel> Filter(string? query, string? hamlibHintPath = null)
    {
        var all = GetModels(hamlibHintPath);
        if (string.IsNullOrWhiteSpace(query)) return all;
        string q = query.Trim();
        return all.Where(m =>
                m.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.Model.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.Macro.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    static List<HamlibModel> LoadFromRigctl(string? hintPath)
    {
        string? rigctl = HamlibPaths.FindRigctl(hintPath);
        if (rigctl is null) return [];

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rigctl,
                    Arguments = "-l",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!proc.Start()) return [];
            // Drain both pipes. Reading only stdout deadlocks once rigctl fills the stderr buffer.
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
            bool exited = proc.WaitForExit(8000);
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
                exited = proc.WaitForExit(2000);
            }
            if (!exited) return [];

            string output = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            var parsed = ParseList(output);
            if (parsed.Count > 0)
                TryCacheToDisk(output);
            return parsed;
        }
        catch
        {
            return [];
        }
    }

    static List<HamlibModel> LoadFromEmbedded()
    {
        var asm = typeof(HamlibCatalog).Assembly;
        string? res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("hamlib-models.txt", StringComparison.OrdinalIgnoreCase));
        if (res is null) return [];
        using var stream = asm.GetManifestResourceStream(res);
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return ParseList(reader.ReadToEnd());
    }

    static List<HamlibModel> LoadFromBundledFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "hamlib-models.txt");
        if (!File.Exists(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SignalDx", "hamlib-models.txt");
        }
        if (!File.Exists(path)) return [];
        return ParseList(File.ReadAllText(path));
    }

    static void TryCacheToDisk(string output)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SignalDx");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "hamlib-models.txt"), output);
        }
        catch
        {
            // ignore cache failures
        }
    }

    public static List<HamlibModel> ParseList(string text)
    {
        var list = new List<HamlibModel>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (line.Contains("Rig #", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith('-')) continue;

            var m = Regex.Match(line, @"^\s*(\d+)\s+(.*\S)\s*$");
            if (!m.Success) continue;

            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            string rest = m.Groups[2].Value;
            var parts = Regex.Split(rest, @"\s{2,}").Where(p => p.Length > 0).ToArray();
            if (parts.Length < 2) continue;

            string mfg = parts[0].Trim();
            string model = parts[1].Trim();
            string status = parts.Length >= 4 ? parts[3].Trim() : "";
            string macro = parts.Length >= 5 ? parts[4].Trim() : "";
            list.Add(new HamlibModel(id, mfg, model, status, macro));
        }
        return list;
    }
}
