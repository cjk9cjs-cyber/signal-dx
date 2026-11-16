using SignalDx.Models;

namespace SignalDx.Logging;

public sealed class ExternalLogRouter : IDisposable
{
    readonly ClubLogClient _clubLog = new();
    readonly QrzLogClient _qrz = new();
    WsjtxUdpLogger? _udp;

    public event Action<string>? StatusChanged;

    public void Configure(StationSettings settings)
    {
        _udp?.Dispose();
        _udp = null;
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

        StatusChanged?.Invoke("External log: " + string.Join(", ", notes));
    }

    public void Dispose() => _udp?.Dispose();
}
