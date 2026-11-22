using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text;
using SignalDx.Models;

namespace SignalDx.Logging;

/// <summary>
/// Uploads decoded WSPR spots to wsprnet.org.
/// Uses the single-spot POST form documented by OpenWebRX / multi-wspr
/// (<c>http://wsprnet.org/post/</c> with <c>function=wspr</c>).
/// Batch MEPT uploads (<c>/meptspots.php</c>) are used when several spots queue.
/// </summary>
public sealed class WsprNetClient : IAsyncDisposable
{
    const string SingleSpotUrl = "http://wsprnet.org/post/";
    const string MeptUrl = "http://wsprnet.org/meptspots.php";
    const string SoftwareId = "SignalDX";
    static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(90);

    readonly object _gate = new();
    readonly List<WsprSpot> _pending = [];
    readonly ConcurrentDictionary<string, long> _recent = new(StringComparer.OrdinalIgnoreCase);
    readonly CancellationTokenSource _cts = new();
    readonly Task _loop;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    string _rxCall = "";
    string _rxGrid = "";
    bool _enabled;
    int _sentTotal;

    public event Action<string>? StatusChanged;
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public int SentTotal => _sentTotal;

    public WsprNetClient()
    {
        _loop = Task.Run(FlushLoopAsync);
    }

    public void Configure(StationSettings settings)
    {
        _enabled = settings.WsprUploadSpots;
        _rxCall = (settings.MyCall ?? "").Trim().ToUpperInvariant();
        _rxGrid = NormalizeGrid(settings.MyGrid);
        if (_enabled && !string.IsNullOrWhiteSpace(_rxCall))
            StatusChanged?.Invoke($"WSPRnet on ({_rxCall} {_rxGrid})");
        else if (!_enabled)
            StatusChanged?.Invoke("WSPRnet upload off");
    }

    public void Enqueue(IEnumerable<DecodeHit> hits, StationSettings settings)
    {
        if (!settings.WsprUploadSpots) return;
        bool wspr = settings.Mode.Equals("WSPR2", StringComparison.OrdinalIgnoreCase) ||
                    settings.Mode.Equals("WSPR", StringComparison.OrdinalIgnoreCase);
        if (!wspr) return;

        string my = (settings.MyCall ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(my)) return;
        _rxCall = my;
        _rxGrid = NormalizeGrid(settings.MyGrid);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int added = 0;
        lock (_gate)
        {
            foreach (var hit in hits)
            {
                if (!TryParseSpot(hit, settings, out var spot)) continue;
                string key = $"{spot.Date}{spot.Time}|{spot.TxCall}|{spot.FreqMhz:F6}";
                if (_recent.TryGetValue(key, out long seen) && now - seen < 600)
                    continue;
                _recent[key] = now;
                _pending.Add(spot);
                added++;
            }
        }

        if (added > 0)
            StatusChanged?.Invoke($"WSPRnet queued +{added} (pending {PendingCount})");
    }

