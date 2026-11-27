using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SignalDx.Logging;

/// <summary>A companion app asked Signal DX to work a decode (WSJT-X "Reply" packet).</summary>
public sealed record WsjtxReply(string Id, uint TimeMs, int Snr, double Dt, uint Hz, string Mode, string Message);

/// <summary>
/// Listens for WSJT-X Reply packets from companion apps. Bound to loopback only, so only
/// software on this PC can make Signal DX transmit.
/// </summary>
public sealed class WsjtxUdpReceiver : IDisposable
{
    const uint TypeReply = 4;

    readonly UdpClient _udp;
    readonly CancellationTokenSource _cts = new();

    public event Action<WsjtxReply>? ReplyReceived;
    public event Action<string>? Faulted;

    public WsjtxUdpReceiver(int port)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        _ = ReceiveLoopAsync(_cts.Token);
    }

    async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var packet = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                var reply = TryParseReply(packet.Buffer);
                if (reply is not null)
                    ReplyReceived?.Invoke(reply);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException ex) { Faulted?.Invoke(ex.Message); }
    }

    public static WsjtxReply? TryParseReply(byte[] buf)
    {
        try
        {
            var r = new WsjtxPacketReader(buf);
            if (r.U32() != WsjtxUdpLogger.Magic) return null;
            r.U32();
            if (r.U32() != TypeReply) return null;

            string id = r.Utf8() ?? "";
            uint timeMs = r.U32();
            int snr = r.I32();
            double dt = r.F64();
            uint hz = r.U32();
            string mode = r.Utf8() ?? "";
            string message = r.Utf8() ?? "";
            return new WsjtxReply(id, timeMs, snr, dt, hz, mode, message);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }

    sealed class WsjtxPacketReader
    {
        readonly byte[] _buf;
        int _pos;

        public WsjtxPacketReader(byte[] buf) => _buf = buf;

        public uint U32()
        {
            uint v = BinaryPrimitives.ReadUInt32BigEndian(_buf.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public int I32()
        {
            int v = BinaryPrimitives.ReadInt32BigEndian(_buf.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public double F64()
        {
            double v = BinaryPrimitives.ReadDoubleBigEndian(_buf.AsSpan(_pos));
            _pos += 8;
            return v;
        }

        public string? Utf8()
        {
            uint len = U32();
            if (len == 0xFFFFFFFFu) return null;
            string s = Encoding.UTF8.GetString(_buf, _pos, checked((int)len));
            _pos += (int)len;
            return s;
        }
    }
}
