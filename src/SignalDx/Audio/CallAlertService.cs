using System.IO;
using System.Media;
using System.Reflection;

namespace SignalDx.Audio;

/// <summary>Plays a short WAV when a station calls our callsign.</summary>
public sealed class CallAlertService : IDisposable
{
    static readonly string DefaultFileName = "call-alert.wav";
    SoundPlayer? _player;
    string? _loadedPath;
    DateTime _lastPlayUtc = DateTime.MinValue;
    bool _disposed;

    public void Play(string? customPath = null)
    {
        if (_disposed) return;

        // Avoid stacking alerts if several stations call in the same slot.
        // Short enough that a Wanted alert can still follow a call-me alert.
        var now = DateTime.UtcNow;
        if ((now - _lastPlayUtc).TotalMilliseconds < 400)
            return;
        _lastPlayUtc = now;

        try
        {
            string path = ResolvePath(customPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            if (_player is null || !string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _player?.Dispose();
                _player = new SoundPlayer(path);
                _player.Load();
                _loadedPath = path;
            }

            _player.Play(); // async — does not block decode UI
        }
        catch
        {
            // Alert must never break decoding.
        }
    }

    static string ResolvePath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        string besideExe = Path.Combine(AppContext.BaseDirectory, "Assets", DefaultFileName);
        if (File.Exists(besideExe))
            return besideExe;

        // Dev / alternate layout
        string? asm = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asm))
        {
            string alt = Path.Combine(asm, "Assets", DefaultFileName);
            if (File.Exists(alt))
                return alt;
        }

        return besideExe;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player?.Dispose();
        _player = null;
    }
}
