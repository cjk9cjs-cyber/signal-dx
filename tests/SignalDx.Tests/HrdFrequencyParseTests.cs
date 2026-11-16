using SignalDx.Cat;
using Xunit;

namespace SignalDx.Tests;

public class HrdFrequencyParseTests
{
    [Theory]
    [InlineData("14074000", 14_074_000)]
    [InlineData("14074", 14_074_000)]
    [InlineData("14.074", 14_074_000)]
    [InlineData("14.074000", 14_074_000)]
    [InlineData("7074000", 7_074_000)]
    [InlineData("7.074", 7_074_000)]
    [InlineData("7074000-21079000", 7_074_000)]
    [InlineData("14074000-21079000", 14_074_000)]
    [InlineData("[367] 7074000", 7_074_000)]
    public void Parses_hrd_frequency_replies(string raw, long expectedHz)
    {
        Assert.Equal(expectedHz, HrdCatController.ParseFrequencyHz(raw));
    }

    [Fact]
    public void Empty_reply_is_null()
    {
        Assert.Null(HrdCatController.ParseFrequencyHz(""));
        Assert.Null(HrdCatController.ParseFrequencyHz("   "));
    }
}
