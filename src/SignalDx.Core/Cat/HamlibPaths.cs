using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SignalDx.Services;

namespace SignalDx.Cat;

public static class HamlibPaths
{
    public static string? FindRigctl(string? preferredDir = null) =>
        FindTool("rigctl.exe", "rigctl", preferredDir);

    public static string? FindRigctld(string? preferredDir = null) =>
        FindTool("rigctld.exe", "rigctld", preferredDir);

    public static string? FindInstallDirectory(string? hintPath = null)
    {
        if (!string.IsNullOrWhiteSpace(hintPath))
        {
            string? dir = Directory.Exists(hintPath) ? hintPath : Path.GetDirectoryName(hintPath);
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "rigctl.exe")))
                return dir;
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "rigctld.exe")))
                return dir;
        }

        string? rigctl = FindRigctl();
        return string.IsNullOrWhiteSpace(rigctl) ? null : Path.GetDirectoryName(rigctl);
    }

    static string? FindTool(string winName, string unixName, string? preferredDir)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredDir))
        {
            string dir = Directory.Exists(preferredDir) ? preferredDir : (Path.GetDirectoryName(preferredDir) ?? "");
            if (!string.IsNullOrWhiteSpace(dir))
            {
                candidates.Add(Path.Combine(dir, winName));
                candidates.Add(Path.Combine(dir, unixName));
            }
        }

        // Installer-bundled Hamlib (complete package). HRD remains a separate CAT option.
        candidates.Add(Path.Combine(BundledTools.HamlibDir, winName));

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = part.Trim().Trim('"');
                candidates.Add(Path.Combine(d, winName));
                candidates.Add(Path.Combine(d, unixName));
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         @"C:\",
                         @"C:\hamlib",
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                     })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                candidates.Add(Path.Combine(root, "hamlib", "bin", winName));
                candidates.Add(Path.Combine(root, "Hamlib", "bin", winName));
                candidates.Add(Path.Combine(root, "bin", winName));

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "hamlib*", SearchOption.TopDirectoryOnly))
                    {
                        candidates.Add(Path.Combine(dir, "bin", winName));
                        candidates.Add(Path.Combine(dir, winName));
                    }
                }
                catch
                {
                    // ignore permission issues
                }
            }

            candidates.Add(@"C:\ProgramData\chocolatey\bin\" + winName);
        }

        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    public static bool LooksLikeHamlibBin(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Regex.IsMatch(Path.GetFileName(path), @"^rigctl(d)?(\.exe)?$", RegexOptions.IgnoreCase);
}
