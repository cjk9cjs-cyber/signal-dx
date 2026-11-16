using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SignalDx.Models;

namespace SignalDx.Logging;

/// <summary>
/// Emits WSJT-X compatible UDP messages (Heartbeat, QSO Logged, Logged ADIF)
/// so Log4OM, Logger32, Ham Radio Deluxe, Cloudlog bridges, etc. can ingest QSOs.
/// </summary>
public sealed class WsjtxUdpLogger : IDisposable
{
    public const uint Magic = 0xadbccbda;
    public const uint Schema = 3;
    public const string Id = "SignalDX";

    readonly UdpClient _udp = new();
    IPEndPoint _endpoint;

    public WsjtxUdpLogger(string host, int port)
    {
        _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
    }

    public void UpdateEndpoint(string host, int port) =>
        _endpoint = new IPEndPoint(IPAddress.Parse(host), port);

    public void SendHeartbeat()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteHeader(w, 0);
        WriteUtf8(w, Id);
        WriteU32(w, Schema);
        WriteUtf8(w, "2.0.0");
        WriteUtf8(w, "signal-dx");
        _udp.Send(ms.ToArray(), _endpoint);
    }

    public void SendQsoLogged(QsoEntry qso)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteHeader(w, 5);
        WriteUtf8(w, Id);
        WriteQDateTime(w, qso.UtcEnd ?? qso.UtcStart);
        WriteUtf8(w, qso.DxCall);
        WriteUtf8(w, qso.DxGrid);
        WriteU64(w, (ulong)Math.Round(qso.DialMhz * 1_000_000d));
        WriteUtf8(w, qso.Mode);
        WriteUtf8(w, qso.SentRst);
        WriteUtf8(w, qso.RcvdRst);
        WriteUtf8(w, "");
        WriteUtf8(w, qso.Comment);
        WriteUtf8(w, "");
        WriteQDateTime(w, qso.UtcStart);
        WriteUtf8(w, qso.MyCall);
        WriteUtf8(w, qso.MyCall);
        WriteUtf8(w, qso.MyGrid);
        WriteUtf8(w, qso.SentRst);
        WriteUtf8(w, qso.RcvdRst);
        WriteUtf8(w, "");
        _udp.Send(ms.ToArray(), _endpoint);
    }

    public void SendLoggedAdif(QsoEntry qso)
    {
        string adif =
            "ADIF 3.1.4 Export from Signal DX\n" +
            "<ADIF_VER:5>3.1.4 <PROGRAMID:8>SignalDX <EOH>\n" +
            AdifExporter.ToRecord(qso) + "\n";

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteHeader(w, 12);
        WriteUtf8(w, Id);
        WriteUtf8(w, adif);
        _udp.Send(ms.ToArray(), _endpoint);
    }

    static void WriteHeader(BinaryWriter w, uint type)
    {
        WriteU32(w, Magic);
        WriteU32(w, Schema);
        WriteU32(w, type);
    }

    static void WriteU32(BinaryWriter w, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        w.Write(b);
    }

    static void WriteU64(BinaryWriter w, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        w.Write(b);
    }

    static void WriteI64(BinaryWriter w, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        w.Write(b);
    }

    static void WriteUtf8(BinaryWriter w, string? s)
    {
        if (s is null)
        {
            WriteU32(w, 0xFFFFFFFFu);
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        WriteU32(w, (uint)bytes.Length);
        w.Write(bytes);
    }

    static void WriteQDateTime(BinaryWriter w, DateTime utc)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        // Julian day number for QDate
        long julian = ToJulianDay(u.Year, u.Month, u.Day);
        WriteI64(w, julian);
        uint ms = (uint)u.TimeOfDay.TotalMilliseconds;
        WriteU32(w, ms);
        w.Write((byte)1); // UTC
    }

    static long ToJulianDay(int year, int month, int day)
    {
        // Fliegel & Van Flandern
        int a = (14 - month) / 12;
        int y = year + 4800 - a;
        int m = month + 12 * a - 3;
        return day + (153 * m + 2) / 5 + 365L * y + y / 4 - y / 100 + y / 400 - 32045;
    }

    public void Dispose() => _udp.Dispose();
}
