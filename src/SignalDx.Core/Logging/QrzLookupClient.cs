using System.Collections.Concurrent;
using System.Net.Http;
using System.Xml.Linq;

namespace SignalDx.Logging;

public sealed record QrzCallInfo(
    string Call,
    string? Name,
    string? Grid,
    string? Country,
    string? State,
    string? County,
    string? Dxcc,
    string Summary);

/// <summary>QRZ.com XML Data API callsign lookup (requires QRZ login; full fields need Logbook Data sub).</summary>
public sealed class QrzLookupClient
{
    const string Agent = "SignalDX/1.0";
    static readonly Uri Endpoint = new("https://xmldata.qrz.com/xml/current/");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly ConcurrentDictionary<string, QrzCallInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    string? _sessionKey;
    string? _sessionUser;

    public void ClearSession()
    {
        _sessionKey = null;
        _sessionUser = null;
    }

    public async Task<(bool Ok, QrzCallInfo? Info, string Message)> LookupAsync(
        string callsign, string username, string password, CancellationToken ct = default)
    {
        string call = NormalizeCall(callsign);
        if (string.IsNullOrEmpty(call))
            return (false, null, "No callsign");

        if (_cache.TryGetValue(call, out var cached))
            return (true, cached, "cached");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, null, "QRZ username/password missing (Settings → Logbooks)");

        try
        {
            if (string.IsNullOrEmpty(_sessionKey) ||
                !string.Equals(_sessionUser, username.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var login = await LoginAsync(username.Trim(), password, ct).ConfigureAwait(false);
                if (!login.Ok)
                    return (false, null, login.Message);
            }

            var result = await FetchCallAsync(call, ct).ConfigureAwait(false);
            if (!result.Ok && result.Message.Contains("Session", StringComparison.OrdinalIgnoreCase))
            {
                ClearSession();
                var login = await LoginAsync(username.Trim(), password, ct).ConfigureAwait(false);
                if (!login.Ok)
                    return (false, null, login.Message);
                result = await FetchCallAsync(call, ct).ConfigureAwait(false);
            }

            if (result is { Ok: true, Info: not null })
                _cache[call] = result.Info;

            return result;
        }
        catch (Exception ex)
        {
            return (false, null, "QRZ: " + ex.Message);
        }
    }

    async Task<(bool Ok, string Message)> LoginAsync(string username, string password, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["agent"] = Agent
        });
        using var resp = await Http.PostAsync(Endpoint, content, ct).ConfigureAwait(false);
        string xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        string? key = SessionValue(doc, "Key");
        string? error = SessionValue(doc, "Error");
        if (!string.IsNullOrEmpty(error))
            return (false, "QRZ: " + error);
        if (string.IsNullOrEmpty(key))
            return (false, "QRZ: login failed (no session key)");
        _sessionKey = key;
        _sessionUser = username;
        return (true, "OK");
    }

    async Task<(bool Ok, QrzCallInfo? Info, string Message)> FetchCallAsync(string call, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_sessionKey))
            return (false, null, "QRZ: no session");

        var qs = new Dictionary<string, string>
        {
            ["s"] = _sessionKey,
            ["callsign"] = call
        };
        using var content = new FormUrlEncodedContent(qs);
        using var resp = await Http.PostAsync(Endpoint, content, ct).ConfigureAwait(false);
        string xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);

        string? sessionError = SessionValue(doc, "Error");
        if (!string.IsNullOrEmpty(sessionError))
            return (false, null, "QRZ: " + sessionError);

        var callNode = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals("Callsign", StringComparison.OrdinalIgnoreCase));
        if (callNode is null)
            return (false, null, $"QRZ: {call} not found");

        string Get(string name) =>
            callNode.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

        string fname = Get("fname");
        string name = Get("name");
        string fullName = string.Join(" ", new[] { fname, name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        string grid = Get("grid");
        string country = Get("country");
        string state = Get("state");
        string county = Get("county");
        string dxcc = Get("dxcc");
        string returnedCall = Get("call");
        if (string.IsNullOrEmpty(returnedCall))
            returnedCall = call;

        var parts = new List<string> { returnedCall };
        if (!string.IsNullOrEmpty(fullName)) parts.Add(fullName);
        if (!string.IsNullOrEmpty(grid)) parts.Add(grid);
        if (!string.IsNullOrEmpty(state) && !string.IsNullOrEmpty(country))
            parts.Add($"{state}, {country}");
        else if (!string.IsNullOrEmpty(country))
            parts.Add(country);
        else if (!string.IsNullOrEmpty(state))
            parts.Add(state);

        var info = new QrzCallInfo(
            returnedCall,
            string.IsNullOrEmpty(fullName) ? null : fullName,
            string.IsNullOrEmpty(grid) ? null : grid,
            string.IsNullOrEmpty(country) ? null : country,
            string.IsNullOrEmpty(state) ? null : state,
            string.IsNullOrEmpty(county) ? null : county,
            string.IsNullOrEmpty(dxcc) ? null : dxcc,
            string.Join(" · ", parts));

        return (true, info, "OK");
    }

    static string? SessionValue(XDocument doc, string localName) =>
        doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)
            && e.Parent is not null
            && e.Parent.Name.LocalName.Equals("Session", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim();

    public static string NormalizeCall(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return "";
        // Strip portable suffixes like /P /QRP for primary lookup, keep compound prefixes.
        string c = callsign.Trim().ToUpperInvariant();
        int slash = c.IndexOf('/');
        if (slash > 0)
        {
            string left = c[..slash];
            string right = c[(slash + 1)..];
            // US portable: K1ABC/P → K1ABC; prefix: W4/G4ABC → G4ABC often better as full
            if (right is "P" or "M" or "QRP" or "A" or "B" or "MM" or "AM")
                return left;
        }
        return c;
    }

    public static string BrowserUrl(string callsign)
    {
        string call = NormalizeCall(callsign);
        return string.IsNullOrEmpty(call)
            ? "https://www.qrz.com/"
            : $"https://www.qrz.com/db/{Uri.EscapeDataString(call)}";
    }
}
