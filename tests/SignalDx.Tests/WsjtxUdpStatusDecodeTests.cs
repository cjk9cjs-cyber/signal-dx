using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SignalDx.Logging;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class WsjtxUdpStatusDecodeTests
{
    [Fact]
    public async Task SendStatus_WritesWsjtxStatusLayout()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        using var logger = new WsjtxUdpLogger("127.0.0.1", port);

        logger.SendStatus(new WsjtxStatus(
            DialHz: 14074000,
            Mode: "FT8",
            DxCall: "K1ABC",
            Report: "-10",
            TxMode: "FT8",
            TxEnabled: true,
            Transmitting: false,
            Decoding: true,
            RxDf: 1500,
            TxDf: 1200,
            DeCall: "K9CJS",
            DeGrid: "EN61",
            DxGrid: "FN42",
            TrPeriod: 15,
            Config: "Signal DX",
            TxMessage: "K1ABC K9CJS EN61"));

        var packet = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var r = new PacketReader(packet.Buffer);

        Assert.Equal(WsjtxUdpLogger.Magic, r.U32());
        Assert.Equal(WsjtxUdpLogger.Schema, r.U32());
        Assert.Equal(1u, r.U32());
        Assert.Equal("SignalDX", r.Utf8());
        Assert.Equal(14074000UL, r.U64());
        Assert.Equal("FT8", r.Utf8());
        Assert.Equal("K1ABC", r.Utf8());
        Assert.Equal("-10", r.Utf8());
        Assert.Equal("FT8", r.Utf8());
        Assert.True(r.Bool());
        Assert.False(r.Bool());
        Assert.True(r.Bool());
        Assert.Equal(1500u, r.U32());
        Assert.Equal(1200u, r.U32());
        Assert.Equal("K9CJS", r.Utf8());
        Assert.Equal("EN61", r.Utf8());
        Assert.Equal("FN42", r.Utf8());
        Assert.False(r.Bool());
        Assert.Equal("", r.Utf8());
        Assert.False(r.Bool());
        Assert.Equal((byte)0, r.Byte());
        Assert.Equal(0u, r.U32());
        Assert.Equal(15u, r.U32());
        Assert.Equal("Signal DX", r.Utf8());
        Assert.Equal("K1ABC K9CJS EN61", r.Utf8());
        Assert.True(r.AtEnd);
    }

    [Fact]
    public async Task SendDecode_WritesWsjtxDecodeLayout()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        using var logger = new WsjtxUdpLogger("127.0.0.1", port);

        logger.SendDecode(new DecodeHit
        {
            Utc = new DateTime(2026, 11, 27, 13, 5, 15, DateTimeKind.Utc),
            Mode = "FT8",
            Hz = 1234,
            SnrDb = -7,
            Dt = 0.2,
            Message = "CQ K1ABC FN42",
            IsNew = true,
        });

        var packet = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var r = new PacketReader(packet.Buffer);

        Assert.Equal(WsjtxUdpLogger.Magic, r.U32());
        Assert.Equal(WsjtxUdpLogger.Schema, r.U32());
        Assert.Equal(2u, r.U32());
        Assert.Equal("SignalDX", r.Utf8());
        Assert.True(r.Bool());
        Assert.Equal(47115000u, r.U32());
        Assert.Equal(-7, r.I32());
        Assert.Equal(0.2, r.F64());
        Assert.Equal(1234u, r.U32());
        Assert.Equal("~", r.Utf8());
        Assert.Equal("CQ K1ABC FN42", r.Utf8());
        Assert.False(r.Bool());
        Assert.False(r.Bool());
        Assert.True(r.AtEnd);
    }

    sealed class PacketReader
    {
        readonly byte[] _buf;
        int _pos;

        public PacketReader(byte[] buf) => _buf = buf;

        public bool AtEnd => _pos == _buf.Length;

        public byte Byte() => _buf[_pos++];

        public bool Bool() => _buf[_pos++] != 0;

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

        public ulong U64()
        {
            ulong v = BinaryPrimitives.ReadUInt64BigEndian(_buf.AsSpan(_pos));
            _pos += 8;
            return v;
        }

        public double F64()
        {
            double v = BinaryPrimitives.ReadDoubleBigEndian(_buf.AsSpan(_pos));
            _pos += 8;
            return v;
        }

        public string Utf8()
        {
            uint len = U32();
            string s = Encoding.UTF8.GetString(_buf, _pos, (int)len);
            _pos += (int)len;
            return s;
        }
    }
}
