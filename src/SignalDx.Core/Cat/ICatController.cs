namespace SignalDx.Cat;

public sealed class RigSnapshot
{
    public long? FrequencyHz { get; init; }
    public string? Mode { get; init; }
    public int? PassbandHz { get; init; }
    public bool? Ptt { get; init; }
    public string? Vfo { get; init; }
    public string? RigInfo { get; init; }
    /// <summary>Hamlib RFPOWER level 0.0–1.0 when available.</summary>
    public double? RfPower { get; init; }
    /// <summary>Hamlib ALC level (typically 0.0–1.0) when available.</summary>
    public double? Alc { get; init; }
}

public interface ICatController : IAsyncDisposable
{
    string Name { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<long?> GetFrequencyHzAsync(CancellationToken ct = default);
    Task SetFrequencyHzAsync(long hz, CancellationToken ct = default);
    Task SetPttAsync(bool on, CancellationToken ct = default);
    Task SetModeUsbAsync(CancellationToken ct = default);
    Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default);
    Task SetRfPowerAsync(double level01, CancellationToken ct = default);
    /// <summary>Set TX audio / mic / USB-DATA drive 0–1 for ALC tuning when the radio exposes it.</summary>
    Task SetAudioDriveAsync(double level01, CancellationToken ct = default);
    Task<double?> GetRfPowerAsync(CancellationToken ct = default);
    Task<double?> GetAlcAsync(CancellationToken ct = default);
    Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
