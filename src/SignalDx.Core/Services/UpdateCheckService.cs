using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SignalDx.Services;

public sealed record ReleaseUpdateInfo(
    Version Version,
    string TagName,
    string Name,
    string ReleaseNotes,
    string HtmlUrl,
    string? InstallerUrl,
    string? InstallerFileName,
    long InstallerSizeBytes,
    string? InstallerSha256);

public sealed record UpdateCheckResult(
    bool Ok,
    bool UpdateAvailable,
    ReleaseUpdateInfo? Release,
    string Message);

/// <summary>
/// Checks public GitHub Releases for a newer Signal DX version and downloads + verifies
/// the Windows Setup installer.
/// </summary>
public sealed class UpdateCheckService
{
    public const string DefaultLatestReleaseApiUrl =
        "https://api.github.com/repos/cjk9cjs-cyber/signal-dx/releases/latest";

    static readonly HttpClient ApiHttp = CreateClient(TimeSpan.FromSeconds(12));
    static readonly HttpClient DownloadHttp = CreateClient(TimeSpan.FromMinutes(10));

    static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SignalDx", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(
        string currentVersionText,
        CancellationToken ct = default,
        string? apiUrl = null)
    {
        if (!TryParseVersion(currentVersionText, out var current))
            current = new Version(0, 0, 0);

        try
        {
            using var resp = await ApiHttp.GetAsync(apiUrl ?? DefaultLatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    false, false, null,
                    $"Update check failed (HTTP {(int)resp.StatusCode}).");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReleaseJson(json, current);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, false, null, "Update check canceled.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, null, $"Update check failed: {ex.Message}");
        }
    }

    public static UpdateCheckResult ParseReleaseJson(string json, Version currentVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
                return new UpdateCheckResult(true, false, null, $"Up to date ({FormatVersion(currentVersion)}).");

            string tag = GetString(root, "tag_name");
            if (!TryParseVersion(tag, out var latest))
            {
                return new UpdateCheckResult(
                    false, false, null,
                    $"Could not read version from latest release tag '{tag}'.");
            }

            string name = GetString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = $"Signal DX {FormatVersion(latest)}";

            string body = GetString(root, "body");
            string htmlUrl = GetString(root, "html_url");

            string? installerUrl = null;
            string? installerName = null;
            long installerSize = 0;
            string? installerSha256 = null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string assetName = GetString(asset, "name");
                    if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isPreferred = assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase);
                    if (installerUrl is not null && !isPreferred)
                        continue;

                    string dlUrl = GetString(asset, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(dlUrl))
                        continue;

                    installerUrl = dlUrl;
                    installerName = assetName;
                    installerSize = asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long bytes)
                        ? bytes
                        : 0;
                    installerSha256 = ParseSha256Digest(GetString(asset, "digest"));

                    if (isPreferred)
                        break;
                }
            }

            var info = new ReleaseUpdateInfo(
                latest,
                tag,
                name,
                body,
                htmlUrl,
                installerUrl,
                installerName,
                installerSize,
                installerSha256);

            bool newer = Normalize3(latest) > Normalize3(currentVersion);
            string msg = newer
                ? $"Update {FormatVersion(latest)} available (current {FormatVersion(currentVersion)})."
                : $"You are up to date ({FormatVersion(currentVersion)}).";

            return new UpdateCheckResult(true, newer, info, msg);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, null, $"Invalid release metadata: {ex.Message}");
        }
    }

    public async Task<string> DownloadInstallerAsync(
        ReleaseUpdateInfo release,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.InstallerUrl))
            throw new InvalidOperationException("No Windows installer (.exe) is attached to this release.");

        string safeName = SanitizeFileName(
            release.InstallerFileName ?? $"SignalDx-{FormatVersion(release.Version)}-Setup.exe");
        string dir = Path.Combine(Path.GetTempPath(), "SignalDx", "Updates");
        Directory.CreateDirectory(dir);

        string finalPath = Path.Combine(dir, safeName);
        string partPath = finalPath + ".part";

        // Reuse an already-downloaded file only when we can verify its SHA-256 hash.
        if (File.Exists(finalPath) && !string.IsNullOrWhiteSpace(release.InstallerSha256) &&
            VerifySha256(finalPath, release.InstallerSha256))
        {
            progress?.Report(1.0);
            return finalPath;
        }

        try
        {
            using var resp = await DownloadHttp.GetAsync(
                release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? release.InstallerSizeBytes;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    downloaded += read;
                    if (total > 0)
                        progress?.Report(Math.Clamp((double)downloaded / total, 0.0, 1.0));
                }
            }

            if (!string.IsNullOrWhiteSpace(release.InstallerSha256) &&
                !VerifySha256(partPath, release.InstallerSha256))
            {
                TryDelete(partPath);
                throw new InvalidOperationException(
                    "Downloaded installer failed SHA-256 verification and was deleted.");
            }

            File.Move(partPath, finalPath, overwrite: true);
            progress?.Report(1.0);
            return finalPath;
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    public static bool VerifySha256(string filePath, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || !File.Exists(filePath))
            return false;

        string expected = expectedHex.Trim();
        if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expected = expected["sha256:".Length..].Trim();

        using var fs = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(fs);
        string actual = Convert.ToHexString(hash);
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];

        if (!Version.TryParse(s, out var parsed))
            return false;

        version = Normalize3(parsed);
        return true;
    }

    public static string FormatVersion(Version v)
    {
        var n = Normalize3(v);
        return $"{n.Major}.{n.Minor}.{n.Build}";
    }

    static Version Normalize3(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

    static string? ParseSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        string s = digest.Trim();
        if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            s = s["sha256:".Length..].Trim();
        return s.Length == 64 ? s : null;
    }

    static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    static string SanitizeFileName(string name)
    {
        string clean = Path.GetFileName(name);
        foreach (char c in Path.GetInvalidFileNameChars())
            clean = clean.Replace(c, '_');
        return string.IsNullOrWhiteSpace(clean) ? "SignalDx-Setup.exe" : clean;
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
