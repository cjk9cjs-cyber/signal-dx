using SignalDx.Services;

namespace SignalDx.Wspr;

public static class WsprdPaths
{
    public static string? Find()
    {
        var candidates = new List<string>();
        if (BundledTools.WsprdPath is string bundled)
            candidates.Add(bundled);

        candidates.Add(@"C:\WSJT\wsjtx\bin\wsprd.exe");
        candidates.Add(@"C:\WSJT-X\bin\wsprd.exe");
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WSJT", "wsjtx", "bin", "wsprd.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WSJT", "wsjtx", "bin", "wsprd.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }
}
