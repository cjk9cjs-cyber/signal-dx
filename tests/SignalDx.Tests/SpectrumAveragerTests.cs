using SignalDx.Dsp;
using Xunit;

namespace SignalDx.Tests;

public class SpectrumAveragerTests
{
    const int Rate = 48000;

    [Fact]
    public void Averages_in_power_domain()
    {
        var avg = new SpectrumAverager(2, depth: 2);
        avg.Add([0f, 10f]);
        var result = avg.Add([10f, 10f]);

        Assert.Equal(10f * MathF.Log10((1f + 10f) / 2f), result[0], 3);
        Assert.Equal(10f, result[1], 3);
    }

    /// <summary>Hopping 4-FSK tone at the given SNR in a 2500 Hz reference bandwidth, like a real WSPR trace.</summary>
    static float[] WsprLikeAudio(int seconds, double snrDb, int seed)
    {
        int n = Rate * seconds;
        var rng = new Random(seed);
        double sigma = Math.Sqrt((Rate / 2.0) / 2500.0);
        double amp = Math.Sqrt(2 * Math.Pow(10, snrDb / 10.0));
        var audio = new float[n];
        double phase = 0;
        double symbolSamples = 8192.0 / 12000.0 * Rate;
        int[] tones = [0, 2, 1, 3, 3, 1, 0, 2, 2, 0, 3, 1];
        for (int i = 0; i < n; i++)
        {
            int sym = tones[(int)(i / symbolSamples) % tones.Length];
            phase += 2 * Math.PI * (1500 + sym * 1.4648) / Rate;
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            double gauss = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            audio[i] = (float)(amp * Math.Sin(phase) + sigma * gauss);
        }
        return audio;
    }

    /// <summary>How many noise standard deviations the tone region sits above the neighbouring noise bins.</summary>
    static double Detectability(IReadOnlyList<float> db, int fftSize)
    {
        int Bin(double hz) => (int)Math.Round(hz * fftSize / Rate);
        var noise = new List<double>();
        for (int b = Bin(1400); b < Bin(1480); b++) noise.Add(db[b]);
        double mean = noise.Average();
        double std = Math.Sqrt(noise.Sum(v => (v - mean) * (v - mean)) / noise.Count);
        float peak = float.MinValue;
        for (int b = Bin(1497); b <= Bin(1507); b++) peak = Math.Max(peak, db[b]);
        return (peak - mean) / std;
    }

    [Fact]
    public void Long_fft_makes_a_weak_wspr_trace_more_detectable_than_the_short_fft()
    {
        var audio = WsprLikeAudio(seconds: 40, snrDb: -25, seed: 7);

        const int shortN = 4096;
        const int longN = 32768;
        var shortFft = new SpectrumAnalyzer(shortN);
        var longFft = new SpectrumAnalyzer(longN);
        var avg = new SpectrumAverager(longN / 2, depth: 3);

        double shortScore = 0, longScore = 0;
        int frames = 0;
        for (int end = longN + Rate; end <= audio.Length; end += Rate / 4)
        {
            shortFft.Analyze(audio.AsSpan(end - shortN, shortN));
            longFft.Analyze(audio.AsSpan(end - longN, longN));
            var shown = avg.Add(longFft.MagnitudesDb);
            if (frames >= 2)
            {
                shortScore += Detectability(shortFft.MagnitudesDb, shortN);
                longScore += Detectability(shown, longN);
            }
            frames++;
        }
        int scored = frames - 2;
        shortScore /= scored;
        longScore /= scored;

        Assert.True(longScore >= 2.0, $"long FFT detectability {longScore:0.0} (short {shortScore:0.0})");
        Assert.True(longScore > shortScore * 2, $"long {longScore:0.0} vs short {shortScore:0.0}");
    }
}
