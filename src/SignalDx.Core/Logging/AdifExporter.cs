using System.Globalization;
using System.Text;
using SignalDx.Models;

namespace SignalDx.Logging;

public static class AdifExporter
{
    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SignalDx");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "signal-dx.adi");
        }
    }

    public static string ToRecord(QsoEntry qso)
    {
        var sb = new StringBuilder();
        void F(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            sb.Append('<').Append(name).Append(':').Append(value.Length).Append('>')
              .Append(value).Append(' ');
        }

        var start = DateTime.SpecifyKind(qso.UtcStart, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(qso.UtcEnd ?? qso.UtcStart, DateTimeKind.Utc);

        F("CALL", qso.DxCall);
        F("GRIDSQUARE", qso.DxGrid);
        F("MODE", qso.Mode);
        F("BAND", NormalizeBand(qso.Band));
        F("FREQ", qso.DialMhz.ToString("0.000000", CultureInfo.InvariantCulture));
        F("QSO_DATE", start.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        F("TIME_ON", start.ToString("HHmmss", CultureInfo.InvariantCulture));
        F("QSO_DATE_OFF", end.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        F("TIME_OFF", end.ToString("HHmmss", CultureInfo.InvariantCulture));
        F("RST_SENT", qso.SentRst);
        F("RST_RCVD", qso.RcvdRst);
        F("STATION_CALLSIGN", qso.MyCall);
        F("MY_GRIDSQUARE", qso.MyGrid);
        F("OPERATOR", qso.MyCall);
        F("COMMENT", qso.Comment);
        sb.Append("<EOR>");
        return sb.ToString();
    }

    public static string ToFile(IEnumerable<QsoEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ADIF 3.1.4 Export from Signal DX");
        sb.AppendLine("<ADIF_VER:5>3.1.4");
        sb.AppendLine("<PROGRAMID:8>SignalDX");
        sb.AppendLine("<EOH>");
        foreach (var e in entries)
            sb.AppendLine(ToRecord(e));
        return sb.ToString();
    }

    public static void Append(string path, QsoEntry qso)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
        using var sw = new StreamWriter(path, append: true, Encoding.UTF8);
        if (!exists)
        {
            sw.WriteLine("ADIF 3.1.4 Export from Signal DX");
            sw.WriteLine("<ADIF_VER:5>3.1.4");
            sw.WriteLine("<PROGRAMID:8>SignalDX");
            sw.WriteLine("<EOH>");
        }
        sw.WriteLine(ToRecord(qso));
    }

    public static void WriteAll(string path, IEnumerable<QsoEntry> entries) =>
        File.WriteAllText(path, ToFile(entries), Encoding.UTF8);

    static string NormalizeBand(string band)
    {
        var b = band.Trim().ToLowerInvariant();
        return b.EndsWith('m') ? b : b + "m";
    }
}
