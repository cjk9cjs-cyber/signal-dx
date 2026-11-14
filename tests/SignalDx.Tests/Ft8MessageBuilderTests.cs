using SignalDx.Ft8;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class Ft8MessageBuilderTests
{
    [Fact]
    public void Build_ReplacesTokens()
    {
        var station = new StationSettings { MyCall = "w1abc", MyGrid = "fn42" };
        string msg = Ft8MessageBuilder.Build("CQ {mycall} {mygrid}", station, "K1XYZ", "+05");
        Assert.Equal("CQ W1ABC FN42", msg);
    }

    [Fact]
    public void Parse_DetectsCqAndGrid()
    {
        var (call, grid, isCq) = Ft8MessageBuilder.Parse("CQ K1ABC FN42");
        Assert.True(isCq);
        Assert.Equal("K1ABC", call);
        Assert.Equal("FN42", grid);
    }

    [Fact]
    public void Parse_DirectedCq()
    {
        var (call, grid, isCq) = Ft8MessageBuilder.Parse("CQ DX VE3DX FN03");
        Assert.True(isCq);
        Assert.Equal("VE3DX", call);
        Assert.Equal("FN03", grid);
    }
}
