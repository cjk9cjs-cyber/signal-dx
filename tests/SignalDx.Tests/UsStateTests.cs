using SignalDx.Geo;
using Xunit;

namespace SignalDx.Tests;

public class UsStateTests
{
    [Theory]
    [InlineData(39.5, -89.0, "IL")]
    [InlineData(43.07, -89.4, "WI")]
    [InlineData(44.5, -89.5, "WI")]
    [InlineData(40.2, -74.6, "NJ")]
    [InlineData(40.7, -74.0, "NY")]
    [InlineData(41.5, -81.7, "OH")]
    public void FromLatLon_ReturnsStateContainingPoint(double lat, double lon, string expected)
    {
        Assert.Equal(expected, UsState.FromLatLon(lat, lon));
    }

    [Fact]
    public void FromGrid_UsesSquareCenter()
    {
        Assert.Equal("IL", UsState.FromGrid("EM59"));
    }

    [Fact]
    public void FromGrid_EN52_IsWisconsin()
    {
        Assert.Equal("WI", UsState.FromGrid("EN52"));
    }

    [Fact]
    public void FromGrid_InvalidReturnsNull()
    {
        Assert.Null(UsState.FromGrid(""));
        Assert.Null(UsState.FromGrid("ZZ"));
    }

    [Fact]
    public void FromLatLon_LakeMichiganShoreResolvesToNearestState()
    {
        Assert.Equal("MI", UsState.FromLatLon(43.0, -86.6));
    }

    [Fact]
    public void FromLatLon_FarOffshoreReturnsNull()
    {
        Assert.Null(UsState.FromLatLon(30.0, -60.0));
    }
}
