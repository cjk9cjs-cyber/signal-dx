using NAudio.Wave;

namespace SignalDx.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    WaveOutEvent? _out;
    BufferedWaveProvider? _buffer;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public static IReadOnlyList<(int Index, string Name)> ListDevices()
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            list.Add((i, caps.ProductName));
        }
        return list;
    }

    public void PlayMono(float[] samples, int sampleRate = AudioCaptureService.SampleRate, int deviceNumber = -1)
    {
        Stop();
        var format = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(format)
        {
            BufferLength = samples.Length * 2 + 1024,
            DiscardOnBufferOverflow = true
        };

        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float clipped = Math.Clamp(samples[i], -1f, 1f);
            short s = (short)(clipped * 32767);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        _buffer.AddSamples(bytes, 0, bytes.Length);

        _out = new WaveOutEvent { DeviceNumber = deviceNumber < 0 ? 0 : deviceNumber };
        _out.Init(_buffer);
        _out.Play();
    }

    public void Stop()
    {
        if (_out is null) return;
        try { _out.Stop(); } catch { /* ignore */ }
        _out.Dispose();
        _out = null;
        _buffer = null;
    }

    public void Dispose() => Stop();
}
