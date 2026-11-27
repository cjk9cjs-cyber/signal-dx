using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class LotwClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    const string UploadUrl = "https://lotw.arrl.org/lotw/upload";
    static readonly string[] TqslCandidates =
    [
        @"C:\Program Files (x86)\TrustedQSL\tqsl.exe",
        @"C:\Program Files\TrustedQSL\tqsl.exe",
    ];

    public static string ResolveTqslPath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        return TqslCandidates.FirstOrDefault(File.Exists) ?? "";
    }

    public async Task<(bool Ok, string Message)> UploadAsync(
        QsoEntry qso, string tqslPath, string stationLocation, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stationLocation))
            return (false, "LoTW station location missing");
        string exe = ResolveTqslPath(tqslPath);
        if (exe.Length == 0)
            return (false, "LoTW: TQSL not found (set path in Settings)");

        string work = Path.Combine(Path.GetTempPath(), "SignalDx-LoTW-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string adi = Path.Combine(work, "qso.adi");
            string tq8 = Path.Combine(work, "qso.tq8");
            await File.WriteAllTextAsync(adi, AdifExporter.ToFile(new[] { qso }), Encoding.UTF8, ct)
                .ConfigureAwait(false);

            var (signed, detail) = await SignAsync(exe, stationLocation, adi, tq8, ct).ConfigureAwait(false);
            if (!signed || !File.Exists(tq8))
                return (false, "LoTW sign failed: " + detail);

            using var file = new ByteArrayContent(await File.ReadAllBytesAsync(tq8, ct).ConfigureAwait(false));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var form = new MultipartFormDataContent { { file, "upfile", "qso.tq8" } };
            using var resp = await Http.PostAsync(UploadUrl, form, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, $"LoTW HTTP {(int)resp.StatusCode}: {Summarize(body)}");
            if (body.Contains("error", StringComparison.OrdinalIgnoreCase))
                return (false, "LoTW: " + Summarize(body));
            return (true, "LoTW OK");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, "LoTW: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch { }
        }
    }

    static async Task<(bool Ok, string Detail)> SignAsync(
        string exe, string location, string adi, string tq8, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-q", "-x", "-a", "compliant", "-l", location, "-o", tq8, adi })
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi);
        if (proc is null)
            return (false, "could not start tqsl");

        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch { }
            return (false, "tqsl timed out");
        }

        string output = Summarize((await stdout.ConfigureAwait(false)) + " " + (await stderr.ConfigureAwait(false)));
        if (proc.ExitCode == 0)
            return (true, output);
        return (false, output.Length > 0 ? output : $"tqsl exit code {proc.ExitCode}");
    }

    static string Summarize(string text)
    {
        string plain = Regex.Replace(text, "<[^>]+>", " ");
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        return plain.Length > 200 ? plain[..200] : plain;
    }
}
