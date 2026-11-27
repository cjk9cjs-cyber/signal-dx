using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class ExternalLogRouter : IDisposable
{
    readonly ClubLogClient _clubLog = new();
    readonly QrzLogClient _qrz = new();
    readonly LotwClient _lotw = new();
    readonly PskReporterClient _psk = new();
    readonly WsprNetClient _wsprNet = new();
    WsjtxUdpLogger? _udp;
    WsjtxUdpReceiver? _replyRx;
    int _replyPort;

    public event Action<string>? StatusChanged;
    public event Action<WsjtxReply>? ReplyReceived;

    public ExternalLogRouter()
    {
        _psk.StatusChanged += s => StatusChanged?.Invoke(s);
        _wsprNet.StatusChanged += s => StatusChanged?.Invoke(s);
    }

    public void Configure(StationSettings settings)
    {
        _udp?.Dispose();
        _udp = null;
        _psk.Configure(settings);
        _wsprNet.Configure(settings);
        ConfigureReplyListener(settings);
        if (!settings.UdpLogEnabled) return;
        try
        {
            _udp = new WsjtxUdpLogger(settings.UdpLogHost, settings.UdpLogPort);
            _udp.SendHeartbeat();
            StatusChanged?.Invoke($"UDP log → {settings.UdpLogHost}:{settings.UdpLogPort}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"UDP log setup failed: {ex.Message}");
        }
    }

    void ConfigureReplyListener(StationSettings settings)
    {
        bool wanted = settings.UdpLogEnabled && settings.UdpReplyEnabled;
        if (!wanted)
        {
            _replyRx?.Dispose();
            _replyRx = null;
            _replyPort = 0;
            return;
        }
        if (_replyRx is not null && _replyPort == settings.UdpReplyPort)
            return;

        _replyRx?.Dispose();
        _replyRx = null;
        try
        {
            var rx = new WsjtxUdpReceiver(settings.UdpReplyPort);
            rx.ReplyReceived += r => ReplyReceived?.Invoke(r);
            rx.Faulted += s => StatusChanged?.Invoke($"UDP reply listener stopped: {s}");
            _replyRx = rx;
            _replyPort = settings.UdpReplyPort;
            StatusChanged?.Invoke($"UDP replies ← 127.0.0.1:{settings.UdpReplyPort}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"UDP reply listener failed on port {settings.UdpReplyPort}: {ex.Message}");
        }
    }

    public void ReportDecodes(IEnumerable<DecodeHit> hits, StationSettings settings)
    {
        if (_udp is not null && settings.UdpLogEnabled && settings.UdpStatusDecodeEnabled)
        {
            foreach (var hit in hits)
            {
                try { _udp.SendDecode(hit); }
                catch { /* keep decoding if a listener is gone */ }
            }
        }
        _psk.Enqueue(hits, settings);
        _wsprNet.Enqueue(hits, settings);
    }

    WsjtxStatus? _lastStatus;
    DateTime _lastStatusUtc;

    public void ReportStatus(WsjtxStatus status, StationSettings settings)
    {
        if (_udp is null || !settings.UdpLogEnabled || !settings.UdpStatusDecodeEnabled) return;
        var now = DateTime.UtcNow;
        if (status == _lastStatus && (now - _lastStatusUtc).TotalSeconds < 5) return;
        try
        {
            _udp.UpdateEndpoint(settings.UdpLogHost, settings.UdpLogPort);
            _udp.SendStatus(status);
            _lastStatus = status;
            _lastStatusUtc = now;
        }
        catch { /* listener may be offline */ }
    }

    public void Heartbeat(StationSettings settings)
    {
        if (!settings.UdpLogEnabled) return;
        try
        {
            if (_udp is null)
                Configure(settings);
            else
            {
                _udp.UpdateEndpoint(settings.UdpLogHost, settings.UdpLogPort);
                _udp.SendHeartbeat();
            }
        }
        catch
        {
            // ignore heartbeat failures
        }
    }

    public async Task PublishAsync(QsoEntry qso, StationSettings settings, CancellationToken ct = default)
    {
        var notes = new List<string>();

        if (settings.AdifAppendEnabled)
        {
            try
            {
                string path = string.IsNullOrWhiteSpace(settings.AdifPath)
                    ? AdifExporter.DefaultPath
                    : settings.AdifPath;
                AdifExporter.Append(path, qso);
                notes.Add("ADIF");
            }
            catch (Exception ex)
            {
                notes.Add("ADIF fail: " + ex.Message);
            }
        }

        if (_udp is not null && settings.UdpLogEnabled)
        {
            try
            {
                _udp.UpdateEndpoint(settings.UdpLogHost, settings.UdpLogPort);
                _udp.SendQsoLogged(qso);
                _udp.SendLoggedAdif(qso);
                notes.Add("UDP");
            }
            catch (Exception ex)
            {
                notes.Add("UDP fail: " + ex.Message);
            }
        }

        if (settings.ClubLogEnabled)
        {
            var (ok, msg) = await _clubLog.UploadAsync(
                qso, settings.ClubLogEmail, settings.MyCall, settings.ClubLogPassword, ct)
                .ConfigureAwait(false);
            notes.Add(ok ? "ClubLog" : msg);
        }

        if (settings.QrzEnabled)
        {
            var (ok, msg) = await _qrz.UploadAsync(qso, settings.QrzApiKey, ct).ConfigureAwait(false);
            notes.Add(ok ? "QRZ" : msg);
        }

        if (settings.LotwEnabled)
        {
            var (ok, msg) = await _lotw.UploadAsync(
                qso, settings.LotwTqslPath, settings.LotwStationLocation, ct).ConfigureAwait(false);
            notes.Add(ok ? "LoTW" : msg);
        }

        StatusChanged?.Invoke("External log: " + string.Join(", ", notes));
    }

    public void Dispose()
    {
        _udp?.Dispose();
        _replyRx?.Dispose();
        _ = _psk.DisposeAsync().AsTask();
        _ = _wsprNet.DisposeAsync().AsTask();
    }
}
