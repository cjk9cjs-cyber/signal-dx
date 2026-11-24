using System.Net;
using System.Net.Sockets;
using System.Text;
using SignalDx.Cat;
using Xunit;

namespace SignalDx.Tests;

public sealed class HamlibTcpCatControllerTests
{
    sealed class MockRigctld : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _cts = new();
        readonly Task _loop;

        public int Port { get; }
        public long FrequencyHz { get; set; } = 14_074_000;
        public string Mode { get; set; } = "PKTUSB";
        public int Passband { get; set; } = 3000;
        public List<string> ReceivedCommands { get; } = [];

        public MockRigctld()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        async Task HandleClientAsync(TcpClient client)
        {
            using var _ = client;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };

            while (!_cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync();
                if (line is null) break;
                ReceivedCommands.Add(line);

                string cmd = line.Trim();
                bool extended = cmd.StartsWith('+');
                string body = extended ? cmd[1..].Trim() : cmd;

                string reply = body switch
                {
                    "f" => extended
                        ? $"get_freq:\nFrequency: {FrequencyHz}\nRPRT 0"
                        : $"{FrequencyHz}",
                    "m" => extended
                        ? $"get_mode:\nMode: {Mode}\nPassband: {Passband}\nRPRT 0"
                        : $"{Mode}\n{Passband}",
                    "t" => extended
                        ? "get_ptt:\nPTT: 0\nRPRT 0"
                        : "0",
                    "v" => extended
                        ? "get_vfo:\nVFO: VFOA\nRPRT 0"
                        : "VFOA",
                    "_" => extended
                        ? "get_info:\nInfo: ID0800\nRPRT 0"
                        : "ID0800",
                    "l RFPOWER" => extended
                        ? "get_level: RFPOWER\n0.5\nRPRT 0"
                        : "0.5",
                    "l ALC" => extended
                        ? "get_level: ALC\n0\nRPRT 0"
                        : "0",
                    _ when body.StartsWith("F ") => HandleSetFreq(body, extended),
                    _ when body.StartsWith("M ") => HandleSetMode(body, extended),
                    _ => "RPRT -1"
                };

                await writer.WriteLineAsync(reply);
            }
        }

        string HandleSetFreq(string body, bool extended)
        {
            if (long.TryParse(body[2..].Trim(), out long hz))
                FrequencyHz = hz;
            return extended ? $"set_freq: {FrequencyHz}\nRPRT 0" : "RPRT 0";
        }

        string HandleSetMode(string body, bool extended)
        {
            var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) Mode = parts[1];
            if (parts.Length >= 3 && int.TryParse(parts[2], out int pb) && pb > 0)
                Passband = pb;
            return extended ? $"set_mode: {Mode} {(parts.Length >= 3 ? parts[2] : "-1")}\nRPRT 0" : "RPRT 0";
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task Connect_StaysConnected_AndReadsSnapshotFast_UsingExtendedProtocol()
    {
        await using var rig = new MockRigctld();
        await using var ctl = new HamlibTcpCatController("127.0.0.1", rig.Port);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await ctl.ConnectAsync();
        Assert.True(ctl.IsConnected);

        var snap = await ctl.GetSnapshotAsync();
        sw.Stop();

        Assert.True(ctl.IsConnected);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Expected fast extended-protocol poll, took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(14_074_000, snap.FrequencyHz);
        Assert.Equal("PKTUSB", snap.Mode);
        Assert.Equal(3000, snap.PassbandHz);
        Assert.Equal(false, snap.Ptt);
        Assert.Equal("VFOA", snap.Vfo);
        Assert.Equal("ID0800", snap.RigInfo);
        Assert.Equal(0.5, snap.RfPower);
        Assert.Equal(0.0, snap.Alc);
        Assert.All(rig.ReceivedCommands, c => Assert.StartsWith("+", c));
    }

    [Fact]
    public async Task SetFrequency_AndSetMode_UpdateRigWithoutClobberingPassband()
    {
        await using var rig = new MockRigctld();
        await using var ctl = new HamlibTcpCatController("127.0.0.1", rig.Port);

        await ctl.ConnectAsync();
        await ctl.SetFrequencyHzAsync(7_074_000);
        await ctl.SetModeAsync("DATA-U", passbandHz: 0);

        Assert.True(ctl.IsConnected);
        Assert.Equal(7_074_000, rig.FrequencyHz);
        Assert.Equal(3000, rig.Passband);
        Assert.Contains("+M DATA-U -1", rig.ReceivedCommands);
    }
}
