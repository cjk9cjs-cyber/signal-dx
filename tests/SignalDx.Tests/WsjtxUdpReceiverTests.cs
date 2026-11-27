using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SignalDx.Logging;
using Xunit;

namespace SignalDx.Tests;

public class WsjtxUdpReceiverTests
{
    [Fact]
    public async Task ReplyPacket_RaisesReplyReceived()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;

        using var rx = new WsjtxUdpReceiver(port);
        var received = new TaskCompletionSource<WsjtxReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        rx.ReplyReceived += r => received.TrySetResult(r);

        using var sender = new UdpClient();
        byte[] packet = BuildReplyPacket("JTAlert", 47115000, -7, 0.2, 1234, "~", "CQ K1ABC FN42");
        await sender.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));

        var reply = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("JTAlert", reply.Id);
        Assert.Equal(47115000u, reply.TimeMs);
        Assert.Equal(-7, reply.Snr);
        Assert.Equal(0.2, reply.Dt);
        Assert.Equal(1234u, reply.Hz);
        Assert.Equal("~", reply.Mode);
        Assert.Equal("CQ K1ABC FN42", reply.Message);
    }

    [Fact]
    public void TryParseReply_IgnoresOtherMessageTypes()
    {
        byte[] heartbeat = BuildReplyPacket("JTAlert", 0, 0, 0, 0, "", "");
        BinaryPrimitives.WriteUInt32BigEndian(heartbeat.AsSpan(8), 0);
        Assert.Null(WsjtxUdpReceiver.TryParseReply(heartbeat));
    }

    [Fact]
    public void TryParseReply_RejectsGarbage()
    {
        Assert.Null(WsjtxUdpReceiver.TryParseReply([1, 2, 3]));
        Assert.Null(WsjtxUdpReceiver.TryParseReply([]));
    }

    static byte[] BuildReplyPacket(string id, uint timeMs, int snr, double dt, uint hz, string mode, string message)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteU32(w, WsjtxUdpLogger.Magic);
        WriteU32(w, WsjtxUdpLogger.Schema);
        WriteU32(w, 4);
        WriteUtf8(w, id);
        WriteU32(w, timeMs);
        WriteU32(w, (uint)snr);
        WriteF64(w, dt);
        WriteU32(w, hz);
        WriteUtf8(w, mode);
        WriteUtf8(w, message);
        w.Write((byte)0);
        w.Write((byte)0);
        return ms.ToArray();
    }

    static void WriteU32(BinaryWriter w, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        w.Write(b);
    }

    static void WriteF64(BinaryWriter w, double v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(b, v);
        w.Write(b);
    }

    static void WriteUtf8(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        WriteU32(w, (uint)bytes.Length);
        w.Write(bytes);
    }
}
