using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SignalDx.Ft8;

/// <summary>
/// Encodes FT8/FT4 free-text into the 79 (or FT4) channel symbols using WSJT-X
/// <c>ft8code</c>/<c>ft4code</c>. Fake hash-based tones will not be decoded by other stations.
/// </summary>
public static class Ft8CodeEncoder
{
    static readonly Regex ChannelDigits = new(@"[0-7]+", RegexOptions.Compiled);

    public static int[] EncodeSymbols(string message, string mode = "FT8", string? codeExeHint = null)
    {
        string msg = (message ?? "").Trim();
        if (string.IsNullOrEmpty(msg))
            throw new InvalidOperationException("Empty TX message — nothing to encode.");

        bool ft4 = mode.Equals("FT4", StringComparison.OrdinalIgnoreCase);
        string? exe = ft4
            ? Ft8CodePaths.FindFt4Code(codeExeHint) ?? Ft8CodePaths.FindFt8Code(codeExeHint)
            : Ft8CodePaths.FindFt8Code(codeExeHint);

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException(
                "ft8code.exe not found. Install WSJT-X (expected under C:\\WSJT\\wsjtx\\bin) so Signal DX can encode real FT8.");

        // FT4 uses ft4code when present; otherwise fall back is wrong — require matching tool.
        if (ft4 && !Path.GetFileName(exe).Equals("ft4code.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? ft4Exe = Ft8CodePaths.FindFt4Code(codeExeHint);
            if (ft4Exe is null)
                throw new InvalidOperationException(
                    "ft4code.exe not found beside WSJT-X. Install a WSJT-X build that includes FT4 tools.");
            exe = ft4Exe;
        }

        string stdout = RunCode(exe, msg);
        int[] symbols = ParseChannelSymbols(stdout);
        int expected = ft4 ? 105 : Ft8Timing.SymbolCount;
        if (symbols.Length != expected)
            throw new InvalidOperationException(
                $"Encoder returned {symbols.Length} symbols (expected {expected}) for \"{msg}\".");

        return symbols;
    }

    static string RunCode(string exe, string message)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + message.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            }
        };

        if (!proc.Start())
            throw new InvalidOperationException("Failed to start " + Path.GetFileName(exe));

        // Avoid deadlock: read both streams.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(8000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new InvalidOperationException(Path.GetFileName(exe) + " timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(
                $"{Path.GetFileName(exe)} failed (exit {proc.ExitCode}): {stderr}".Trim());

        return stdout;
    }

    /// <summary>Parse the "Channel symbols (N tones):" digit groups from ft8code output.</summary>
    public static int[] ParseChannelSymbols(string stdout)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("Channel symbols", StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
            throw new InvalidOperationException("ft8code output missing 'Channel symbols' section.");

        var digits = new StringBuilder();
        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (digits.Length > 0) break;
                continue;
            }
            // Stop if we leave the symbol block (e.g. next section header).
            if (line.Contains("bits:", StringComparison.OrdinalIgnoreCase) && digits.Length > 40)
                break;

            foreach (Match m in ChannelDigits.Matches(line))
                digits.Append(m.Value);
        }

        if (digits.Length == 0)
            throw new InvalidOperationException("Could not parse channel symbols from ft8code output.");

        var symbols = new int[digits.Length];
        for (int i = 0; i < digits.Length; i++)
            symbols[i] = digits[i] - '0';
        return symbols;
    }
}
