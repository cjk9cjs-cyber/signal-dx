using System.Security.Cryptography;
using System.Text;
using SignalDx.Services;
using Xunit;

namespace SignalDx.Tests;

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v2.14.0", 2, 14, 0)]
    [InlineData("2.13.3", 2, 13, 3)]
    [InlineData("v2.14.0-rc1+abc123", 2, 14, 0)]
    [InlineData("2.15", 2, 15, 0)]
    public void TryParseVersion_ParsesValidTags(string raw, int major, int minor, int build)
    {
        Assert.True(UpdateCheckService.TryParseVersion(raw, out var ver));
        Assert.Equal(new Version(major, minor, build), ver);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    public void TryParseVersion_RejectsInvalid(string raw)
    {
        Assert.False(UpdateCheckService.TryParseVersion(raw, out _));
    }

    [Fact]
    public void ParseReleaseJson_DetectsNewerVersionAndSetupAsset()
    {
        const string json = """
        {
          "tag_name": "v2.15.0",
          "name": "Signal DX v2.15.0",
          "body": "Highlights:\n- Cool new feature",
          "html_url": "https://github.com/cjk9cjs-cyber/signal-dx/releases/tag/v2.15.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-11-24T12:00:00Z",
          "assets": [
            {
              "name": "SignalDx-HOWTO.pdf",
              "browser_download_url": "https://example.invalid/SignalDx-HOWTO.pdf",
              "size": 12345
            },
            {
              "name": "SignalDx-2.15.0-Setup.exe",
              "browser_download_url": "https://example.invalid/SignalDx-2.15.0-Setup.exe",
              "size": 52428800,
              "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            }
          ]
        }
        """;

        var result = UpdateCheckService.ParseReleaseJson(json, new Version(2, 14, 0));

        Assert.True(result.Ok);
        Assert.True(result.UpdateAvailable);
        Assert.NotNull(result.Release);
        Assert.Equal(new Version(2, 15, 0), result.Release!.Version);
        Assert.Equal("SignalDx-2.15.0-Setup.exe", result.Release.InstallerFileName);
        Assert.Equal("https://example.invalid/SignalDx-2.15.0-Setup.exe", result.Release.InstallerUrl);
        Assert.Equal(52428800, result.Release.InstallerSizeBytes);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Release.InstallerSha256);
    }

    [Fact]
    public void ParseReleaseJson_ReportsUpToDateWhenSameOrOlder()
    {
        const string json = """
        {
          "tag_name": "v2.14.0",
          "name": "Signal DX v2.14.0",
          "body": "",
          "html_url": "https://github.com/cjk9cjs-cyber/signal-dx/releases/tag/v2.14.0",
          "draft": false,
          "prerelease": false,
          "assets": []
        }
        """;

        var same = UpdateCheckService.ParseReleaseJson(json, new Version(2, 14, 0));
        Assert.True(same.Ok);
        Assert.False(same.UpdateAvailable);

        var older = UpdateCheckService.ParseReleaseJson(json, new Version(2, 14, 1));
        Assert.True(older.Ok);
        Assert.False(older.UpdateAvailable);
    }

    [Fact]
    public void ParseReleaseJson_IgnoresDraftRelease()
    {
        const string json = """
        {
          "tag_name": "v9.9.9",
          "draft": true,
          "prerelease": false,
          "assets": []
        }
        """;

        var result = UpdateCheckService.ParseReleaseJson(json, new Version(2, 14, 0));
        Assert.True(result.Ok);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public void VerifySha256_MatchesAndDetectsMismatch()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"signaldx-sha-test-{Guid.NewGuid():N}.bin");
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("Signal DX installer test payload");
            File.WriteAllBytes(tempFile, payload);
            string expectedHex = Convert.ToHexString(SHA256.HashData(payload));

            Assert.True(UpdateCheckService.VerifySha256(tempFile, expectedHex.ToLowerInvariant()));
            Assert.True(UpdateCheckService.VerifySha256(tempFile, expectedHex.ToUpperInvariant()));
            Assert.False(UpdateCheckService.VerifySha256(tempFile, new string('0', 64)));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
