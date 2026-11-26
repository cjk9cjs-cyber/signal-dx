using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SignalDx.Audio;
using SignalDx.Geo;
using SignalDx.Models;

namespace SignalDx.Ft8;

/// <summary>
/// Runs the JTDX <c>jtdxjt9</c> decoder on a 12 kHz WAV slot and parses stdout into <see cref="DecodeHit"/>.
/// </summary>
public sealed class JtdxDecoderService : IDisposable
{
    static readonly Regex DecodeLine = new(
        // HHMMSS  SNR  DT  Hz  ~  message  [*]
        @"^\s*(?:(\d{6})\s+)?(-?\d+(?:\.\d+)?)\s+([+-]?\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+(?:[~#@$&*A-Za-z]\s+)?(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    readonly HashSet<string> _heard = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _gridByCall = new(StringComparer.OrdinalIgnoreCase);
    readonly object _procGate = new();
    Process? _running;
    CancellationTokenSource? _cts;
    int _busy;

    public bool IsBusy => Interlocked.CompareExchange(ref _busy, 0, 0) != 0;

    public bool IsAvailable(string? jt9Path) =>
        !string.IsNullOrWhiteSpace(jt9Path) && File.Exists(jt9Path);

    public string? LastStdoutPath => Path.Combine(Wav12kWriter.DecodeDirectory, "last-stdout.txt");
    public string? LastWavPath => Path.Combine(Wav12kWriter.DecodeDirectory, "last-slot.wav");

    public async Task<IReadOnlyList<DecodeHit>> DecodeSlotAsync(
        float[] samples48k,
        StationSettings station,
        DateTime slotUtc,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Decoder busy");

        try
        {
            return await DecodeSlotCoreAsync(samples48k, station, slotUtc, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    async Task<IReadOnlyList<DecodeHit>> DecodeSlotCoreAsync(
        float[] samples48k,
        StationSettings station,
        DateTime slotUtc,
        CancellationToken cancellationToken)
    {
        string? exe = string.IsNullOrWhiteSpace(station.JtdxJt9Path)
            ? JtdxPaths.FindJt9()
            : station.JtdxJt9Path;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new FileNotFoundException("jtdxjt9.exe not found. Install JTDX or set the decoder path in Settings.");

        string mode = string.IsNullOrWhiteSpace(station.Mode) ? "FT8" : station.Mode.Trim().ToUpperInvariant();
        bool ft4 = mode.Equals("FT4", StringComparison.OrdinalIgnoreCase);
        int depth = Math.Clamp(station.JtdxDecodeDepth, 1, 3);

        // WSJT/JTDX expect …/yyMMdd_HHmmss.wav so nutc parses from the filename.
        string wavName = $"{slotUtc:yyMMdd_HHmmss}.wav";
        string wav = Wav12kWriter.WriteSlotWav(samples48k, wavName);
        string workDir = Path.GetDirectoryName(exe)!;
        string dataDir = Wav12kWriter.DecodeDirectory;
        Directory.CreateDirectory(dataDir);

        // Keep a stable copy for troubleshooting (overwrite each slot).
        try { File.Copy(wav, Path.Combine(dataDir, "last-slot.wav"), overwrite: true); }
        catch { /* ignore */ }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(ft4 ? "-4" : "-8");
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(depth.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(dataDir);
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add(dataDir);
        psi.ArgumentList.Add(wav);

        var proc = new Process { StartInfo = psi };
        lock (_procGate) _running = proc;

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start jtdxjt9.");

            // Read streams to completion — BeginOutputReadLine + WaitForExit races and can drop decode lines.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            double slotSec = ft4 ? 7.5 : 15.0;
            int timeoutMs = (int)((slotSec + 20) * 1000);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                if (ct.IsCancellationRequested)
                    throw;
                throw new TimeoutException("jtdxjt9 decode timed out.");
            }

            string text = await stdoutTask.ConfigureAwait(false);
            string err = await stderrTask.ConfigureAwait(false);

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dataDir, "last-stdout.txt"),
                    text + (string.IsNullOrEmpty(err) ? "" : "\n--- stderr ---\n" + err),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* ignore */ }

            if (!string.IsNullOrEmpty(err) &&
                err.Contains("unrecognised option", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This jtdxjt9 build does not support FT8/FT4 file flags (-8/-4). " +
                    "Use a JTDX contest (or newer) build, e.g. C:\\JTDX_contest\\bin\\jtdxjt9.exe.");
            }

            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(err))
                text = err;

            return ParseOutput(text, station, slotUtc, mode);
        }
        finally
        {
            lock (_procGate)
            {
                if (ReferenceEquals(_running, proc))
                    _running = null;
            }
            try { proc.Dispose(); } catch { /* ignore */ }
            TryDelete(wav);
        }
    }

