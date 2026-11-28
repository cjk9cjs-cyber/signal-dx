using SignalDx.Ft8;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class HashedCallHintTests
{
    static DecodeHit Hit(int hz, string message, string mode = "FT8") => new()
    {
        Utc = DateTime.UtcNow,
        Mode = mode,
        Hz = hz,
        SnrDb = -10,
        Dt = 0,
        Message = message,
    };

    [Fact]
    public void Suggest_ReturnsResolvedCallsOnSameFrequency_NewestFirst()
    {
        var target = Hit(1523, "K9CJS <...> -10");
        var recent = new[]
        {
            Hit(1522, "K9CJS <W1ABC/P> -08"),
            Hit(1524, "CQ W2XYZ/MM FN31"),
            Hit(1500, "CQ FAR1AWAY FN31"),
            Hit(1523, "CQ <...> FN42"),
            Hit(1523, "CQ W1ABC/P FN42"),
        };

        var result = HashedCallHint.Suggest(target, recent, "K9CJS");

        Assert.Equal(new[] { "W1ABC/P", "W2XYZ/MM" }, result);
    }

    [Fact]
    public void Suggest_ExcludesMyCallAndUnresolvedCalls()
    {
        var target = Hit(1523, "K9CJS <...> -10");
        var recent = new[]
        {
            Hit(1523, "CQ K9CJS EN52"),
            Hit(1523, "K9CJS <...> -10"),
        };

        Assert.Empty(HashedCallHint.Suggest(target, recent, "K9CJS"));
    }

    [Fact]
    public void Suggest_IgnoresOtherModes()
    {
        var target = Hit(1523, "K9CJS <...> -10", "FT8");
        var recent = new[] { Hit(1523, "CQ W1ABC FN42", "FT4") };

        Assert.Empty(HashedCallHint.Suggest(target, recent, "K9CJS"));
    }

    [Fact]
    public void IsUnresolved_OnlyMatchesUnknownHash()
    {
        Assert.True(HashedCallHint.IsUnresolved(Hit(1500, "K9CJS <...> -10")));
        Assert.False(HashedCallHint.IsUnresolved(Hit(1500, "K9CJS <W1ABC/P> -10")));
    }
}
