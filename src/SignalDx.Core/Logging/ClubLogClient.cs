using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class ClubLogClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<(bool Ok, string Message)> UploadAsync(
        QsoEntry qso, string email, string callsign, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return (false, "Club Log email/password missing");

        string adif = AdifExporter.ToRecord(qso);
        var form = new Dictionary<string, string>
        {
            ["email"] = email.Trim(),
            ["callsign"] = callsign.Trim().ToUpperInvariant(),
            ["password"] = password,
            ["api"] = "1",
            ["adif"] = adif
        };

        using var content = new FormUrlEncodedContent(form);
        using var resp = await Http.PostAsync("https://clublog.org/realtime.php", content, ct)
            .ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return (false, $"Club Log HTTP {(int)resp.StatusCode}: {body}");
        if (body.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Added", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(body))
            return (true, "Club Log OK");
        return (false, "Club Log: " + body.Trim());
    }
}
