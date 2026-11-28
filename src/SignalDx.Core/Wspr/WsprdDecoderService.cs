using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SignalDx.Audio;
using SignalDx.Geo;
using SignalDx.Models;

namespace SignalDx.Wspr;

/// <summary>
/// Runs WSJT-X <c>wsprd</c> on a 120 s WSPR slot and turns its spot lines into <see cref="DecodeHit"/>s.
/// </summary>
public sealed class WsprdDecoderService : IDisposable
{
    static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(100);

    static readonly Regex SpotLine = new(
        // HHMM  SNR  DT  FreqMHz  Drift  CALL GRID DBM
        @"^\s*(\d{4})\s+(-?\d+)\s+(-?\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\s+(-?\d+)\s+(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex GridLike = new(
        @"^[A-R]{2}\d{2}(?:[A-X]{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    readonly HashSet<string> _heard = new(StringComparer.OrdinalIgnoreCase);
    readonly object _procGate = new();
    Process? _running;
    CancellationTokenSource? _cts;
    int _busy;

    public bool IsBusy => Interlocked.CompareExchange(ref _busy, 0, 0) != 0;

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
        string exe = WsprdPaths.Find()
            ?? throw new FileNotFoundException("wsprd.exe not found. Install WSJT-X to decode WSPR.");

        string dataDir = Path.Combine(Wav12kWriter.DecodeDirectory, "wsprd");
        Directory.CreateDirectory(dataDir);
        foreach (string stale in new[] { "ALL_WSPR.TXT", "wspr_spots.txt" })
            TryDelete(Path.Combine(dataDir, stale));

        // wsprd reads the spot date and time from the filename as yymmdd_hhmm.
        string wav = Wav12kWriter.WriteSlotWav(samples48k, $"{slotUtc:yyMMdd_HHmm}.wav");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = dataDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(dataDir);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(station.DialMhz.ToString("0.000000", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(wav);

        var proc = new Process { StartInfo = psi };
        lock (_procGate) _running = proc;

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start wsprd.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DecodeTimeout);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                if (ct.IsCancellationRequested)
                    throw;
                throw new TimeoutException("wsprd decode timed out.");
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

            return ParseOutput(text, station, slotUtc);
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

    public IReadOnlyList<DecodeHit> ParseOutput(string stdout, StationSettings station, DateTime slotUtc)
    {
        var hits = new List<DecodeHit>();
        foreach (var raw in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var hit = TryParseLine(raw.Trim(), station, slotUtc);
            if (hit is not null)
                hits.Add(hit);
        }

        return hits.OrderBy(h => h.Hz).ToList();
    }

    DecodeHit? TryParseLine(string line, StationSettings station, DateTime slotUtc)
    {
        var m = SpotLine.Match(line);
        if (!m.Success) return null;

        if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double snr))
            return null;
        if (!double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dt))
            return null;
        if (!double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double freqMhz))
            return null;

        // Type-1 spots only: CALL GRID DBM. Compound and hashed messages carry no grid and are skipped.
        string[] parts = m.Groups[6].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;
        string call = parts[0].ToUpperInvariant();
        string grid = parts[1].ToUpperInvariant();
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dbm))
            return null;
        if (!GridLike.IsMatch(grid))
            return null;

        int hz = (int)Math.Round((freqMhz - station.DialMhz) * 1_000_000.0);
        string? country = CallsignCountry.FromCall(call);
        string? state = UsState.Resolve(country, grid, call);
        double? miles = Maidenhead.DistanceMiles(station.MyGrid, grid);

        return new DecodeHit
        {
            Utc = slotUtc,
            Mode = "WSPR2",
            Hz = hz,
            SnrDb = Math.Round(snr, 1),
            Dt = Math.Round(dt, 2),
            Message = $"{call} {grid} {dbm}",
            DxCall = call,
            DxGrid = grid,
            Country = country,
            State = state,
            IsNew = _heard.Add(call),
            IsCq = false,
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
