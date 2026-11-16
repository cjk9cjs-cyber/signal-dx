using SignalDx.Ft8;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class AutoSequencerTests
{
    [Fact]
    public void AdvancesFromCqWhenCalled()
    {
        var station = new StationSettings { MyCall = "W1ABC", MyGrid = "FN42" };
        var seq = new AutoSequencer();
        seq.StartCq();
        Assert.True(seq.TryAdvanceFromDecode("W1ABC K1XYZ FN31", station));
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("K1XYZ", seq.DxCall);
    }
}
