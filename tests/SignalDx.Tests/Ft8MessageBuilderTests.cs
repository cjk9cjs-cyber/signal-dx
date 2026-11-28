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
    public void Build_DirectedCqCustomMacro()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        string dx = Ft8MessageBuilder.Build("CQ DX {mycall} {mygrid}", station, "", "-10");
        string eu = Ft8MessageBuilder.Build("CQ EU {mycall} {mygrid}", station, "", "-10");
        Assert.Equal("CQ DX K9CJS EN52", dx);
        Assert.Equal("CQ EU K9CJS EN52", eu);
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

    [Fact]
    public void Parse_QsoUsesCallingStationNotCalled()
    {
        // CALLED CALLER GRID — country/state must follow the transmitter (CALLER).
        var (call, grid, isCq) = Ft8MessageBuilder.Parse("K9CJS W1ABC FN42");
        Assert.False(isCq);
        Assert.Equal("W1ABC", call);
        Assert.Equal("FN42", grid);
    }

    [Theory]
    [InlineData("CQ 5R8IC LH28", "5R8IC")]
    [InlineData("K9CJS 5R8IC -12", "5R8IC")]
    [InlineData("K9CJS 3Y0J R-05", "3Y0J")]
    [InlineData("CQ DX 9A1A JN85", "9A1A")]
    public void Parse_AcceptsCallsStartingWithDigit(string message, string expected)
    {
        var (call, _, _) = Ft8MessageBuilder.Parse(message);
        Assert.Equal(expected, call);
    }

    [Theory]
    [InlineData("K9CJS <W1ABC> R-05", "W1ABC")]
    [InlineData("CQ <5R8IC>", "5R8IC")]
    [InlineData("K9CJS <...> -10", null)]
    public void Parse_HandlesHashedCalls(string message, string? expected)
    {
        var (call, _, _) = Ft8MessageBuilder.Parse(message);
        Assert.Equal(expected, call);
    }

    [Fact]
    public void Parse_StillRejectsReportsAsCalls()
    {
        Assert.False(Ft8MessageBuilder.LooksLikeCall("-12"));
        Assert.False(Ft8MessageBuilder.LooksLikeCall("+05"));
        Assert.False(Ft8MessageBuilder.LooksLikeCall("R-10"));
        Assert.False(Ft8MessageBuilder.LooksLikeCall("599"));
        Assert.Equal("Madagascar", SignalDx.Geo.CallsignCountry.FromCall("5R8IC"));
    }

    [Theory]
    [InlineData("W1ABC", true)]
    [InlineData("K9CJS", true)]
    [InlineData("W1ABC/P", true)]
    [InlineData("W1ABC/R", true)]
    [InlineData("2E0ABC", true)]
    [InlineData("A22A", true)]
    [InlineData("4U1UN", true)]
    [InlineData("W1ABC/7", false)]
    [InlineData("W1AW/0", false)]
    [InlineData("W1ABC/M", false)]
    [InlineData("EA8/W1ABC", false)]
    [InlineData("K1ABC/QRP", false)]
    [InlineData("3DA0AQ", false)]
    public void IsStandardFt8Call_ClassifiesCompoundAndNonstandardCalls(string call, bool expected)
    {
        Assert.Equal(expected, Ft8MessageBuilder.IsStandardFt8Call(call));
    }

    [Fact]
    public void Build_WrapsNonstandardCompoundDxCallInAngleBrackets()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        Assert.Equal("<W1ABC/7> K9CJS -10", Ft8MessageBuilder.Build("{dxcall} {mycall} {snr}", station, "W1ABC/7", "-10"));
        Assert.Equal("<W1ABC/7> K9CJS -10", Ft8MessageBuilder.Build("{dxcall} {mycall} {snr}", station, "<W1ABC/7>", "-10"));
        Assert.Equal("<EA8/W1ABC> K9CJS EN52", Ft8MessageBuilder.Build("{dxcall} {mycall} {mygrid}", station, "EA8/W1ABC", "-10"));
        Assert.Equal("W1ABC/P K9CJS -10", Ft8MessageBuilder.Build("{dxcall} {mycall} {snr}", station, "W1ABC/P", "-10"));
    }
}
