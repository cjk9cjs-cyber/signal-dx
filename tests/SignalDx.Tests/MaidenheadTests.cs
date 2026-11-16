using SignalDx.Geo;
using Xunit;

namespace SignalDx.Tests;

public class MaidenheadTests
{
    [Fact]
    public void Distance_Fn42ToIo91_IsReasonable()
    {
        // Rough Boston-area (FN42) to London-area (IO91)
        double? mi = Maidenhead.DistanceMiles("FN42", "IO91");
        Assert.NotNull(mi);
        Assert.InRange(mi!.Value, 2900, 3600);
    }

    [Fact]
    public void Distance_SameGrid_IsNearZero()
    {
        double? mi = Maidenhead.DistanceMiles("FN42", "FN42");
        Assert.NotNull(mi);
        Assert.True(mi < 1);
    }

    [Fact]
    public void Distance_MissingGrid_ReturnsNull()
    {
        Assert.Null(Maidenhead.DistanceMiles("FN42", null));
        Assert.Null(Maidenhead.DistanceMiles("", "IO91"));
    }
}
