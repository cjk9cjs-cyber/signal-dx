using System.Diagnostics;

namespace SignalDx.Cat;

public sealed class HamlibRigctldLauncher
{
    Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;
    public string? ExecutablePath { get; private set; }

    public static string? FindRigctld(string? hintPath = null) => HamlibPaths.FindRigctld(hintPath);

    public (bool Ok, string Message) Start(int modelId, string device, int baud, int tcpPort, string? exePath = null)
    {
        Stop();
        // Prefer explicit path; otherwise search PATH + hamlib-* install folders.
        string? exe = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath)
            ? exePath
            : FindRigctld(exePath);
        if (exe is null)
            exe = FindRigctld();
        if (exe is null || !File.Exists(exe))
            return (false, "rigctld not found. Install Hamlib (e.g. hamlib-w32) or set the rigctld path in Settings.");

        // Ensure device is the bare COM name when a friendly label was chosen.
        string rigDevice = ExtractPort(device);
        ExecutablePath = exe;
        string args = $"-m {modelId} -r \"{rigDevice}\" -s {baud} -t {tcpPort}";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };

            // Hamlib DLLs live next to the exe — put that folder first on PATH for the child.
            string? bin = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(bin))
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.Environment["PATH"] = bin + Path.PathSeparator + path;
            }

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!_process.Start())
                return (false, "Failed to start rigctld.");

            Thread.Sleep(500);
            if (_process.HasExited)
            {
                string err = "";
                try { err = _process.StandardError.ReadToEnd(); } catch { /* ignore */ }
                return (false, string.IsNullOrWhiteSpace(err)
                    ? $"rigctld exited immediately (code {_process.ExitCode})."
                    : err.Trim());
            }

            return (true, $"rigctld started (pid {_process.Id}): {args}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch { /* ignore */ }
        try { _process.Dispose(); } catch { /* ignore */ }
        _process = null;
    }

    public static string ExtractPort(string deviceOrDisplay)
    {
        if (string.IsNullOrWhiteSpace(deviceOrDisplay)) return "COM1";
        string s = deviceOrDisplay.Trim();
        int sep = s.IndexOf('—');
        if (sep < 0) sep = s.IndexOf('-');
        // Only split on em-dash style friendly labels: "COM7 — Silicon Labs..."
        int em = s.IndexOf(" — ", StringComparison.Ordinal);
        if (em > 0) return s[..em].Trim();
        // "COM7 - something"
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(COM\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        return s;
    }
}