    public IReadOnlyList<DecodeHit> ParseOutput(string stdout, StationSettings station, DateTime slotUtc, string mode)
    {
        var hits = new List<DecodeHit>();
        foreach (var raw in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('<') || line.Contains("<DecodeFinished>", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("--- stderr", StringComparison.OrdinalIgnoreCase))
                break;

            var hit = TryParseLine(line, station, slotUtc, mode);
            if (hit is not null)
                hits.Add(hit);
        }

        return hits.OrderBy(h => h.Hz).ToList();
    }

    DecodeHit? TryParseLine(string line, StationSettings station, DateTime slotUtc, string mode)
    {
        var m = DecodeLine.Match(line);
        if (!m.Success) return null;

        string message = m.Groups[5].Value.Trim().TrimEnd('*').Trim();
        if (string.IsNullOrWhiteSpace(message) || message.StartsWith('<'))
            return null;

        if (message.Contains("pass", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("thr", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double snr))
            return null;
        if (!double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dt))
            return null;
        if (!double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hzF))
            return null;

        DateTime utc = slotUtc;
        if (m.Groups[1].Success && m.Groups[1].Value.Length == 6 &&
            int.TryParse(m.Groups[1].Value.AsSpan(0, 2), out int hh) &&
            int.TryParse(m.Groups[1].Value.AsSpan(2, 2), out int mm) &&
            int.TryParse(m.Groups[1].Value.AsSpan(4, 2), out int ss) &&
            hh is >= 0 and <= 23 && mm is >= 0 and <= 59 && ss is >= 0 and <= 59)
        {
            utc = new DateTime(slotUtc.Year, slotUtc.Month, slotUtc.Day, hh, mm, ss, DateTimeKind.Utc);
        }

        var parsed = Ft8MessageBuilder.Parse(message);
        // DxCall = transmitting / calling station (not the called party).
        string? dxCall = parsed.DxCall;
        string? dxGrid = parsed.DxGrid;
        if (!string.IsNullOrWhiteSpace(dxCall))
        {
            if (!string.IsNullOrWhiteSpace(dxGrid))
                _gridByCall[dxCall] = dxGrid;
            else if (_gridByCall.TryGetValue(dxCall, out string? knownGrid))
                dxGrid = knownGrid;
        }
        bool isCq = parsed.IsCq;
        bool isNew = !string.IsNullOrWhiteSpace(dxCall) && _heard.Add(dxCall);
        string my = (station.MyCall ?? "").Trim();
        bool isToMe = !string.IsNullOrEmpty(my) &&
                      message.StartsWith(my + " ", StringComparison.OrdinalIgnoreCase);
        string? country = CallsignCountry.FromCall(dxCall);
        string? state = UsState.Resolve(country, dxGrid, dxCall);
        double? miles = Maidenhead.DistanceMiles(station.MyGrid, dxGrid);

        return new DecodeHit
        {
            Utc = utc,
            Mode = mode,
            Hz = (int)Math.Round(hzF),
            SnrDb = Math.Round(snr, 1),
            Dt = Math.Round(dt, 2),
            Message = message,
            DxCall = dxCall,
            DxGrid = dxGrid,
            Country = country,
            State = state,
            IsNew = isNew,
            IsCq = isCq,
            IsToMe = isToMe,
            DistanceMiles = miles is double mi ? Math.Round(mi) : null
        };
    }

    public void CancelInFlight()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        Process? p;
        lock (_procGate) p = _running;
        TryKill(p);
    }

    static void TryKill(Process? proc)
    {
        if (proc is null) return;
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // leave for next cleanup
        }
    }

    public void Dispose() => CancelInFlight();
}
