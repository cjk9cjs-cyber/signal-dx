using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SignalDx.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    IWavePlayer? _out;
    DriveSampleProvider? _drive;
    MMDeviceEnumerator? _enumerator;
    MMDevice? _device;
    float _gain = 1f;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public string? ActiveDeviceName { get; private set; }

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

    public static IReadOnlyList<(int Index, string Name, string Id)> ListDevices()
    {
        var list = new List<(int, string, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int i = 0; i < devices.Count; i++)
                list.Add((i, devices[i].FriendlyName, devices[i].ID));
        }
        catch
        {
            // empty
        }
        return list;
    }

    /// <param name="volume01">Initial 0–1 TX drive (ALC audio level); change later via <see cref="Gain"/>.</param>
    /// <param name="preferredName">When set, pick this WASAPI render device by friendly name.</param>
    public void PlayMono(
        float[] samples,
        int sampleRate = AudioCaptureService.SampleRate,
        int deviceNumber = -1,
        float volume01 = 1f,
        string? preferredName = null)
    {
        Stop();
        if (samples is null || samples.Length == 0)
            throw new InvalidOperationException("TX audio buffer is empty.");

        _gain = Math.Clamp(volume01, 0f, 1f);

        var source = new BufferedFloatSampleProvider(samples, sampleRate);
        _drive = new DriveSampleProvider(source) { Gain = _gain };
        var wave = _drive.ToWaveProvider16();

        _enumerator = new MMDeviceEnumerator();
        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        if (devices.Count == 0)
            throw new InvalidOperationException("No WASAPI playback devices found.");

        int idx = ResolveDeviceIndex(devices, preferredName, deviceNumber);
        _device = devices[idx];
        ActiveDeviceName = _device.FriendlyName;

        // Keep MMDevice + enumerator alive for the whole TX — disposing them after Play()
        // (previous bug) can mute or tear down the stream while PTT is still keyed.
        var wasapi = new WasapiOut(_device, AudioClientShareMode.Shared, true, 100);
        wasapi.Init(wave);
        _out = wasapi;
        wasapi.Play();

        if (wasapi.PlaybackState != PlaybackState.Playing)
            throw new InvalidOperationException($"WASAPI failed to start on '{ActiveDeviceName}'.");
    }

    static int ResolveDeviceIndex(MMDeviceCollection devices, string? preferredName, int deviceNumber)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].FriendlyName.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].FriendlyName.Contains(preferredName, StringComparison.OrdinalIgnoreCase) ||
                    preferredName.Contains(devices[i].FriendlyName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        if (deviceNumber < 0) return 0;
        return Math.Min(deviceNumber, devices.Count - 1);
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { /* ignore */ }
        try { _out?.Dispose(); } catch { /* ignore */ }
        _out = null;
        _drive = null;

        // Device/enumerator after WasapiOut dispose.
        try { _device?.Dispose(); } catch { /* ignore */ }
        _device = null;
        try { _enumerator?.Dispose(); } catch { /* ignore */ }
        _enumerator = null;
        ActiveDeviceName = null;
    }

    public void Dispose() => Stop();

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

        int _silencePads;

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _samples.Length - _pos;
            if (available <= 0)
            {
                // Brief silence pad so the last WASAPI period drains, then stop (return 0).
                if (_silencePads++ < 8)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                return 0;
            }

            int n = Math.Min(count, available);
            Array.Copy(_samples, _pos, buffer, offset, n);
            _pos += n;
            if (n < count)
            {
                Array.Clear(buffer, offset + n, count - n);
                return count;
            }

            return n;
        }
    }

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
