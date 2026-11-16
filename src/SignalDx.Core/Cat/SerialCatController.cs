using System.IO.Ports;
using System.Text;

namespace SignalDx.Cat;

public enum SerialCatDialect
{
    Kenwood,
    Yaesu
}

/// <summary>
/// Basic serial CAT for Kenwood (FA/IF/TX/RX) and Yaesu (FA/ID/; TX1;/TX0;).
/// Works with many CAT-by-cable and USB-serial radios / interfaces.
/// </summary>
public sealed class SerialCatController : ICatController
{
    readonly string _portName;
    readonly int _baud;
    readonly SerialCatDialect _dialect;
    SerialPort? _port;
    readonly SemaphoreSlim _gate = new(1, 1);

    public SerialCatController(string portName, int baud, SerialCatDialect dialect)
    {
        _portName = portName;
        _baud = baud;
        _dialect = dialect;
    }

    public string Name => $"{_dialect} {_portName}@{_baud}";
    public bool IsConnected => _port?.IsOpen == true;

    public static IReadOnlyList<string> ListPorts() => SerialPort.GetPortNames().OrderBy(p => p).ToArray();

    public Task ConnectAsync(CancellationToken ct = default)
    {
        DisconnectAsync();
        _port = new SerialPort(_portName, _baud, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 800,
            WriteTimeout = 800,
            NewLine = ";",
            Encoding = Encoding.ASCII
        };
        _port.Open();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_port is null) return Task.CompletedTask;
        try { if (_port.IsOpen) _port.Close(); } catch { /* ignore */ }
        try { _port.Dispose(); } catch { /* ignore */ }
        _port = null;
        return Task.CompletedTask;
    }

    public async Task<long?> GetFrequencyHzAsync(CancellationToken ct = default)
    {
        string? r = await TransactAsync("FA;", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(r)) return null;
        // Kenwood/Yaesu: FA00014074000;
        string digits = new(r.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out long hz) ? hz : null;
    }

    public Task SetFrequencyHzAsync(long hz, CancellationToken ct = default)
    {
        string body = hz.ToString().PadLeft(11, '0');
        return TransactAsync($"FA{body};", ct);
    }

    public Task SetPttAsync(bool on, CancellationToken ct = default)
    {
        if (_dialect == SerialCatDialect.Yaesu)
            return TransactAsync(on ? "TX1;" : "TX0;", ct);
        return TransactAsync(on ? "TX;" : "RX;", ct);
    }

    public Task SetModeUsbAsync(CancellationToken ct = default) =>
        SetModeAsync("USB", 0, ct);

    public Task SetModeAsync(string mode, int passbandHz = 0, CancellationToken ct = default)
    {
        // Best-effort USB/LSB only for serial dialect.
        bool lsb = mode.Equals("LSB", StringComparison.OrdinalIgnoreCase);
        if (_dialect == SerialCatDialect.Yaesu)
            return TransactAsync(lsb ? "MD01;" : "MD02;", ct);
        return TransactAsync(lsb ? "MD1;" : "MD2;", ct);
    }

    public Task SetRfPowerAsync(double level01, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetAudioDriveAsync(double level01, CancellationToken ct = default) => Task.CompletedTask;
    public Task<double?> GetRfPowerAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
    public Task<double?> GetAlcAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);

    public async Task<RigSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        long? hz = null;
        try { hz = await GetFrequencyHzAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
        return new RigSnapshot { FrequencyHz = hz, Mode = "USB" };
    }


    async Task<string?> TransactAsync(string cmd, CancellationToken ct)
    {
        if (_port is null || !_port.IsOpen) throw new InvalidOperationException("CAT not connected.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _port.DiscardInBuffer();
            _port.Write(cmd);
            await Task.Delay(60, ct).ConfigureAwait(false);
            if (_port.BytesToRead <= 0)
                return null;
            return _port.ReadExisting();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
