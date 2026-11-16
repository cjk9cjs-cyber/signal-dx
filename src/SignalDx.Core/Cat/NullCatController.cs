namespace SignalDx.Cat;

public sealed class NullCatController : ICatController
{
    public string Name => "None";
    public bool IsConnected => false;
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task<long?> GetFrequencyHzAsync(CancellationToken ct = default) => Task.FromResult<long?>(null);
    public Task SetFrequencyHzAsync(long hz, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetPttAsync(bool on, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetModeUsbAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetRfPowerAsync(double level01, CancellationToken ct = default) => Task.CompletedTask;
    public Task<double?> GetRfPowerAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
    public Task<double?> GetAlcAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
    public Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult(new RigSnapshot());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
