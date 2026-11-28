using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SignalDx.Audio;

public sealed class AudioCaptureService : IDisposable
{
    public const int SampleRate = 48000;

    /// <summary>~130 s of mono float at 48 kHz: one full WSPR 2-minute slot plus margin. FT8/FT4 read only the tail.</summary>
    const int RingCapacity = SampleRate * 130;

    WasapiCapture? _wasapi;
    readonly object _gate = new();
    float[] _latest = [];
    readonly float[] _ring = new float[RingCapacity];
    int _ringWrite;
    int _ringCount;
    long _totalWritten;
    DateTime _anchorUtc = DateTime.UtcNow;
    long _anchorSample;
    int _sourceRate = SampleRate;
    int _sourceChannels = 1;
    bool _sourceFloat;

    // Resample scratch (source-rate mono → 48 kHz).
    readonly List<float> _monoPending = new(4096);
    double _resamplePos;

    public event Action? FrameAvailable;

    public bool IsRunning => _wasapi is not null;
    public int DeviceNumber { get; private set; } = -1;
    public int BufferedSamples
    {
        get { lock (_gate) return _ringCount; }
    }

    public double BufferedSeconds => BufferedSamples / (double)SampleRate;

    public static IReadOnlyList<(int Index, string Name, string Id)> ListDevices()
    {
        var list = new List<(int, string, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            for (int i = 0; i < devices.Count; i++)
            {
                string name = devices[i].FriendlyName;
                string id = devices[i].ID;
                list.Add((i, name, id));
            }
        }
        catch
        {
            // fall through empty
        }
        return list;
    }

