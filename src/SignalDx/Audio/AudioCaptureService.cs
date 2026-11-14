using NAudio.Wave;

namespace SignalDx.Audio;

public sealed class AudioCaptureService : IDisposable
{
    public const int SampleRate = 48000;

    WaveInEvent? _waveIn;
    readonly object _gate = new();
    float[] _latest = [];

    public event Action? FrameAvailable;

    public bool IsRunning => _waveIn is not null;
    public int DeviceNumber { get; private set; } = -1;

    public static IReadOnlyList<(int Index, string Name)> ListDevices()
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            list.Add((i, caps.ProductName));
        }
        return list;
    }

    public void Start(int deviceNumber = -1)
    {
        Stop();
        DeviceNumber = deviceNumber;
        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber < 0 ? 0 : deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50
        };
        _waveIn.DataAvailable += OnData;
        _waveIn.StartRecording();
    }

    public void Stop()
    {
        if (_waveIn is null) return;
        _waveIn.DataAvailable -= OnData;
        try { _waveIn.StopRecording(); } catch { /* ignore */ }
        _waveIn.Dispose();
        _waveIn = null;
    }

    public float[] GetLatestFrame()
    {
        lock (_gate) return (float[])_latest.Clone();
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        int samples = e.BytesRecorded / 2;
        var frame = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
            frame[i] = s / 32768f;
        }

        lock (_gate) _latest = frame;
        FrameAvailable?.Invoke();
    }

    public void Dispose() => Stop();
}
