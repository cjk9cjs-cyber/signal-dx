using System.Runtime.InteropServices;

namespace SignalDx.Ft8;

/// <summary>Locates WSJT-X <c>ft8code.exe</c> / <c>ft4code.exe</c> for real channel encoding.</summary>
public static class Ft8CodePaths
{
    public static string? FindFt8Code(string? preferred = null) =>
        FindTool("ft8code.exe", preferred);

    public static string? FindFt4Code(string? preferred = null) =>
        FindTool("ft4code.exe", preferred);

    static string? FindTool(string exeName, string? preferred)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            if (File.Exists(preferred) &&
                preferred.EndsWith(exeName, StringComparison.OrdinalIgnoreCase))
                return preferred;
            string? dir = Directory.Exists(preferred) ? preferred : Path.GetDirectoryName(preferred);
            if (!string.IsNullOrWhiteSpace(dir))
                candidates.Add(Path.Combine(dir, exeName));
        }

        candidates.Add(@"C:\WSJT\wsjtx\bin\" + exeName);
        candidates.Add(@"C:\WS\3.2.0\bin\" + exeName);
        candidates.Add(@"C:\WSJT-X\bin\" + exeName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         @"C:\"
                     })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "WSJT*", SearchOption.TopDirectoryOnly))
                    {
                        candidates.Add(Path.Combine(dir, "bin", exeName));
                        candidates.Add(Path.Combine(dir, "wsjtx", "bin", exeName));
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }
}
