using SignalDx.Models;

namespace SignalDx.Cat;

public sealed class CatService : IAsyncDisposable
{
    ICatController _controller = new NullCatController();
    CancellationTokenSource? _pollCts;
    Task? _pollTask;
    readonly HamlibRigctldLauncher _launcher = new();

    public event Action<RigSnapshot>? SnapshotChanged;
    public event Action<long>? FrequencyChanged;
    public event Action<string>? StatusChanged;

    public bool IsConnected => _controller.IsConnected;
    public string BackendName => _controller.Name;
    public long? LastFrequencyHz { get; private set; }
    public RigSnapshot LastSnapshot { get; private set; } = new();
    public bool IsRigctldManaged => _launcher.IsRunning;
    public HamlibRigctldLauncher Launcher => _launcher;

    public async Task ApplySettingsAsync(StationSettings settings, CancellationToken ct = default)
    {
        await StopAsync(stopLauncher: false).ConfigureAwait(false);
        _controller = Create(settings);
        if (settings.CatBackend == CatBackend.None)
        {
            StatusChanged?.Invoke("CAT disabled");
            return;
        }

        try
        {
            await _controller.ConnectAsync(ct).ConfigureAwait(false);
            StatusChanged?.Invoke($"CAT connected: {_controller.Name}");

            if (settings.CatFollowDial)
                await SetDialMhzAsync(settings.DialMhz, ct).ConfigureAwait(false);

            string mode = string.IsNullOrWhiteSpace(settings.HamlibMode) ? "USB" : settings.HamlibMode;
            await _controller.SetModeAsync(mode, 0, ct).ConfigureAwait(false);

            if (settings.CatPollEnabled)
                StartPolling();

            var snap = await _controller.GetSnapshotAsync(ct).ConfigureAwait(false);
            PublishSnapshot(snap);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"CAT connect failed: {ex.Message}");
            await _controller.DisconnectAsync().ConfigureAwait(false);
            _controller = new NullCatController();
        }
    }

    public (bool Ok, string Message) StartRigctld(StationSettings settings)
    {
        var result = _launcher.Start(
            settings.HamlibModelId,
            string.IsNullOrWhiteSpace(settings.HamlibDevice) ? settings.CatComPort : settings.HamlibDevice,
            settings.CatBaud,
            settings.CatPort,
            string.IsNullOrWhiteSpace(settings.HamlibRigctldPath) ? null : settings.HamlibRigctldPath);
        StatusChanged?.Invoke(result.Message);
        return result;
    }

    public void StopRigctld()
    {
        _launcher.Stop();
        StatusChanged?.Invoke("rigctld stopped");
    }

    public async Task SetDialMhzAsync(double mhz, CancellationToken ct = default)
    {
        if (!_controller.IsConnected) return;
        long hz = (long)Math.Round(mhz * 1_000_000d);
        await _controller.SetFrequencyHzAsync(hz, ct).ConfigureAwait(false);
        LastFrequencyHz = hz;
        FrequencyChanged?.Invoke(hz);
    }

    public async Task SetModeAsync(string mode, CancellationToken ct = default)
    {
        if (!_controller.IsConnected) return;
        await _controller.SetModeAsync(mode, 0, ct).ConfigureAwait(false);
        StatusChanged?.Invoke($"CAT mode {mode}");
    }

    public async Task SetRfPowerAsync(double level01, CancellationToken ct = default)
    {
        if (!_controller.IsConnected) return;
        await _controller.SetRfPowerAsync(level01, ct).ConfigureAwait(false);
        StatusChanged?.Invoke($"CAT RFPOWER {Math.Clamp(level01, 0, 1):0.##}");
        LastSnapshot = new RigSnapshot
        {
            FrequencyHz = LastSnapshot.FrequencyHz,
            Mode = LastSnapshot.Mode,
            PassbandHz = LastSnapshot.PassbandHz,
            Ptt = LastSnapshot.Ptt,
            Vfo = LastSnapshot.Vfo,
            RigInfo = LastSnapshot.RigInfo,
            RfPower = Math.Clamp(level01, 0, 1),
            Alc = LastSnapshot.Alc
        };
        SnapshotChanged?.Invoke(LastSnapshot);
    }

    public async Task SetPttAsync(bool on, bool enabled, CancellationToken ct = default)
    {
        if (!enabled || !_controller.IsConnected) return;
        await _controller.SetPttAsync(on, ct).ConfigureAwait(false);
        StatusChanged?.Invoke(on ? "CAT PTT ON" : "CAT PTT OFF");
        LastSnapshot = new RigSnapshot
        {
            FrequencyHz = LastSnapshot.FrequencyHz,
            Mode = LastSnapshot.Mode,
            PassbandHz = LastSnapshot.PassbandHz,
            Ptt = on,
            Vfo = LastSnapshot.Vfo,
            RigInfo = LastSnapshot.RigInfo,
            RfPower = LastSnapshot.RfPower,
            Alc = LastSnapshot.Alc
        };
        SnapshotChanged?.Invoke(LastSnapshot);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!_controller.IsConnected) return;
        var snap = await _controller.GetSnapshotAsync(ct).ConfigureAwait(false);
        PublishSnapshot(snap);
    }

    public async Task StopAsync(bool stopLauncher = true)
    {
        if (_pollCts is not null)
        {
            _pollCts.Cancel();
            try { if (_pollTask is not null) await _pollTask.ConfigureAwait(false); } catch { /* ignore */ }
            _pollCts.Dispose();
            _pollCts = null;
            _pollTask = null;
        }

        await _controller.DisconnectAsync().ConfigureAwait(false);
        await _controller.DisposeAsync().ConfigureAwait(false);
        _controller = new NullCatController();

        if (stopLauncher)
            _launcher.Stop();
    }

    void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;
        _pollTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var snap = await _controller.GetSnapshotAsync(ct).ConfigureAwait(false);
                    PublishSnapshot(snap);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // keep polling
                }

                try { await Task.Delay(750, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    void PublishSnapshot(RigSnapshot snap)
    {
        LastSnapshot = snap;
        if (snap.FrequencyHz is long hz && hz != LastFrequencyHz)
        {
            LastFrequencyHz = hz;
            FrequencyChanged?.Invoke(hz);
        }
        SnapshotChanged?.Invoke(snap);
    }

    static ICatController Create(StationSettings s)
    {
        // Selecting the virtual HRD model forces the HRD backend.
        if (s.HamlibModelId == HrdCatController.ModelId)
            s.CatBackend = CatBackend.HamRadioDeluxe;

        string port = HamlibRigctldLauncher.ExtractPort(
            string.IsNullOrWhiteSpace(s.CatComPort) ? s.HamlibDevice : s.CatComPort);
        return s.CatBackend switch
        {
            CatBackend.HamlibTcp => new HamlibTcpCatController(s.CatHost, s.CatPort),
            CatBackend.SerialKenwood => new SerialCatController(port, s.CatBaud, SerialCatDialect.Kenwood),
            CatBackend.SerialYaesu => new SerialCatController(port, s.CatBaud, SerialCatDialect.Yaesu),
            CatBackend.HamRadioDeluxe => new HrdCatController(
                string.IsNullOrWhiteSpace(s.CatHost) ? "127.0.0.1" : s.CatHost,
                s.HrdPort > 0 ? s.HrdPort : HrdCatController.DefaultPort),
            _ => new NullCatController()
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(stopLauncher: true).ConfigureAwait(false);
    }
}
