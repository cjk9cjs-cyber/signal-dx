using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SignalDx.Models;

namespace SignalDx.Logging;

/// <summary>
/// Queues decoded spots and posts them to PSK Reporter (IPFIX/UDP → report.pskreporter.info:4739).
/// Protocol matches the FreeDV / WSJT-X simplified IPFIX layout documented at pskreporter.info/pskdev.html.
/// </summary>
public sealed class PskReporterClient : IAsyncDisposable
{
    const string Host = "report.pskreporter.info";
    const int Port = 4739;
    const string SoftwareId = "SignalDX";
    static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(120);
    static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(300);

    // Receiver template (link 0x9992): call, grid, software
    static readonly byte[] RxFormatHeader =
    [
        0x00, 0x03, 0x00, 0x24, 0x99, 0x92, 0x00, 0x03, 0x00, 0x00,
        0x80, 0x02, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x04, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x08, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,
        0x00, 0x00
    ];

    // Sender template (link 0x9993): call, freq(4), snr(1), mode, infoSource(1), time(4)
    static readonly byte[] TxFormatHeader =
    [
        0x00, 0x02, 0x00, 0x34, 0x99, 0x93, 0x00, 0x06,
        0x80, 0x01, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x05, 0x00, 0x04, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x0A, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F,
        0x80, 0x0B, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F,
        0x00, 0x96, 0x00, 0x04
    ];

    readonly object _gate = new();
    readonly List<Spot> _pending = [];
    readonly ConcurrentDictionary<string, long> _recent = new(StringComparer.OrdinalIgnoreCase);
    readonly uint _observationId = (uint)Random.Shared.Next();
    readonly CancellationTokenSource _cts = new();
    readonly Task _loop;

    uint _sequence;
    string _rxCall = "";
    string _rxGrid = "";
    bool _enabled;
    int _sentTotal;

    public event Action<string>? StatusChanged;

    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public int SentTotal => _sentTotal;

    public PskReporterClient()
    {
        _loop = Task.Run(SendLoopAsync);
    }

    public void Configure(StationSettings settings)
    {
        _enabled = settings.PskReporterEnabled;
        _rxCall = (settings.MyCall ?? "").Trim().ToUpperInvariant();
        _rxGrid = (settings.MyGrid ?? "").Trim().ToUpperInvariant();
        if (_enabled && !string.IsNullOrWhiteSpace(_rxCall))
            StatusChanged?.Invoke($"PSK Reporter on ({_rxCall} {_rxGrid})");
        else if (!_enabled)
            StatusChanged?.Invoke("PSK Reporter off");
    }

    public void Enqueue(IEnumerable<DecodeHit> hits, StationSettings settings)
    {
        if (!settings.PskReporterEnabled) return;
        string my = (settings.MyCall ?? "").Trim();
        if (string.IsNullOrWhiteSpace(my)) return;

        _rxCall = my.ToUpperInvariant();
        _rxGrid = (settings.MyGrid ?? "").Trim().ToUpperInvariant();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int added = 0;
        lock (_gate)
        {
            foreach (var hit in hits)
            {
                string mode = NormalizeMode(
                    string.IsNullOrWhiteSpace(hit.Mode) ? settings.Mode : hit.Mode);
                bool wspr = mode == "WSPR";

                string? call = hit.DxCall;
                if (wspr)
                {
                    // Prefer explicit DxCall; else parse "CALL GRID DBM" (not FT8 QSO layout).
                    if (string.IsNullOrWhiteSpace(call) || LooksLikeGrid(call))
                        call = TryParseWsprCall(hit.Message);
                }
                else if (string.IsNullOrWhiteSpace(call))
                {
                    var parsed = Ft8.Ft8MessageBuilder.Parse(hit.Message);
                    call = parsed.DxCall;
                }

                if (string.IsNullOrWhiteSpace(call)) continue;
                call = call.Trim().ToUpperInvariant();
                if (call.Equals(my, StringComparison.OrdinalIgnoreCase)) continue;
                if (call is "CQ" or "QRZ" or "DE" or "DX") continue;
                if (LooksLikeGrid(call)) continue;

                // Dedup same call on same ~kHz for a few minutes
                long rfHz = (long)Math.Round(settings.DialMhz * 1_000_000d) + hit.Hz;
                string key = $"{call}:{mode}:{rfHz / 1000}";
                if (_recent.TryGetValue(key, out long seen) && now - seen < CacheTtl.TotalSeconds)
                    continue;
                _recent[key] = now;

                int snr = (int)Math.Clamp(Math.Round(hit.SnrDb), sbyte.MinValue, sbyte.MaxValue);
                _pending.Add(new Spot(
                    call,
                    (uint)Math.Clamp(rfHz, 0, uint.MaxValue),
                    (sbyte)snr,
                    mode,
                    (uint)now));
                added++;
            }
        }

        if (added > 0)
            PruneRecent(now);
    }

