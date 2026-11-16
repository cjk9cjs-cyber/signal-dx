namespace SignalDx.Ft8;

/// <summary>
/// Generates an 8-FSK FT8-like waveform for monitoring TX audio.
/// Uses a deterministic symbol sequence derived from the message (not full LDPC/FT8 channel coding).
/// </summary>
public static class Ft8ToneGenerator
{
    const int SampleRate = 48000;

    public static float[] Generate(string message, int baseHz, double seconds = 12.64, double drive01 = 0.4)
    {
        var symbols = BuildSymbols(message);
        int totalSamples = (int)(SampleRate * seconds);
        var audio = new float[totalSamples];
        double samplesPerSymbol = SampleRate * Ft8Timing.SymbolDurationSec;
        double phase = 0;
        // Full-scale at 100% drive — use the Drive slider to set ALC on the radio.
        double amplitude = Math.Clamp(drive01, 0, 1);

        for (int i = 0; i < totalSamples; i++)
        {
            int sym = Math.Min((int)(i / samplesPerSymbol), symbols.Length - 1);
            double freq = baseHz + symbols[sym] * Ft8Timing.ToneSpacingHz;
            phase += 2 * Math.PI * freq / SampleRate;
            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
            double env = Envelope(i, totalSamples);
            audio[i] = (float)(amplitude * env * Math.Sin(phase));
        }

        return audio;
    }

    static int[] BuildSymbols(string message)
    {
        // Costas arrays at start/mid/end-ish + message-derived payload tones
        int[] costas = [3, 1, 4, 0, 6, 5, 2];
        var symbols = new int[Ft8Timing.SymbolCount];
        Array.Copy(costas, 0, symbols, 0, 7);
        Array.Copy(costas, 0, symbols, 36, 7);
        Array.Copy(costas, 0, symbols, 72, 7);

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(message.ToUpperInvariant()));

        int payloadIndex = 0;
        for (int i = 0; i < symbols.Length; i++)
        {
            if (i is >= 0 and < 7 or >= 36 and < 43 or >= 72 and < 79)
                continue;
            symbols[i] = hash[payloadIndex % hash.Length] % 8;
            payloadIndex++;
        }

        return symbols;
    }

    static double Envelope(int i, int n)
    {
        const int ramp = 480; // 10 ms @ 48 kHz
        if (i < ramp) return i / (double)ramp;
        if (i > n - ramp) return (n - i) / (double)ramp;
        return 1.0;
    }
}
