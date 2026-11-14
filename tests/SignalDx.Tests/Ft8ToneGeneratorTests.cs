using SignalDx.Ft8;
using Xunit;

namespace SignalDx.Tests;

public class Ft8ToneGeneratorTests
{
    [Fact]
    public void Generate_ProducesNonSilentAudio()
    {
        var audio = Ft8ToneGenerator.Generate("CQ W1ABC FN42", 1500, seconds: 1.0);
        Assert.True(audio.Length > 1000);
        Assert.Contains(audio, s => Math.Abs(s) > 0.01f);
    }
}
