using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SignalDx.Cat;

/// <summary>
/// Ham Radio Deluxe Rig Control over TCP (default localhost:7809).
/// Speaks the WSJT-X/JTDX HRD protocol (binary UTF-16 frames, with text+\r fallback).
/// </summary>
public sealed class HrdCatController : ICatController
{
    public const int ModelId = 9999;
    public const int DefaultPort = 7809;

    readonly string _host;
    readonly int _port;
    readonly SemaphoreSlim _gate = new(1, 1);

    TcpClient? _client;
    NetworkStream? _stream;
    bool _useTextProtocol;
    int _radioId = 1;
    string? _pttButton;
    string? _info;

    public HrdCatController(string host, int port)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port <= 0 ? DefaultPort : port;
    }

    public string Name => $"HRD {_host}:{_port}";
    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _client = new TcpClient { NoDelay = true };
        using var reg = ct.Register(() => { try { _client.Close(); } catch { /* ignore */ } });
        await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        _stream = _client.GetStream();

        // Prefer binary (v5/v6) protocol; fall back to older text+\r.
        _useTextProtocol = false;
        try
        {
            await SendRawAsync("get context", prependContext: false, ct).ConfigureAwait(false);
        }
        catch
        {
            await DisconnectAsync().ConfigureAwait(false);
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            _stream = _client.GetStream();
            _useTextProtocol = true;
            await SendRawAsync("get context", prependContext: false, ct).ConfigureAwait(false);
        }

        string radios = await SendRawAsync("get radios", prependContext: false, ct).ConfigureAwait(false);
        var first = radios.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
        {
            var parts = first.Split(':', 2);
            if (int.TryParse(parts[0], out int id))
                _radioId = id;
        }

        string radio = await SendRawAsync("get radio", prependContext: false, ct).ConfigureAwait(false);
        string version = "";
        try { version = await SendRawAsync("get version", prependContext: false, ct).ConfigureAwait(false); }
        catch { /* optional */ }

        try
        {
            string buttons = await SendRawAsync("get buttons", prependContext: true, ct).ConfigureAwait(false);
            _pttButton = buttons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(b => b.Replace(' ', '~'))
                .FirstOrDefault(b => Regex.IsMatch(b, @"^(TX|TX~Alt|TX~Data)$", RegexOptions.IgnoreCase));
        }
        catch
        {
            _pttButton = "TX";
        }

        _info = $"HRD {radio} {version}".Trim();
    }

    public Task DisconnectAsync()
    {
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _client?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    /// <summary>Last raw HRD frequency reply (for diagnostics).</summary>
    public string? LastFrequencyRaw { get; private set; }

    public async Task<long?> GetFrequencyHzAsync(CancellationToken ct = default)
    {
        // Prefer VFO A from "get frequencies" (e.g. "7074000-21079000"); fall back to "get frequency".
        string raw;
        try
        {
            raw = await SendRawAsync("get frequencies", prependContext: true, ct).ConfigureAwait(false);
            LastFrequencyRaw = raw;
            long? fromPair = ParseFrequencyHz(raw);
            if (fromPair is > 0)
                return fromPair;
        }
        catch
        {
            // optional command on older HRD
        }

        raw = await SendRawAsync("get frequency", prependContext: true, ct).ConfigureAwait(false);
        LastFrequencyRaw = raw;
        return ParseFrequencyHz(raw);
    }

    /// <summary>
    /// Parse HRD frequency replies: Hz ("14074000"), kHz ("14074"), MHz ("14.074"),
    /// or VFO A-B pairs ("7074000-21079000").
    /// </summary>
    public static long? ParseFrequencyHz(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Drop HRD context prefixes like "[367] " so the radio id is not parsed as MHz.
        string cleaned = Regex.Replace(raw.Trim(), @"^\[\d+\]\s*", "");

        // VFO A-B form used by "get frequencies"
        var pair = Regex.Match(cleaned, @"(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)");
        if (pair.Success)
            return ScaleToHz(pair.Groups[1].Value);

        // Strip thousands separators: "7,074,000" or "14.074.000" (EU)
        if (cleaned.Count(c => c == '.') >= 2 && !cleaned.Contains(','))
        {
            string digits = Regex.Replace(cleaned, @"[^\d]", "");
            if (long.TryParse(digits, out long eu) && eu > 0)
                return eu;
        }

        var m = Regex.Match(cleaned, @"(\d+(?:[.,]\d+)?)");
        if (!m.Success) return null;
        string token = m.Groups[1].Value.Replace(',', '.');
        return ScaleToHz(token);
    }

    static long? ScaleToHz(string token)
    {
        if (!double.TryParse(token,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double v))
            return null;

        long hz;
        if (v >= 1_000_000) // already Hz
            hz = (long)Math.Round(v);
        else if (v >= 1_000) // kHz
            hz = (long)Math.Round(v * 1_000d);
        else // MHz
            hz = (long)Math.Round(v * 1_000_000d);

        return hz > 0 ? hz : null;
    }

    public async Task SetFrequencyHzAsync(long hz, CancellationToken ct = default)
    {
        string r = await SendRawAsync($"set frequency-hz {hz}", prependContext: true, ct).ConfigureAwait(false);
        if (!r.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"HRD set frequency failed: {r}");
    }

    public async Task SetPttAsync(bool on, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_pttButton)) return;
        string r = await SendRawAsync(
            $"set button-select {_pttButton} {(on ? "1" : "0")}",
            prependContext: true,
            ct).ConfigureAwait(false);
        if (!r.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"HRD PTT failed: {r}");
    }

    public Task SetModeUsbAsync(CancellationToken ct = default) =>
        SetModeAsync("USB", 0, ct);

    public async Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default)
    {
        // Best-effort: many HRD layouts expose a Mode dropdown.
        try
        {
            string dropdowns = await SendRawAsync("get dropdowns", prependContext: true, ct).ConfigureAwait(false);
            string? modeDd = dropdowns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(d => d.Contains("Mode", StringComparison.OrdinalIgnoreCase));
            if (modeDd is null) return;

            string list = await SendRawAsync($"get dropdown-list {{{modeDd}}}", prependContext: true, ct)
                .ConfigureAwait(false);
            var items = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            int idx = items.FindIndex(i => i.Equals(mode, StringComparison.OrdinalIgnoreCase)
                                           || i.StartsWith(mode, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;

            string item = items[idx].Replace(' ', '~');
            string dd = modeDd.Replace(' ', '~');
            await SendRawAsync($"set dropdown {dd} {item} {idx}", prependContext: true, ct).ConfigureAwait(false);
        }
        catch
        {
            // Mode mapping varies widely by radio layout — ignore.
        }
    }

    public async Task SetRfPowerAsync(double level01, CancellationToken ct = default)
    {
        await SetSliderByMatchAsync(level01, name =>
            name.Contains("RF~Power", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Power", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("Power", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("ALC", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Mic", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("USB", StringComparison.OrdinalIgnoreCase)), ct).ConfigureAwait(false);
    }

    public async Task SetAudioDriveAsync(double level01, CancellationToken ct = default)
    {
        // Prefer USB/DATA/MIC gain sliders that actually move radio ALC for digi audio.
        await SetSliderByMatchAsync(level01, name =>
            name.Contains("USB", StringComparison.OrdinalIgnoreCase)
            || name.Contains("DATA", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MIC", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mod", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Drive", StringComparison.OrdinalIgnoreCase), ct).ConfigureAwait(false);
    }

    async Task SetSliderByMatchAsync(double level01, Func<string, bool> match, CancellationToken ct)
    {
        try
        {
            string sliders = await SendRawAsync("get sliders", prependContext: true, ct).ConfigureAwait(false);
            string? name = sliders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Replace(' ', '~'))
                .FirstOrDefault(match);
            if (name is null) return;
            int pct = (int)Math.Round(Math.Clamp(level01, 0, 1) * 100);
            await SendRawAsync($"set slider {name} {pct}", prependContext: true, ct).ConfigureAwait(false);
        }
        catch
        {
            // optional
        }
    }

    public async Task<double?> GetRfPowerAsync(CancellationToken ct = default) =>
        await GetSliderLevel01Async(name =>
            name.Contains("RF~Power", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Power", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("Power", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("ALC", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Mic", StringComparison.OrdinalIgnoreCase)), ct).ConfigureAwait(false);

    public async Task<double?> GetAlcAsync(CancellationToken ct = default) =>
        await GetSliderLevel01Async(name =>
            name.Contains("ALC", StringComparison.OrdinalIgnoreCase), ct).ConfigureAwait(false);

    async Task<double?> GetSliderLevel01Async(Func<string, bool> match, CancellationToken ct)
    {
        try
        {
            string sliders = await SendRawAsync("get sliders", prependContext: true, ct).ConfigureAwait(false);
            string? name = sliders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Replace(' ', '~'))
                .FirstOrDefault(match);
            if (name is null) return null;

            string r = await SendRawAsync($"get slider {name}", prependContext: true, ct).ConfigureAwait(false);
            // Replies are often a bare integer 0–100, sometimes with other text.
            foreach (var part in r.Split([' ', '\r', '\n', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int pct) && pct is >= 0 and <= 100)
                    return pct / 100.0;
                if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v)
                    && v is >= 0 and <= 1.0)
                    return v;
            }
        }
        catch
        {
            // optional
        }
        return null;
    }

    public async Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        long? hz = null;
        bool? ptt = null;
        double? rf = null;
        double? alc = null;
        try { hz = await GetFrequencyHzAsync(ct).ConfigureAwait(false); } catch { /* keep going */ }
        try
        {
            if (!string.IsNullOrWhiteSpace(_pttButton))
            {
                string r = await SendRawAsync($"get button-select {_pttButton}", prependContext: true, ct)
                    .ConfigureAwait(false);
                ptt = r.Contains('1');
            }
        }
        catch { /* optional */ }
        try { rf = await GetRfPowerAsync(ct).ConfigureAwait(false); } catch { /* optional */ }
        try { alc = await GetAlcAsync(ct).ConfigureAwait(false); } catch { /* optional */ }

        return new RigSnapshot
        {
            FrequencyHz = hz,
            Mode = "USB",
            Ptt = ptt,
            RigInfo = _info,
            RfPower = rf,
            Alc = alc
        };
    }

    async Task<string> SendRawAsync(string cmd, bool prependContext, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("HRD not connected. Is Rig Control running?");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string full = prependContext ? $"[{_radioId}] {cmd}" : cmd;
            if (_useTextProtocol)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(full + "\r");
                await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
                return await ReadTextReplyAsync(ct).ConfigureAwait(false);
            }

            byte[] frame = BuildBinaryFrame(full);
            await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            return await ReadBinaryReplyAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    static byte[] BuildBinaryFrame(string payload)
    {
        // HRDMessage: size(u32) magic1(i32) magic2(i32) checksum(i32) utf16le payload + NUL
        byte[] text = Encoding.Unicode.GetBytes(payload + "\0");
        int size = 16 + text.Length;
        var buf = new byte[size];
        BinaryPrimitivesWriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)size);
        BinaryPrimitivesWriteInt32LittleEndian(buf.AsSpan(4, 4), unchecked((int)0x1234ABCD));
        BinaryPrimitivesWriteInt32LittleEndian(buf.AsSpan(8, 4), unchecked((int)0xABCD1234));
        BinaryPrimitivesWriteInt32LittleEndian(buf.AsSpan(12, 4), 0);
        Buffer.BlockCopy(text, 0, buf, 16, text.Length);
        return buf;
    }

    async Task<string> ReadBinaryReplyAsync(CancellationToken ct)
    {
        if (_stream is null) return "";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(3000);

        var ms = new MemoryStream();
        var temp = new byte[4096];
        // Read at least the 16-byte header
        while (ms.Length < 16)
        {
            int n = await _stream.ReadAsync(temp.AsMemory(0, temp.Length), cts.Token).ConfigureAwait(false);
            if (n <= 0) break;
            ms.Write(temp, 0, n);
        }

        if (ms.Length < 16)
            throw new InvalidOperationException("HRD returned an empty/short reply.");

        byte[] all = ms.ToArray();
        uint declared = BinaryPrimitivesReadUInt32LittleEndian(all.AsSpan(0, 4));
        while (all.Length < declared)
        {
            int n = await _stream.ReadAsync(temp.AsMemory(0, temp.Length), cts.Token).ConfigureAwait(false);
            if (n <= 0) break;
            var merged = new byte[all.Length + n];
            Buffer.BlockCopy(all, 0, merged, 0, all.Length);
            Buffer.BlockCopy(temp, 0, merged, all.Length, n);
            all = merged;
        }

        int payloadBytes = Math.Max(0, all.Length - 16);
        if (payloadBytes <= 0) return "";
        string s = Encoding.Unicode.GetString(all, 16, payloadBytes);
        return s.TrimEnd('\0').Trim();
    }

    async Task<string> ReadTextReplyAsync(CancellationToken ct)
    {
        if (_stream is null) return "";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(3000);
        var ms = new MemoryStream();
        var temp = new byte[1024];
        // HRD text replies are typically short; read until timeout after first bytes.
        int n = await _stream.ReadAsync(temp.AsMemory(0, temp.Length), cts.Token).ConfigureAwait(false);
        if (n > 0) ms.Write(temp, 0, n);
        while (_stream.DataAvailable)
        {
            n = await _stream.ReadAsync(temp.AsMemory(0, temp.Length), cts.Token).ConfigureAwait(false);
            if (n <= 0) break;
            ms.Write(temp, 0, n);
        }
        return Encoding.ASCII.GetString(ms.ToArray()).Trim();
    }

    static void BinaryPrimitivesWriteUInt32LittleEndian(Span<byte> d, uint v)
    {
        d[0] = (byte)v; d[1] = (byte)(v >> 8); d[2] = (byte)(v >> 16); d[3] = (byte)(v >> 24);
    }

    static void BinaryPrimitivesWriteInt32LittleEndian(Span<byte> d, int v) =>
        BinaryPrimitivesWriteUInt32LittleEndian(d, unchecked((uint)v));

    static uint BinaryPrimitivesReadUInt32LittleEndian(ReadOnlySpan<byte> d) =>
        (uint)(d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24));

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
