namespace SignalDx.Services;

/// <summary>
/// Locates tools shipped under <c>{app}\Tools\</c> by the Windows installer
/// (JTDX decoder, WSJT-X ft8code, Hamlib rigctld). HRD is not bundled.
/// </summary>
public static class BundledTools
{
    public static string Root =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Tools"));

    public static string JtdxDir => Path.Combine(Root, "JTDX");
    public static string WsjtDir => Path.Combine(Root, "WSJT");
    public static string HamlibDir => Path.Combine(Root, "Hamlib");

    public static string? JtdxJt9Path
    {
        get
        {
            string p = Path.Combine(JtdxDir, "jtdxjt9.exe");
            return File.Exists(p) ? p : null;
        }
    }

    public static string? Ft8CodePath
    {
        get
        {
            string p = Path.Combine(WsjtDir, "ft8code.exe");
            return File.Exists(p) ? p : null;
        }
    }

    public static string? Ft4CodePath
    {
        get
        {
            string p = Path.Combine(WsjtDir, "ft4code.exe");
            return File.Exists(p) ? p : null;
        }
    }

    public static string? RigctldPath
    {
        get
        {
            string p = Path.Combine(HamlibDir, "rigctld.exe");
            return File.Exists(p) ? p : null;
        }
    }

    public static string? RigctlPath
    {
        get
        {
            string p = Path.Combine(HamlibDir, "rigctl.exe");
            return File.Exists(p) ? p : null;
        }
    }
}
