using SignalDx.Ft8;
using Xunit;

namespace SignalDx.Tests;

public class Ft8ToneGeneratorTests
{
    [Fact]
    public void Synthesize_ProducesNonSilentAudio()
    {
        int[] symbols = Enumerable.Range(0, 79).Select(i => i % 8).ToArray();
        var audio = Ft8ToneGenerator.Synthesize(symbols, 1500, seconds: 1.0);
        Assert.True(audio.Length > 1000);
        Assert.Contains(audio, s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    public void Generate_WithWsjtx_ProducesFullSlotAudio()
    {
        if (Ft8CodePaths.FindFt8Code() is null)
            return;

        var audio = Ft8ToneGenerator.Generate("CQ K9CJS EN52", 1500, seconds: 12.64, mode: "FT8");
        Assert.True(audio.Length > 400_000);
        Assert.Contains(audio, s => Math.Abs(s) > 0.05f);
    }
}