    async Task FlushLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, _cts.Token).ConfigureAwait(false);
                await FlushAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"WSPRnet flush error: {ex.Message}");
            }
        }
    }

    async Task FlushAsync(CancellationToken ct)
    {
        List<WsprSpot> batch;
        lock (_gate)
        {
            if (_pending.Count == 0 || !_enabled || string.IsNullOrWhiteSpace(_rxCall))
                return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        try
        {
            if (batch.Count == 1)
                await UploadSingleAsync(batch[0], ct).ConfigureAwait(false);
            else
                await UploadMeptAsync(batch, ct).ConfigureAwait(false);

            Interlocked.Add(ref _sentTotal, batch.Count);
            StatusChanged?.Invoke($"WSPRnet uploaded {batch.Count} (total {_sentTotal})");
        }
        catch (Exception ex)
        {
            lock (_gate)
                _pending.InsertRange(0, batch);
            StatusChanged?.Invoke($"WSPRnet upload failed: {ex.Message}");
        }
    }

    async Task UploadSingleAsync(WsprSpot spot, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["function"] = "wspr",
            ["date"] = spot.Date,
            ["time"] = spot.Time,
            ["sig"] = spot.Snr.ToString("0", CultureInfo.InvariantCulture),
            ["dt"] = spot.Dt.ToString("0.0", CultureInfo.InvariantCulture),
            ["drift"] = "0",
            ["tqrg"] = spot.FreqMhz.ToString("0.000000", CultureInfo.InvariantCulture),
            ["tcall"] = spot.TxCall,
            ["tgrid"] = spot.TxGrid,
            ["dbm"] = spot.Dbm.ToString(CultureInfo.InvariantCulture),
            ["version"] = SoftwareId,
            ["rcall"] = _rxCall,
            ["rgrid"] = _rxGrid,
            ["rqrg"] = spot.DialMhz.ToString("0.000000", CultureInfo.InvariantCulture),
            ["mode"] = "2" // WSPR-2
        };

        using var content = new FormUrlEncodedContent(form);
        using var resp = await _http.PostAsync(SingleSpotUrl, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    async Task UploadMeptAsync(IReadOnlyList<WsprSpot> spots, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var s in spots)
        {
            // ALL_MEPT 11-field line: date time sync snr dt freq call grid dbm drift mode
            sb.Append(s.Date).Append(' ')
              .Append(s.Time).Append(' ')
              .Append("10").Append(' ')
              .Append(s.Snr.ToString("0", CultureInfo.InvariantCulture)).Append(' ')
              .Append(s.Dt.ToString("0.0", CultureInfo.InvariantCulture)).Append(' ')
              .Append(s.FreqMhz.ToString("0.000000", CultureInfo.InvariantCulture)).Append(' ')
              .Append(s.TxCall).Append(' ')
              .Append(string.IsNullOrWhiteSpace(s.TxGrid) ? "    " : s.TxGrid).Append(' ')
              .Append(s.Dbm).Append(' ')
              .Append('0').Append(' ')
              .Append('2')
              .Append('\n');
        }

        using var form = new MultipartFormDataContent();
        var fileBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "allmept", "ALL_MEPT.TXT");
        form.Add(new StringContent(_rxCall), "call");
        form.Add(new StringContent(_rxGrid), "grid");
        form.Add(new StringContent(SoftwareId), "version");

        using var resp = await _http.PostAsync(MeptUrl, form, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    static bool TryParseSpot(DecodeHit hit, StationSettings settings, out WsprSpot spot)
    {
        spot = default!;
        string msg = (hit.Message ?? "").Trim();
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[^1], out int dbm)) return false;
        string grid = parts[^2].Trim().ToUpperInvariant();
        if (grid.Length != 4) return false;
        string call = string.Join(" ", parts[..^2]).Trim().ToUpperInvariant();
        if (call.Length < 3) return false;

        // Skip our own TX echoes.
        if (call.Equals(settings.MyCall?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        DateTime utc = hit.Utc.ToUniversalTime();
        double dial = settings.DialMhz;
        double freq = dial + hit.Hz / 1_000_000.0;

        spot = new WsprSpot(
            Date: utc.ToString("yyMMdd", CultureInfo.InvariantCulture),
            Time: utc.ToString("HHmm", CultureInfo.InvariantCulture),
            Snr: hit.SnrDb,
            Dt: hit.Dt,
            FreqMhz: freq,
            DialMhz: dial,
            TxCall: call,
            TxGrid: grid,
            Dbm: dbm);
        return true;
    }

    static string NormalizeGrid(string? grid)
    {
        string g = (grid ?? "").Trim().ToUpperInvariant();
        if (g.Length >= 6) return g[..6];
        if (g.Length >= 4) return g[..4];
        return g;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
        try { await FlushAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* ignore */ }
        _http.Dispose();
        _cts.Dispose();
    }

    sealed record WsprSpot(
        string Date,
        string Time,
        double Snr,
        double Dt,
        double FreqMhz,
        double DialMhz,
        string TxCall,
        string TxGrid,
        int Dbm);
}
