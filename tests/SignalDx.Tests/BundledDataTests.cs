using SignalDx.Ft8;
using SignalDx.Geo;
using Xunit;

namespace SignalDx.Tests;

public class BundledDataTests
{
    [Fact]
    public void CallGridDatabase_LooksUpBundledGrid()
    {
        Assert.Equal("EN52", CallGridDatabase.Lookup("AB9AI"));
    }

    [Fact]
    public void CallGridDatabase_PrefersSixCharacterGrid()
    {
        Assert.Equal("EN54TG", CallGridDatabase.PreferredGrid("AB9AH", "EN54"));
    }

    [Fact]
    public void KnownCallsigns_ContainsListedCall()
    {
        Assert.True(KnownCallsigns.Contains("1A0C"));
    }

    [Fact]
    public void KnownCallsigns_FlagsUnlistedCall()
    {
        Assert.False(KnownCallsigns.Contains("ZZ9ZZZ"));
    }
}
