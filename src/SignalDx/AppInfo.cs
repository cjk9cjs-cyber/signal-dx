using System.Reflection;

namespace SignalDx;

/// <summary>Static about / version metadata for Settings → About.</summary>
public static class AppInfo
{
    public const string ProductName = "Signal DX";
    public const string CreatorName = "CJ Schmit";
    public const string CreatorCall = "K9CJS";
    public const string CreatorEmail = "cj.k9cjs@gmail.com";
    /// <summary>First commit / project start date (local).</summary>
    public static readonly DateOnly DateCreated = new(2026, 11, 14);
    /// <summary>
    /// Date this assembly version was cut. Update with every version bump
    /// (see .cursor/rules/bump-version.mdc).
    /// </summary>
    public static readonly DateOnly VersionDate = new(2026, 11, 24); // 2.17.1

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

    public static string VersionDateDisplay => VersionDate.ToString("MMMM d, yyyy");

    public const string Tagline = "Windows FT8 / FT4 / WSPR-2 digi operator console.";

    public const string RepoUrl = "https://github.com/cjk9cjs-cyber/signal-dx";
    public const string ReadmeUrl = "https://github.com/cjk9cjs-cyber/signal-dx/blob/cursor/ft8-operator-console/README.md";
    public const string HowtoUrl = "https://github.com/cjk9cjs-cyber/signal-dx/blob/cursor/ft8-operator-console/docs/HOWTO.md";
    public const string HowtoPdfUrl = "https://github.com/cjk9cjs-cyber/signal-dx/releases/latest/download/SignalDx-HOWTO.pdf";
    public const string ReleasesUrl = "https://github.com/cjk9cjs-cyber/signal-dx/releases";

    /// <summary>Third-party components bundled or invoked at runtime.</summary>
    public const string Acknowledgments =
        "Signal DX uses parts of other open amateur-radio projects:\n\n" +
        "• JTDX Contest — FT8/FT4 decoder (jtdxjt9) by Arvo Järve, ES1JA, and contributors.\n" +
        "• WSJT-X — FT8/FT4 channel encoding (ft8code / ft4code) and the WSPR protocol by Joe Taylor, K1JT, " +
        "Steve Franke, K9AN, Bill Somerville, G4WJS, and the WSJT Development Group.\n" +
        "• WSPR Type-1 packing adapted from JTEncode / WsprSharp (Scott Harden, AJ4VD).\n\n" +
        "Those projects remain the property of their respective authors and are subject " +
        "to their own licenses. Signal DX is not affiliated with or endorsed by JTDX Contest or WSJT-X.";
}

