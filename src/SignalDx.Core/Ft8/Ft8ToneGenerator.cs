namespace SignalDx.Ft8;

/// <summary>
/// Synthesizes FT8/FT4 8-FSK TX audio from real channel symbols (via WSJT-X ft8code/ft4code).
/// </summary>
public static class Ft8ToneGenerator
{
    const int SampleRate = 48000;

    /// <summary>
    /// Encode <paramref name="message"/> with WSJT-X tools and synthesize audio at <paramref name="baseHz"/>.
    /// Throws if the encoder is missing or the message cannot be packed.
    /// </summary>
    public static float[] Generate(
        string message,
        int baseHz,
        double seconds = 12.64,
        double drive01 = 1.0,
        string mode = "FT8",
        string? codeExeHint = null)
    {
        int[] symbols = Ft8CodeEncoder.EncodeSymbols(message, mode, codeExeHint);
        return Synthesize(symbols, baseHz, seconds, drive01, mode);
    }

    public static float[] Synthesize(
        int[] symbols,
        int baseHz,
        double seconds,
        double drive01 = 1.0,
        string mode = "FT8")
    {
        if (symbols is null || symbols.Length == 0)
            throw new ArgumentException("No channel symbols.", nameof(symbols));

        bool ft4 = mode.Equals("FT4", StringComparison.OrdinalIgnoreCase);
        double toneSpacing = ft4 ? 20.833333333 : Ft8Timing.ToneSpacingHz;
        double symbolDur = ft4 ? (4.48 / 105.0) : Ft8Timing.SymbolDurationSec;

        int totalSamples = (int)(SampleRate * seconds);
        var audio = new float[totalSamples];
        double samplesPerSymbol = SampleRate * symbolDur;
        double phase = 0;
        double amplitude = Math.Clamp(drive01, 0, 1);

        for (int i = 0; i < totalSamples; i++)
        {
            int symIdx = Math.Min((int)(i / samplesPerSymbol), symbols.Length - 1);
            int tone = Math.Clamp(symbols[symIdx], 0, 7);
            double freq = baseHz + tone * toneSpacing;
            phase += 2 * Math.PI * freq / SampleRate;
            if (phase > Math.PI * 2)
                phase %= Math.PI * 2;
            double env = Envelope(i, totalSamples);
            audio[i] = (float)(amplitude * env * Math.Sin(phase));
        }

        return audio;
    }

    /// <summary>
    /// Continuous single-tone carrier for SWR / ALC tune-up (not a valid FT8 message).
    /// </summary>
    public static float[] GenerateConstantTone(
        int hz,
        double seconds,
        double drive01 = 0.5)
    {
        int totalSamples = Math.Max(1, (int)(SampleRate * seconds));
        var audio = new float[totalSamples];
        double amplitude = Math.Clamp(drive01, 0, 1);
        double phase = 0;
        double step = 2 * Math.PI * Math.Clamp(hz, 200, 3000) / SampleRate;
        const int ramp = 480; // 10 ms

        for (int i = 0; i < totalSamples; i++)
        {
            phase += step;
            if (phase > Math.PI * 2)
                phase %= Math.PI * 2;
            double env = 1.0;
            if (i < ramp) env = i / (double)ramp;
            else if (i > totalSamples - ramp) env = (totalSamples - i) / (double)ramp;
            audio[i] = (float)(amplitude * env * Math.Sin(phase));
        }

        return audio;
    }

    static double Envelope(int i, int n)
    {
        const int ramp = 480; // 10 ms @ 48 kHz
        if (i < ramp) return i / (double)ramp;
        if (i > n - ramp) return (n - i) / (double)ramp;
        return 1.0;
    }
}
