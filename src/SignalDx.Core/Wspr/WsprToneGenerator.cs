namespace SignalDx.Wspr;

/// <summary>Synthesize WSPR-2 4-FSK TX audio at 48 kHz (continuous-phase).</summary>
public static class WsprToneGenerator
{
    const int SampleRate = 48000;

    /// <summary>
    /// Build a full WSPR-2 slot waveform: 1 s lead-in silence + 110.592 s of 4-FSK.
    /// </summary>
    public static float[] Generate(
        string callsign,
        string grid4,
        int powerDbm,
        int baseHz = 1500,
        double drive01 = 1.0)
    {
        int[] symbols = WsprEncoder.EncodeSymbols(callsign, grid4, powerDbm);
        return Synthesize(symbols, baseHz, drive01, includeLeadIn: true);
    }

    public static float[] Synthesize(
        int[] symbols,
        int baseHz,
        double drive01 = 1.0,
        bool includeLeadIn = true)
    {
        if (symbols is null || symbols.Length != WsprEncoder.SymbolCount)
            throw new ArgumentException("WSPR requires 162 channel symbols.", nameof(symbols));

        double amplitude = Math.Clamp(drive01, 0, 1);
        int leadSamples = includeLeadIn
            ? (int)Math.Round(SampleRate * WsprEncoder.LeadInSec)
            : 0;
        double samplesPerSymbol = SampleRate * WsprEncoder.SymbolDurationSec;
        int signalSamples = (int)Math.Round(samplesPerSymbol * symbols.Length);
        var audio = new float[leadSamples + signalSamples];

        double phase = 0;
        int ramp = Math.Min(SampleRate / 100, signalSamples / 4); // 10 ms

        for (int i = 0; i < signalSamples; i++)
        {
            int symIdx = Math.Min((int)(i / samplesPerSymbol), symbols.Length - 1);
            int tone = Math.Clamp(symbols[symIdx], 0, 3);
            // TxHz is the audio passband center; four tones sit at ±0.5 / ±1.5 · spacing.
            double freq = baseHz + (tone - 1.5) * WsprEncoder.ToneSpacingHz;
            phase += 2 * Math.PI * freq / SampleRate;
            if (phase > Math.PI * 2)
                phase %= Math.PI * 2;

            double env = 1.0;
            if (i < ramp) env = i / (double)ramp;
            int fromEnd = signalSamples - 1 - i;
            if (fromEnd < ramp) env = Math.Min(env, fromEnd / (double)ramp);

            audio[leadSamples + i] = (float)(amplitude * env * Math.Sin(phase));
        }

        return audio;
    }
}
