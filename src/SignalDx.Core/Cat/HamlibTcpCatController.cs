using System.Net.Sockets;
using System.Text;

namespace SignalDx.Cat;

/// <summary>
/// Full-ish Hamlib <c>rigctld</c> TCP client (default 127.0.0.1:4532).
/// Speaks the netrigctl protocol: command + newline, response lines ending with RPRT.
/// </summary>
public sealed class HamlibTcpCatController : ICatController
{
    readonly string _host;
    readonly int _port;
    TcpClient? _client;
    NetworkStream? _stream;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly StringBuilder _readBuf = new();

    public HamlibTcpCatController(string host, int port)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port <= 0 ? 4532 : port;
    }

    public string Name => $"Hamlib {_host}:{_port}";
    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _client = new TcpClient { NoDelay = true };
        using var reg = ct.Register(() => { try { _client.Close(); } catch { /* ignore */ } });
        await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        _stream = _client.GetStream();
        _readBuf.Clear();

        // Verify with a lightweight probe.
        await GetFrequencyHzAsync(ct).ConfigureAwait(false);
    }

    public Task DisconnectAsync()
    {
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _client?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _client = null;
        _readBuf.Clear();
        return Task.CompletedTask;
    }

    public async Task<long?> GetFrequencyHzAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("f", ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)) continue;
            if (long.TryParse(line.Trim(), out long hz)) return hz;
        }
        return null;
    }

    public async Task SetFrequencyHzAsync(long hz, CancellationToken ct = default) =>
        await EnsureOkAsync($"F {hz}", ct).ConfigureAwait(false);

    public async Task SetPttAsync(bool on, CancellationToken ct = default) =>
        await EnsureOkAsync(on ? "T 1" : "T 0", ct).ConfigureAwait(false);

    public Task SetModeUsbAsync(CancellationToken ct = default) =>
        SetModeAsync("USB", 0, ct);

    public async Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default)
    {
        string m = string.IsNullOrWhiteSpace(mode) ? "USB" : mode.Trim().ToUpperInvariant();
        await EnsureOkAsync($"M {m} {Math.Max(0, passbandHz)}", ct).ConfigureAwait(false);
    }

    public async Task SetRfPowerAsync(double level01, CancellationToken ct = default)
    {
        double v = Math.Clamp(level01, 0, 1);
        await EnsureOkAsync($"L RFPOWER {v.ToString(System.Globalization.CultureInfo.InvariantCulture)}", ct)
            .ConfigureAwait(false);
    }

    public async Task<double?> GetRfPowerAsync(CancellationToken ct = default) =>
        await GetLevelAsync("RFPOWER", ct).ConfigureAwait(false);

    public async Task<double?> GetAlcAsync(CancellationToken ct = default) =>
        await GetLevelAsync("ALC", ct).ConfigureAwait(false);

    async Task<double?> GetLevelAsync(string name, CancellationToken ct)
    {
        var lines = await CommandLinesAsync($"l {name}", ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(line.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
        }
        return null;
    }

    public async Task<bool?> GetPttAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("t", ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(line.Trim(), out int v)) return v != 0;
        }
        return null;
    }

    public async Task<(string? Mode, int Passband)> GetModeAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("m", ct).ConfigureAwait(false);
        string? mode = null;
        int pass = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)) continue;
            if (mode is null && !int.TryParse(line.Trim(), out _))
                mode = line.Trim();
            else if (int.TryParse(line.Trim(), out int p))
                pass = p;
        }
        return (mode, pass);
    }

    public async Task<string?> GetVfoAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("v", ct).ConfigureAwait(false);
        return lines.FirstOrDefault(l => !l.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase))?.Trim();
    }

    public async Task<string?> GetInfoAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("_", ct).ConfigureAwait(false);
        return string.Join(" | ", lines.Where(l => !l.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        long? hz = null;
        string? mode = null;
        int pass = 0;
        bool? ptt = null;
        string? vfo = null;
        string? info = null;
        double? rf = null;
        double? alc = null;

        try { hz = await GetFrequencyHzAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { (mode, pass) = await GetModeAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { ptt = await GetPttAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { vfo = await GetVfoAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { info = await GetInfoAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { rf = await GetRfPowerAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { alc = await GetAlcAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }

        return new RigSnapshot
        {
            FrequencyHz = hz,
            Mode = mode,
            PassbandHz = pass,
            Ptt = ptt,
            Vfo = vfo,
            RigInfo = info,
            RfPower = rf,
            Alc = alc
        };
    }

    async Task EnsureOkAsync(string cmd, CancellationToken ct)
    {
        var lines = await CommandLinesAsync(cmd, ct).ConfigureAwait(false);
        var rprt = lines.LastOrDefault(l => l.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase));
        if (rprt is null) return;
        var parts = rprt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[1], out int code) && code != 0)
            throw new InvalidOperationException($"Hamlib {cmd} failed: {rprt}");
    }

    async Task<List<string>> CommandLinesAsync(string cmd, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Hamlib not connected.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[] payload = Encoding.ASCII.GetBytes(cmd + "\n");
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            return await ReadUntilRprtAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<List<string>> ReadUntilRprtAsync(CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Hamlib not connected.");
        var lines = new List<string>();
        var buf = new byte[512];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(2500);

        while (!cts.IsCancellationRequested)
        {
            while (true)
            {
                string pending = _readBuf.ToString();
                int idx = pending.IndexOf('\n');
                if (idx < 0) break;
                string line = pending[..idx].Trim('\r', '\n', ' ');
                _readBuf.Remove(0, idx + 1);
                if (line.Length == 0) continue;
                lines.Add(line);
                if (line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase))
                    return lines;
            }

            int n;
            try
            {
                n = await _stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (n <= 0) break;
            _readBuf.Append(Encoding.ASCII.GetString(buf, 0, n));
        }

        // Some builds omit RPRT on get commands — return whatever we collected.
        if (_readBuf.Length > 0)
        {
            foreach (var extra in _readBuf.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                lines.Add(extra.Trim('\r', ' '));
            _readBuf.Clear();
        }

        return lines;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
