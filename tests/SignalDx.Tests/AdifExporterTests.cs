using SignalDx.Logging;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class AdifExporterTests
{
    [Fact]
    public void ToRecord_ContainsRequiredFields()
    {
        var qso = new QsoEntry
        {
            UtcStart = new DateTime(2026, 11, 14, 12, 30, 0, DateTimeKind.Utc),
            UtcEnd = new DateTime(2026, 11, 14, 12, 31, 0, DateTimeKind.Utc),
            DxCall = "K1ABC",
            DxGrid = "FN42",
            Mode = "FT8",
            Band = "20m",
            DialMhz = 14.074,
            MyCall = "W1XYZ",
            MyGrid = "FN31",
            SentRst = "-10",
            RcvdRst = "-12"
        };

        string adif = AdifExporter.ToRecord(qso);
        Assert.Contains("<CALL:5>K1ABC", adif);
        Assert.Contains("<MODE:3>FT8", adif);
        Assert.Contains("<BAND:3>20m", adif);
        Assert.Contains("<EOR>", adif);
    }
}
