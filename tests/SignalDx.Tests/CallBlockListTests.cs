using SignalDx.Logging;
using Xunit;

namespace SignalDx.Tests;

public class CallBlockListTests
{
    [Theory]
    [InlineData("K1ABC", "K1ABC", true)]
    [InlineData("k1abc", "K1ABC", true)]
    [InlineData("K1ABC", "K1ABC/P", true)]
    [InlineData("K1ABC", "W4/K1ABC", true)]
    [InlineData("K1ABC", "K1ABD", false)]
    [InlineData("K1*", "K1XYZ", true)]
    [InlineData("K1*", "W1K1A", false)]
    [InlineData("*XYZ", "K9XYZ", true)]
    [InlineData("*XYZ", "K9XYA", false)]
    [InlineData("5R8IC", "<5R8IC>", true)]
    public void IsBlocked_MatchesExactAndWildcard(string entry, string call, bool expected) =>
        Assert.Equal(expected, CallBlockList.IsBlocked([entry], call));

    [Fact]
    public void IsBlocked_NullAndEmpty()
    {
        Assert.False(CallBlockList.IsBlocked([], "K1ABC"));
        Assert.False(CallBlockList.IsBlocked(["K1ABC"], null));
        Assert.False(CallBlockList.IsBlocked(["K1ABC"], ""));
    }

    [Theory]
    [InlineData(" k1abc ", "K1ABC")]
    [InlineData("k1*", "K1*")]
    [InlineData("*", null)]
    [InlineData("K", null)]
    [InlineData("K1 ABC!", null)]
    public void NormalizeEntry(string input, string? expected) =>
        Assert.Equal(expected, CallBlockList.NormalizeEntry(input));
}
