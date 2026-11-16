namespace SignalDx.Audio;

/// <summary>
/// Downsamples 48 kHz mono float audio to 12 kHz 16-bit PCM WAV for jtdxjt9 / jt9 file decode.
/// </summary>
public static class Wav12kWriter
{
    public const int InputRate = 48000;
    public const int OutputRate = 12000;
    const int Decimation = InputRate / OutputRate; // 4

    public static string DecodeDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx", "decode");

    public static float[] Downsample48kTo12k(ReadOnlySpan<float> samples48k)
    {
        int outLen = samples48k.Length / Decimation;
        if (outLen <= 0) return [];

        var dst = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            int baseIdx = i * Decimation;
            // Box average (anti-alias light) over the decimation window.
            float sum = 0;
            for (int k = 0; k < Decimation; k++)
                sum += samples48k[baseIdx + k];
            dst[i] = sum / Decimation;
        }
        return dst;
    }

    public static void WritePcm16Mono(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        int dataBytes = samples.Length * 2;
        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);           // PCM chunk size
        bw.Write((short)1);     // PCM
        bw.Write((short)1);     // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // byte rate
        bw.Write((short)2);     // block align
        bw.Write((short)16);    // bits
        bw.Write("data"u8);
        bw.Write(dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            float x = Math.Clamp(samples[i], -1f, 1f);
            short s = (short)Math.Round(x * 32767f);
            bw.Write(s);
        }
    }

    /// <summary>Write a 12 kHz WAV under %LocalAppData%\SignalDx\decode\ and return its path.</summary>
    public static string WriteSlotWav(ReadOnlySpan<float> samples48k, string? fileName = null)
    {
        string dir = DecodeDirectory;
        Directory.CreateDirectory(dir);
        string name = string.IsNullOrWhiteSpace(fileName)
            ? $"slot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav"
            : fileName;
        string path = Path.Combine(dir, name);
        var samples12k = Downsample48kTo12k(samples48k);
        WritePcm16Mono(path, samples12k, OutputRate);
        return path;
    }
}
