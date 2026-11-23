using System.IO;
using System.Media;
using System.Reflection;
using NAudio.Wave;

namespace SignalDx.Audio;

/// <summary>Plays a short alert sound (call-me or Wanted) on the default Windows audio device.</summary>
public sealed class CallAlertService : IDisposable
{
    readonly string _defaultFileName;
    readonly object _gate = new();
    SoundPlayer? _player;
    string? _loadedPath;
    WaveOutEvent? _waveOut;
    AudioFileReader? _reader;
    DateTime _lastPlayUtc = DateTime.MinValue;
    bool _disposed;

    public CallAlertService(string defaultFileName = "call-alert.wav")
    {
        _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "call-alert.wav" : defaultFileName;
    }

    public void Play(string? customPath = null, bool bypassCooldown = false)
    {
        if (_disposed) return;

        // Avoid stacking alerts if several stations trigger in the same slot.
        var now = DateTime.UtcNow;
        if (!bypassCooldown && (now - _lastPlayUtc).TotalMilliseconds < 400)
            return;
        _lastPlayUtc = now;

        try
        {
            string path = ResolvePath(customPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            bool isWav = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            if (isWav && TryPlayWithSoundPlayer(path))
                return;

            PlayWithNAudio(path);
        }
        catch
        {
            // Alert must never break decoding.
        }
    }

    bool TryPlayWithSoundPlayer(string path)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return true;
                StopNAudioLocked();

                if (_player is null || !string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _player?.Dispose();
                    _player = new SoundPlayer(path);
                    _player.Load();
                    _loadedPath = path;
                }

                _player.Play(); // async — does not block decode UI
                return true;
            }
        }
        catch
        {
            // Fall back to NAudio for non-PCM/compressed WAV files.
            return false;
        }
    }

    void PlayWithNAudio(string path)
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopNAudioLocked();

            var reader = new AudioFileReader(path);
            var waveOut = new WaveOutEvent { DeviceNumber = -1 };
            waveOut.Init(reader);
            waveOut.PlaybackStopped += (_, _) =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_waveOut, waveOut))
                    {
                        _waveOut = null;
                        _reader = null;
                    }
                }
                try { waveOut.Dispose(); } catch { /* ignore */ }
                try { reader.Dispose(); } catch { /* ignore */ }
            };
            _reader = reader;
            _waveOut = waveOut;
            waveOut.Play();
        }
    }

    void StopNAudioLocked()
    {
        var wo = _waveOut;
        var rd = _reader;
        _waveOut = null;
        _reader = null;
        try { wo?.Stop(); } catch { /* ignore */ }
        try { wo?.Dispose(); } catch { /* ignore */ }
        try { rd?.Dispose(); } catch { /* ignore */ }
    }

    string ResolvePath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        string besideExe = Path.Combine(AppContext.BaseDirectory, "Assets", _defaultFileName);
        if (File.Exists(besideExe))
            return besideExe;

        // Dev / alternate layout
        string? asm = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asm))
        {
            string alt = Path.Combine(asm, "Assets", _defaultFileName);
            if (File.Exists(alt))
                return alt;
        }

        return besideExe;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopNAudioLocked();
            _player?.Dispose();
            _player = null;
        }
    }
}