    public void Start(int deviceNumber = -1, string? preferredName = null, string? preferredId = null)
    {
        Stop();
        DeviceNumber = deviceNumber;

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        if (devices.Count == 0)
            throw new InvalidOperationException("No WASAPI capture devices found.");

        int idx = -1;
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].ID.Equals(preferredId, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx < 0 && !string.IsNullOrWhiteSpace(preferredName))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].FriendlyName.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx < 0)
            idx = deviceNumber < 0 ? 0 : Math.Min(deviceNumber, devices.Count - 1);

        DeviceNumber = idx;
        var device = devices[idx];

        _wasapi = new WasapiCapture(device);
        var wf = _wasapi.WaveFormat;
        _sourceRate = wf.SampleRate;
        _sourceChannels = Math.Max(1, wf.Channels);
        // WASAPI usually delivers IEEE float; fall back to PCM16 layout otherwise.
        _sourceFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat
                       || wf.BitsPerSample == 32;

        lock (_gate)
        {
            Array.Clear(_ring);
            _ringWrite = 0;
            _ringCount = 0;
            _totalWritten = 0;
            _anchorUtc = DateTime.UtcNow;
            _anchorSample = 0;
            _latest = [];
            _monoPending.Clear();
            _resamplePos = 0;
        }

        _wasapi.DataAvailable += OnData;
        _wasapi.RecordingStopped += (_, _) => { /* ignore */ };
        _wasapi.StartRecording();
    }

    public void Stop()
    {
        if (_wasapi is null) return;
        _wasapi.DataAvailable -= OnData;
        try { _wasapi.StopRecording(); } catch { /* ignore */ }
        try { _wasapi.Dispose(); } catch { /* ignore */ }
        _wasapi = null;
    }

    public float[] GetLatestFrame()
    {
        lock (_gate) return (float[])_latest.Clone();
    }

    /// <summary>Copy the most recent <paramref name="seconds"/> of buffered audio (48 kHz mono).</summary>
    public float[] CopyRecent(double seconds)
    {
        if (seconds <= 0) return [];
        int want = (int)Math.Round(seconds * SampleRate);
        lock (_gate)
        {
            int n = Math.Min(want, _ringCount);
            if (n <= 0) return [];
            var dst = new float[n];
            int start = (_ringWrite - n + RingCapacity) % RingCapacity;
            CopyFromRing(start, dst, n);
            return dst;
        }
    }

    /// <summary>Copy samples from <paramref name="utcStart"/> through now (clamped to ring).</summary>
    public float[] CopySince(DateTime utcStart)
    {
        lock (_gate)
        {
            long oldest = _totalWritten - _ringCount;
            long startSample = _anchorSample + (long)Math.Round((utcStart - _anchorUtc).TotalSeconds * SampleRate);
            if (startSample < oldest) startSample = oldest;
            if (startSample >= _totalWritten) return [];

            int n = (int)(_totalWritten - startSample);
            if (n > _ringCount) n = _ringCount;
            if (n <= 0) return [];

            var dst = new float[n];
            int fromOldest = (int)(startSample - oldest);
            int start = (_ringWrite - _ringCount + fromOldest + RingCapacity) % RingCapacity;
            CopyFromRing(start, dst, n);
            return dst;
        }
    }

    void CopyFromRing(int start, float[] dst, int n)
    {
        int first = Math.Min(n, RingCapacity - start);
        Array.Copy(_ring, start, dst, 0, first);
        if (first < n)
            Array.Copy(_ring, 0, dst, first, n - first);
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        // Decode to mono float at device rate, then resample into 48 kHz ring.
        int bytesPerSample = _sourceFloat ? 4 : 2;
        int blockAlign = Math.Max(1, bytesPerSample * _sourceChannels);
        int frames = e.BytesRecorded / blockAlign;
        if (frames <= 0) return;

        var mono = new float[frames];
        var buf = e.Buffer;
        for (int i = 0; i < frames; i++)
        {
            int off = i * blockAlign;
            float sum = 0;
            for (int ch = 0; ch < _sourceChannels; ch++)
            {
                int o = off + ch * bytesPerSample;
                if (_sourceFloat)
                {
                    sum += BitConverter.ToSingle(buf, o);
                }
                else
                {
                    short s = (short)(buf[o] | (buf[o + 1] << 8));
                    sum += s / 32768f;
                }
            }
            mono[i] = sum / _sourceChannels;
        }

        float[] at48k = ResampleTo48k(mono, _sourceRate);

        lock (_gate)
        {
            if (_totalWritten == 0)
            {
                _anchorUtc = DateTime.UtcNow;
                _anchorSample = 0;
            }

            for (int i = 0; i < at48k.Length; i++)
            {
                _ring[_ringWrite] = at48k[i];
                _ringWrite = (_ringWrite + 1) % RingCapacity;
            }
            _ringCount = Math.Min(RingCapacity, _ringCount + at48k.Length);
            _totalWritten += at48k.Length;
            _latest = at48k;
        }

        FrameAvailable?.Invoke();
    }

    float[] ResampleTo48k(float[] mono, int sourceRate)
    {
        if (sourceRate == SampleRate)
            return mono;
        if (sourceRate <= 0 || mono.Length == 0)
            return [];

        // Append to pending so fractional positions carry across callbacks.
        _monoPending.AddRange(mono);
        double step = (double)sourceRate / SampleRate;
        var list = new List<float>(mono.Length * SampleRate / sourceRate + 8);

        while (_resamplePos + 1 < _monoPending.Count)
        {
            int i0 = (int)_resamplePos;
            double frac = _resamplePos - i0;
            float a = _monoPending[i0];
            float b = _monoPending[i0 + 1];
            list.Add((float)(a + (b - a) * frac));
            _resamplePos += step;
        }

        int consumed = Math.Max(0, (int)_resamplePos);
        if (consumed > 0 && consumed < _monoPending.Count)
        {
            _monoPending.RemoveRange(0, consumed);
            _resamplePos -= consumed;
        }
        else if (consumed >= _monoPending.Count)
        {
            _monoPending.Clear();
            _resamplePos = 0;
        }

        // Cap pending growth if callbacks stall oddly.
        if (_monoPending.Count > sourceRate * 2)
        {
            int keep = sourceRate / 2;
            _monoPending.RemoveRange(0, _monoPending.Count - keep);
            _resamplePos = Math.Min(_resamplePos, _monoPending.Count - 1);
        }

        return list.ToArray();
    }

    public void Dispose() => Stop();
}
