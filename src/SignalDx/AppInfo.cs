using System.Reflection;

namespace SignalDx;

/// <summary>Static about / version metadata for Settings → About.</summary>
public static class AppInfo
{
    public const string ProductName = "Signal DX";
    public const string CreatorName = "CJ Schmit";
    public const string CreatorCall = "K9CJS";
    /// <summary>First commit / project start date (local).</summary>
    public static readonly DateOnly DateCreated = new(2026, 11, 14);

    public static string Version
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // Strip optional "+gitsha" suffix from some SDK layouts.
                int plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }

            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    public static string CreatorDisplay => $"{CreatorName}  ({CreatorCall})";

    public static string DateCreatedDisplay => DateCreated.ToString("MMMM d, yyyy");
}
