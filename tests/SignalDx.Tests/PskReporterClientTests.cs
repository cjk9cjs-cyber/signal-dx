using SignalDx.Logging;
using Xunit;

namespace SignalDx.Tests;

public class PskReporterClientTests
{
    [Fact]
    public void BuildPacket_HasIpfixHeaderAndMinimumSize()
    {
        uint seq = 0;
        var spots = new List<PskReporterClient.Spot>
        {
            new("K1JT", 14_074_000, -12, "FT8", 1_700_000_000)
        };
        byte[] packet = PskReporterClient.BuildPacket("K9CJS", "EN52", spots, ref seq, 0x12345678);

        Assert.True(packet.Length >= 16);
        Assert.Equal(10, (packet[0] << 8) | packet[1]);
        Assert.Equal(packet.Length, (packet[2] << 8) | packet[3]);
        Assert.Equal(1u, seq);
    }
}
