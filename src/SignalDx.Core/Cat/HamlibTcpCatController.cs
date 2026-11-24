using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace SignalDx.Cat;

/// <summary>
/// Hamlib <c>rigctld</c> TCP client (default 127.0.0.1:4532).
/// Uses the extended response protocol (<c>+</c> prefix) so every command — including getters —
/// terminates immediately with <c>RPRT &lt;code&gt;</c> instead of waiting for a read timeout.
/// </summary>
public sealed class HamlibTcpCatController : ICatController
{
    readonly string _host;
    readonly int _port;
    TcpClient? _client;
    NetworkStream? _stream;
    volatile bool _connected;
    string? _rigInfo;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly StringBuilder _readBuf = new();

    public HamlibTcpCatController(string host, int port)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port <= 0 ? 4532 : port;
    }

    public string Name => $"Hamlib {_host}:{_port}";
    public bool IsConnected => _connected && _client is not null && _stream is not null;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var client = new TcpClient { NoDelay = true };
        using var reg = ct.Register(() => { try { client.Close(); } catch { /* ignore */ } });
        try
        {
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            _client = client;
            _stream = client.GetStream();
            _readBuf.Clear();
            _connected = true;

            // Verify with a lightweight probe.
            await GetFrequencyHzAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        _rigInfo = null;
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
        if (HasErrorRprt(lines)) return null;
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_freq")) continue;
            string val = StripFieldPrefix(line, "Frequency:");
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long hz))
                return hz;
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
        // Passband 0 in Hamlib means RIG_PASSBAND_NORMAL (which narrows Yaesu PKTUSB to ~600 Hz).
        // Use -1 (RIG_PASSBAND_NOCHANGE) when no explicit passband is requested.
        int pb = passbandHz > 0 ? passbandHz : -1;
        await EnsureOkAsync($"M {m} {pb}", ct).ConfigureAwait(false);
    }

    public async Task SetRfPowerAsync(double level01, CancellationToken ct = default)
    {
        double v = Math.Clamp(level01, 0, 1);
        await EnsureOkAsync($"L RFPOWER {v.ToString(CultureInfo.InvariantCulture)}", ct)
            .ConfigureAwait(false);
    }

    public async Task SetAudioDriveAsync(double level01, CancellationToken ct = default)
    {
        double v = Math.Clamp(level01, 0, 1);
        string s = v.ToString(CultureInfo.InvariantCulture);
        // Try common Hamlib TX-audio levels; first success wins.
        foreach (var name in new[] { "MICGAIN", "AF", "USB_AF", "COMP" })
        {
            try
            {
                await EnsureOkAsync($"L {name} {s}", ct).ConfigureAwait(false);
                return;
            }
            catch
            {
                // try next
            }
        }
    }

    public async Task<double?> GetRfPowerAsync(CancellationToken ct = default) =>
        await GetLevelAsync("RFPOWER", ct).ConfigureAwait(false);

    public async Task<double?> GetAlcAsync(CancellationToken ct = default) =>
        await GetLevelAsync("ALC", ct).ConfigureAwait(false);

    async Task<double?> GetLevelAsync(string name, CancellationToken ct)
    {
        var lines = await CommandLinesAsync($"l {name}", ct).ConfigureAwait(false);
        if (HasErrorRprt(lines)) return null;
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_level")) continue;
            string val = StripAnyKeyPrefix(line);
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
        }
        return null;
    }

    public async Task<bool?> GetPttAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("t", ct).ConfigureAwait(false);
        if (HasErrorRprt(lines)) return null;
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_ptt")) continue;
            string val = StripFieldPrefix(line, "PTT:");
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v != 0;
        }
        return null;
    }

    public async Task<(string? Mode, int Passband)> GetModeAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("m", ct).ConfigureAwait(false);
        if (HasErrorRprt(lines)) return (null, 0);
        string? mode = null;
        int pass = 0;
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_mode")) continue;
            if (line.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase))
            {
                mode = line["Mode:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("Passband:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line["Passband:".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
                    pass = p;
                continue;
            }
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
        if (HasErrorRprt(lines)) return null;
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_vfo")) continue;
            string val = StripFieldPrefix(line, "VFO:");
            if (val.Length > 0) return val;
        }
        return null;
    }

    public async Task<string?> GetInfoAsync(CancellationToken ct = default)
    {
        var lines = await CommandLinesAsync("_", ct).ConfigureAwait(false);
        if (HasErrorRprt(lines)) return null;
        var parts = new List<string>();
        foreach (var line in lines)
        {
            if (IsMetaOrEchoLine(line, "get_info")) continue;
            string val = StripFieldPrefix(line, "Info:");
            if (val.Length > 0) parts.Add(val);
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    public async Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        long? hz = null;
        string? mode = null;
        int pass = 0;
        bool? ptt = null;
        string? vfo = null;
        double? rf = null;
        double? alc = null;

        try { hz = await GetFrequencyHzAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { (mode, pass) = await GetModeAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { ptt = await GetPttAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { vfo = await GetVfoAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        if (_rigInfo is null)
        {
            try { _rigInfo = await GetInfoAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        }
        try { rf = await GetRfPowerAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try { alc = await GetAlcAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }

        return new RigSnapshot
        {
            FrequencyHz = hz,
            Mode = mode,
            PassbandHz = pass,
            Ptt = ptt,
            Vfo = vfo,
            RigInfo = _rigInfo,
            RfPower = rf,
            Alc = alc
        };
    }

    async Task EnsureOkAsync(string cmd, CancellationToken ct)
    {
        var lines = await CommandLinesAsync(cmd, ct).ConfigureAwait(false);
        var rprt = lines.LastOrDefault(l => l.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase));
        if (rprt is null)
            throw new InvalidOperationException($"Hamlib {cmd} got no RPRT response.");
        if (TryParseRprtCode(rprt, out int code) && code != 0)
            throw new InvalidOperationException($"Hamlib {cmd} failed: {rprt}");
    }

    async Task<List<string>> CommandLinesAsync(string cmd, CancellationToken ct)
    {
        if (!_connected || _stream is null)
            throw new InvalidOperationException("Hamlib not connected.");

        // Extended response protocol ("+" prefix) guarantees an RPRT line for every command.
        string wireCmd = cmd.StartsWith('+') ? cmd : "+" + cmd;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_connected || _stream is null)
                throw new InvalidOperationException("Hamlib not connected.");

            byte[] payload = Encoding.ASCII.GetBytes(wireCmd + "\n");
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            return await ReadUntilRprtAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            _connected = false;
            throw;
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

            if (n <= 0)
            {
                _connected = false;
                throw new IOException("Hamlib rigctld closed the connection.");
            }
            _readBuf.Append(Encoding.ASCII.GetString(buf, 0, n));
        }

        if (_readBuf.Length > 0)
        {
            foreach (var extra in _readBuf.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                lines.Add(extra.Trim('\r', ' '));
            _readBuf.Clear();
        }

        return lines;
    }

    static bool TryParseRprtCode(string line, out int code)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
            return true;
        code = 0;
        return false;
    }

    static bool HasErrorRprt(List<string> lines)
    {
        var rprt = lines.LastOrDefault(l => l.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase));
        return rprt is not null && TryParseRprtCode(rprt, out int code) && code != 0;
    }

    static bool IsMetaOrEchoLine(string line, string commandEchoPrefix) =>
        line.StartsWith("RPRT", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith(commandEchoPrefix + ":", StringComparison.OrdinalIgnoreCase);

    static string StripFieldPrefix(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? line[prefix.Length..].Trim()
            : line.Trim();

    static string StripAnyKeyPrefix(string line)
    {
        int colon = line.IndexOf(':');
        return colon >= 0 ? line[(colon + 1)..].Trim() : line.Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
