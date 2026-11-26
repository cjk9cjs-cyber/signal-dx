using System.IO.Compression;
using System.Text.Json;

namespace SignalDx.Services;

/// <summary>
/// Backs up and restores Signal DX user data (settings, logbook, wanted list, worked calls)
/// as a single zip so it survives a clean reinstall.
/// </summary>
public static class SettingsBackupService
{
    const string ManifestName = "backup-info.json";
    const string PendingFolder = "restore-pending";

    public static readonly IReadOnlyList<string> BackupFiles =
    [
        "settings.json",
        "qso-log.json",
        "signal-dx.adi",
        "wanted.json",
        "worked-calls.txt",
    ];

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SignalDx");

    static string PendingDirOf(string dataDir) => Path.Combine(dataDir, PendingFolder);

    public sealed record BackupResult(int FileCount, string Path);

    public static BackupResult CreateBackup(string zipPath, string appVersion, string? dataDir = null)
    {
        dataDir ??= DataDir;
        Directory.CreateDirectory(dataDir);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var present = BackupFiles.Where(f => File.Exists(Path.Combine(dataDir, f))).ToArray();

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string name in present)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var input = new FileStream(
                    Path.Combine(dataDir, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var output = entry.Open();
                input.CopyTo(output);
            }

            var manifest = zip.CreateEntry(ManifestName);
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(JsonSerializer.Serialize(new
            {
                app = "Signal DX",
                version = appVersion,
                createdUtc = DateTime.UtcNow.ToString("o"),
                files = present,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        return new BackupResult(present.Length, zipPath);
    }

    /// <summary>
    /// Validates the zip and stages its files; they replace the live data on the next app start
    /// (the running app would otherwise overwrite them when it exits).
    /// </summary>
    public static int StageRestore(string zipPath, string? dataDir = null)
    {
        dataDir ??= DataDir;
        string pendingDir = PendingDirOf(dataDir);

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries
            .Where(e => BackupFiles.Contains(e.FullName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
            throw new InvalidDataException("This zip does not contain Signal DX backup files.");

        if (Directory.Exists(pendingDir)) Directory.Delete(pendingDir, recursive: true);
        Directory.CreateDirectory(pendingDir);

        foreach (var entry in entries)
        {
            string name = BackupFiles.First(f => f.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase));
            entry.ExtractToFile(Path.Combine(pendingDir, name), overwrite: true);
        }

        return entries.Count;
    }

    /// <summary>Call once at startup before any store reads its file.</summary>
    public static void ApplyPendingRestore(string? dataDir = null)
    {
        dataDir ??= DataDir;
        string pendingDir = PendingDirOf(dataDir);

        try
        {
            if (!Directory.Exists(pendingDir)) return;

            Directory.CreateDirectory(dataDir);
            foreach (string file in Directory.GetFiles(pendingDir))
            {
                string name = Path.GetFileName(file);
                if (!BackupFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dataDir, name), overwrite: true);
            }

            Directory.Delete(pendingDir, recursive: true);
        }
        catch
        {
            // A failed restore must never block app start.
        }
    }
}
