using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class QrzLogClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<(bool Ok, string Message)> UploadAsync(
        QsoEntry qso, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "QRZ API key missing");

        string adif = AdifExporter.ToRecord(qso);
        var form = new Dictionary<string, string>
        {
            ["KEY"] = apiKey.Trim(),
            ["ACTION"] = "INSERT",
            ["ADIF"] = adif
        };

        using var content = new FormUrlEncodedContent(form);
        using var resp = await Http.PostAsync("https://logbook.qrz.com/api", content, ct)
            .ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (body.Contains("RESULT=OK", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("RESULT=EXISTS", StringComparison.OrdinalIgnoreCase))
            return (true, "QRZ OK");
        return (false, "QRZ: " + body.Trim());
    }
}
