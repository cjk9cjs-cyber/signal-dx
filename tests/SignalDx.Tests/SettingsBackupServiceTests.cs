using System.IO.Compression;
using SignalDx.Services;
using Xunit;

namespace SignalDx.Tests;

public class SettingsBackupServiceTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "sdx-backup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void BackupThenRestore_RoundTripsFilesOnNextStart()
    {
        string live = Path.Combine(_root, "live");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "settings.json"), "{\"MyCall\":\"K9CJS\"}");
        File.WriteAllText(Path.Combine(live, "worked-calls.txt"), "K1ABC\n");
        File.WriteAllText(Path.Combine(live, "tx-log.txt"), "not backed up");
        string zip = Path.Combine(_root, "backup.zip");

        var result = SettingsBackupService.CreateBackup(zip, "9.9.9", live);
        Assert.Equal(2, result.FileCount);
        using (var archive = ZipFile.OpenRead(zip))
            Assert.DoesNotContain(archive.Entries, e => e.FullName == "tx-log.txt");

        string fresh = Path.Combine(_root, "fresh");
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(fresh, "settings.json"), "{}");

        Assert.Equal(2, SettingsBackupService.StageRestore(zip, fresh));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(fresh, "settings.json")));

        SettingsBackupService.ApplyPendingRestore(fresh);
        Assert.Equal("{\"MyCall\":\"K9CJS\"}", File.ReadAllText(Path.Combine(fresh, "settings.json")));
        Assert.Equal("K1ABC\n", File.ReadAllText(Path.Combine(fresh, "worked-calls.txt")));
        Assert.False(Directory.Exists(Path.Combine(fresh, "restore-pending")));
    }

    [Fact]
    public void StageRestore_RejectsZipWithoutBackupFiles()
    {
        Directory.CreateDirectory(_root);
        string zip = Path.Combine(_root, "other.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("readme.txt");

        Assert.Throws<InvalidDataException>(() => SettingsBackupService.StageRestore(zip, Path.Combine(_root, "d")));
    }
}
