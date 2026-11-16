using System.Runtime.InteropServices;

namespace SignalDx.Ft8;

public static class JtdxPaths
{
    public static string? FindJt9(string? preferred = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            if (File.Exists(preferred))
                return preferred;
            string? dir = Directory.Exists(preferred) ? preferred : Path.GetDirectoryName(preferred);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                candidates.Add(Path.Combine(dir, "jtdxjt9.exe"));
                candidates.Add(Path.Combine(dir, "jt9.exe"));
            }
        }

        // Prefer contest / builds that expose -8 / -4 FT8/FT4 file mode.
        candidates.Add(@"C:\JTDX_contest\bin\jtdxjt9.exe");
        candidates.Add(@"C:\JTDX64\159\bin\jtdxjt9.exe");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[]
                     {
                         @"C:\",
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                     })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "JTDX*", SearchOption.TopDirectoryOnly))
                    {
                        candidates.Add(Path.Combine(dir, "bin", "jtdxjt9.exe"));
                        candidates.Add(Path.Combine(dir, "jtdxjt9.exe"));
                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                                candidates.Add(Path.Combine(sub, "bin", "jtdxjt9.exe"));
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
                catch
                {
                    // ignore permission issues
                }
            }
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = part.Trim().Trim('"');
                candidates.Add(Path.Combine(d, "jtdxjt9.exe"));
                candidates.Add(Path.Combine(d, "jt9.exe"));
            }
        }

        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }
}
