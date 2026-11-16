using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SignalDx.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    IWavePlayer? _out;
    DriveSampleProvider? _drive;
    float _gain = 1f;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    /// <summary>Live TX drive 0–1; takes effect immediately while playing.</summary>
    public float Gain
    {
        get => _gain;
        set
        {
            _gain = Math.Clamp(value, 0f, 1f);
            if (_drive is not null)
                _drive.Gain = _gain;
        }
    }

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

    /// <param name="volume01">Initial 0–1 TX drive (ALC audio level); change later via <see cref="Gain"/>.</param>
    public void PlayMono(float[] samples, int sampleRate = AudioCaptureService.SampleRate, int deviceNumber = -1,
        float volume01 = 1f)
    {
        Stop();
        _gain = Math.Clamp(volume01, 0f, 1f);

        // Stream samples with a live gain stage so the Drive slider works during TX.
        var source = new BufferedFloatSampleProvider(samples, sampleRate);
        _drive = new DriveSampleProvider(source) { Gain = _gain };
        var wave = _drive.ToWaveProvider16();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (devices.Count > 0)
            {
                int idx = deviceNumber < 0 ? 0 : Math.Min(deviceNumber, devices.Count - 1);
                var wasapi = new WasapiOut(devices[idx], AudioClientShareMode.Shared, true, 50);
                wasapi.Init(wave);
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
        waveOut.Init(wave);
        _out = waveOut;
        waveOut.Play();
    }

    public void Stop()
    {
        if (_out is null && _drive is null) return;
        try { _out?.Stop(); } catch { /* ignore */ }
        try { _out?.Dispose(); } catch { /* ignore */ }
        _out = null;
        _drive = null;
    }

    public void Dispose() => Stop();

    /// <summary>Feeds a pre-generated float buffer as an <see cref="ISampleProvider"/>.</summary>
    sealed class BufferedFloatSampleProvider : ISampleProvider
    {
        readonly float[] _samples;
        int _pos;

        public BufferedFloatSampleProvider(float[] samples, int sampleRate)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _samples.Length - _pos;
            if (available <= 0) return 0;
            int n = Math.Min(count, available);
            Array.Copy(_samples, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    /// <summary>Multiplies samples by a mutable gain (updated from the Drive slider).</summary>
    sealed class DriveSampleProvider : ISampleProvider
    {
        readonly ISampleProvider _source;

        public DriveSampleProvider(ISampleProvider source) => _source = source;

        public float Gain { get; set; } = 1f;
        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _source.Read(buffer, offset, count);
            float g = Gain;
            if (Math.Abs(g - 1f) < 0.0001f) return n;
            for (int i = 0; i < n; i++)
                buffer[offset + i] *= g;
            return n;
        }
    }
}
