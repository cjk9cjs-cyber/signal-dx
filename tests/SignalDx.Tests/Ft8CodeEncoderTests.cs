using SignalDx.Ft8;
using Xunit;

namespace SignalDx.Tests;

public class Ft8CodeEncoderTests
{
    [Fact]
    public void ParseChannelSymbols_Reads79Tones()
    {
        const string sample = """
            Message                               Decoded
            1. CQ K9CJS EN52                         CQ K9CJS EN52

            Channel symbols (79 tones):
              Sync               Data               Sync               Data               Sync
            3140652 00000000100575567570512303226 3140652 41054460146170745766346543012 3140652
            """;

        int[] symbols = Ft8CodeEncoder.ParseChannelSymbols(sample);
        Assert.Equal(79, symbols.Length);
        Assert.Equal(new[] { 3, 1, 4, 0, 6, 5, 2 }, symbols.Take(7));
        Assert.All(symbols, s => Assert.InRange(s, 0, 7));
    }

    [Fact]
    public void EncodeSymbols_WithWsjtx_Produces79ValidTones()
    {
        string? exe = Ft8CodePaths.FindFt8Code();
        if (exe is null)
        {
            // CI / machines without WSJT-X — skip rather than fail the suite.
            return;
        }

        int[] symbols = Ft8CodeEncoder.EncodeSymbols("CQ K9CJS EN52", "FT8", exe);
        Assert.Equal(79, symbols.Length);
        Assert.Equal(new[] { 3, 1, 4, 0, 6, 5, 2 }, symbols.Take(7));
    }
}
