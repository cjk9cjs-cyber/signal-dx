using SignalDx.Cat;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class CatServicePttSafetyTests
{
    sealed class FakeCatController : ICatController
    {
        public List<string> Events { get; } = [];
        public string Name => "FakeCAT";
        public bool IsConnected { get; private set; }

        public Task ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            Events.Add("connect");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            Events.Add("disconnect");
            return Task.CompletedTask;
        }

        public Task<long?> GetFrequencyHzAsync(CancellationToken ct = default) => Task.FromResult<long?>(14_074_000);
        public Task SetFrequencyHzAsync(long hz, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetPttAsync(bool on, CancellationToken ct = default)
        {
            Events.Add(on ? "ptt:on" : "ptt:off");
            return Task.CompletedTask;
        }

        public Task SetModeUsbAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRfPowerAsync(double level01, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetAudioDriveAsync(double level01, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double?> GetRfPowerAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
        public Task<double?> GetAlcAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
        public Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default) => Task.FromResult(new RigSnapshot());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ConnectAndDispose_ForcePttOff_WhenCatPttEnabled()
    {
        var fake = new FakeCatController();
        await using var cat = new CatService(_ => fake);

        var settings = new StationSettings
        {
            CatBackend = CatBackend.HamRadioDeluxe,
            CatPttEnabled = true,
            CatPollEnabled = false
        };

        await cat.ApplySettingsAsync(settings);
        Assert.Equal(["connect", "ptt:off"], fake.Events);

        await cat.SetPttAsync(true, enabled: true);
        await cat.StopAsync(stopLauncher: false);

        Assert.Equal(["connect", "ptt:off", "ptt:on", "ptt:off", "disconnect"], fake.Events);
    }

    [Fact]
    public async Task ConnectAndDispose_DoNotTouchPtt_WhenCatPttDisabled()
    {
        var fake = new FakeCatController();
        await using var cat = new CatService(_ => fake);

        var settings = new StationSettings
        {
            CatBackend = CatBackend.HamRadioDeluxe,
            CatPttEnabled = false,
            CatPollEnabled = false
        };

        await cat.ApplySettingsAsync(settings);
        await cat.StopAsync(stopLauncher: false);

        Assert.Equal(["connect", "disconnect"], fake.Events);
    }
}
