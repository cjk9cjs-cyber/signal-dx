using SignalDx.Dsp;
using Xunit;

namespace SignalDx.Tests;

public class SpectrumAnalyzerTests
{
    [Fact]
    public void Analyze_FindsTonePeakNearExpectedBin()
    {
        const int sampleRate = 48000;
        const int fftSize = 2048;
        const double toneHz = 1500;
        var samples = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * toneHz * i / sampleRate);

        var analyzer = new SpectrumAnalyzer(fftSize);
        analyzer.Analyze(samples);

        int expected = SpectrumAnalyzer.HzToBin(toneHz, sampleRate, fftSize);
        int peak = 0;
        float best = float.NegativeInfinity;
        for (int i = 0; i < analyzer.MagnitudesDb.Count; i++)
        {
            if (analyzer.MagnitudesDb[i] > best)
            {
                best = analyzer.MagnitudesDb[i];
                peak = i;
            }
        }

        Assert.InRange(peak, expected - 2, expected + 2);
    }
}
