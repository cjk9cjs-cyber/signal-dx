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

    [Fact]
    public void BuildPacket_IncludesAntennaInReceiverRecord()
    {
        uint seq = 0;
        byte[] packet = PskReporterClient.BuildPacket("K9CJS", "EN52", [], ref seq, 1, "Dipole");

        byte[] antenna = [6, (byte)'D', (byte)'i', (byte)'p', (byte)'o', (byte)'l', (byte)'e'];
        Assert.True(packet.AsSpan().IndexOf(antenna) >= 0);

        Assert.Equal(0x2C, (packet[18] << 8) | packet[19]);
        Assert.Equal(4, (packet[22] << 8) | packet[23]);
    }
}