    /// <summary>ADIF MODE/SUBMODE values PSK Reporter accepts.</summary>
    static string NormalizeMode(string? mode)
    {
        string m = (mode ?? "").Trim().ToUpperInvariant();
        if (m is "WSPR2" or "WSPR-2" or "WSPR") return "WSPR";
        if (string.IsNullOrWhiteSpace(m)) return "FT8";
        return m;
    }

    static string? TryParseWsprCall(string? message)
    {
        var parts = (message ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        if (!int.TryParse(parts[^1], out _)) return null;
        if (!LooksLikeGrid(parts[^2])) return null;
        string call = string.Join(" ", parts[..^2]).Trim().ToUpperInvariant();
        return call.Length >= 3 ? call : null;
    }

    static bool LooksLikeGrid(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length is not (4 or 6)) return false;
        return char.IsLetter(s[0]) && char.IsLetter(s[1])
               && char.IsDigit(s[2]) && char.IsDigit(s[3]);
    }

    void PruneRecent(long now)
    {
        foreach (var kv in _recent)
        {
            if (now - kv.Value >= CacheTtl.TotalSeconds)
                _recent.TryRemove(kv.Key, out _);
        }
    }

    async Task SendLoopAsync()
    {
        using var timer = new PeriodicTimer(SendInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                try { await FlushAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("PSK Reporter: " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public async Task FlushAsync()
    {
        List<Spot> batch;
        string rxCall;
        string rxGrid;
        bool enabled;
        lock (_gate)
        {
            enabled = _enabled;
            rxCall = _rxCall;
            rxGrid = _rxGrid;
            if (!enabled || _pending.Count == 0 || string.IsNullOrWhiteSpace(rxCall))
                return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        byte[] packet = BuildPacket(rxCall, rxGrid, batch, ref _sequence, _observationId);
        using var udp = new UdpClient();
        await udp.SendAsync(packet, packet.Length, Host, Port).ConfigureAwait(false);
        Interlocked.Add(ref _sentTotal, batch.Count);
        StatusChanged?.Invoke($"PSK Reporter: sent {batch.Count} spot(s)");
    }

    public static byte[] BuildPacket(
        string rxCall, string rxGrid, IReadOnlyList<Spot> spots, ref uint sequence, uint observationId)
    {
        var ms = new MemoryStream();
        // IPFIX header placeholder
        ms.Write(new byte[16]);

        ms.Write(RxFormatHeader);
        if (spots.Count > 0)
            ms.Write(TxFormatHeader);

        WriteReceiverSet(ms, rxCall, rxGrid);
        if (spots.Count > 0)
            WriteSenderSet(ms, spots);

        byte[] packet = ms.ToArray();
        // Version 10
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), ++sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), observationId);
        return packet;
    }

    static void WriteReceiverSet(MemoryStream ms, string call, string grid)
    {
        int start = (int)ms.Length;
        ms.WriteByte(0x99);
        ms.WriteByte(0x92);
        ms.Write(new byte[2]); // length placeholder
        WriteUtf(ms, call);
        WriteUtf(ms, grid);
        WriteUtf(ms, SoftwareId);
        Pad4(ms, start);
        int len = (int)ms.Length - start;
        Span<byte> lenBuf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)len);
        ms.Position = start + 2;
        ms.Write(lenBuf);
        ms.Position = ms.Length;
    }

    static void WriteSenderSet(MemoryStream ms, IReadOnlyList<Spot> spots)
    {
        int start = (int)ms.Length;
        ms.WriteByte(0x99);
        ms.WriteByte(0x93);
        ms.Write(new byte[2]); // length placeholder
        Span<byte> freq = stackalloc byte[4];
        Span<byte> time = stackalloc byte[4];
        foreach (var s in spots)
        {
            WriteUtf(ms, s.Call);
            BinaryPrimitives.WriteUInt32BigEndian(freq, s.FrequencyHz);
            ms.Write(freq);
            ms.WriteByte(unchecked((byte)s.Snr));
            WriteUtf(ms, s.Mode);
            ms.WriteByte(1); // automatically extracted
            BinaryPrimitives.WriteUInt32BigEndian(time, s.UnixSeconds);
            ms.Write(time);
        }
        Pad4(ms, start);
        int len = (int)ms.Length - start;
        Span<byte> lenBuf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)len);
        ms.Position = start + 2;
        ms.Write(lenBuf);
        ms.Position = ms.Length;
    }

    static void WriteUtf(MemoryStream ms, string value)
    {
        byte[] utf = Encoding.UTF8.GetBytes(value ?? "");
        if (utf.Length > 254)
            utf = utf.AsSpan(0, 254).ToArray();
        ms.WriteByte((byte)utf.Length);
        ms.Write(utf);
    }

    static void Pad4(MemoryStream ms, int start)
    {
        int len = (int)ms.Length - start;
        int pad = (4 - (len % 4)) % 4;
        for (int i = 0; i < pad; i++)
            ms.WriteByte(0);
    }

    public async ValueTask DisposeAsync()
    {
        try { await FlushAsync().ConfigureAwait(false); } catch { /* ignore */ }
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
        _cts.Dispose();
    }

    public readonly record struct Spot(string Call, uint FrequencyHz, sbyte Snr, string Mode, uint UnixSeconds);
}
