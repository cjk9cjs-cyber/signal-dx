using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SignalDx.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    IWavePlayer? _out;
    BufferedWaveProvider? _buffer;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public static IReadOnlyList<(int Index, string Name)> ListDevices()
    {
        var list = new List<(int, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int i = 0; i < devices.Count; i++)
                list.Add((i, devices[i].FriendlyName));
        }
        catch
        {
            // empty
        }
        return list;
    }

    /// <param name="volume01">0–1 TX drive into the radio (ALC audio level).</param>
    public void PlayMono(float[] samples, int sampleRate = AudioCaptureService.SampleRate, int deviceNumber = -1,
        float volume01 = 1f)
    {
        Stop();
        var format = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(format)
        {
            BufferLength = Math.Max(samples.Length * 2 + 1024, sampleRate * 2),
            DiscardOnBufferOverflow = true
        };

        float vol = Math.Clamp(volume01, 0f, 1f);
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float clipped = Math.Clamp(samples[i] * vol, -1f, 1f);
            short s = (short)(clipped * 32767);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        _buffer.AddSamples(bytes, 0, bytes.Length);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (devices.Count > 0)
            {
                int idx = deviceNumber < 0 ? 0 : Math.Min(deviceNumber, devices.Count - 1);
                var wasapi = new WasapiOut(devices[idx], AudioClientShareMode.Shared, true, 50);
                wasapi.Init(_buffer);
                _out = wasapi;
                wasapi.Play();
                return;
            }
        }
        catch
        {
            // fall back to WaveOut
        }

        var waveOut = new WaveOutEvent
        {
            DeviceNumber = deviceNumber < 0 ? 0 : deviceNumber,
            Volume = 1f
        };
        waveOut.Init(_buffer);
        _out = waveOut;
        waveOut.Play();
    }

    public void Stop()
    {
        if (_out is null) return;
        try { _out.Stop(); } catch { /* ignore */ }
        try { _out.Dispose(); } catch { /* ignore */ }
        _out = null;
        _buffer = null;
    }

    public void Dispose() => Stop();
}
