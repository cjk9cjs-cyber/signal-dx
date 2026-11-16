namespace SignalDx.Band;

public sealed record BandPreset(string Band, string Mode, double DialMhz, bool Primary = true);

/// <summary>WSJT-X / JTDX conventional USB dial frequencies for FT8 and FT4.</summary>
public static class BandPlan
{
    public static IReadOnlyList<BandPreset> Presets { get; } =
    [
        // FT8 — primary watering holes
        new("160m", "FT8", 1.840),
        new("80m", "FT8", 3.573),
        new("60m", "FT8", 5.357),
        new("40m", "FT8", 7.074),
        new("30m", "FT8", 10.136),
        new("20m", "FT8", 14.074),
        new("17m", "FT8", 18.100),
        new("15m", "FT8", 21.074),
        new("12m", "FT8", 24.915),
        new("10m", "FT8", 28.074),
        new("6m", "FT8", 50.313),
        new("4m", "FT8", 70.154),
        new("2m", "FT8", 144.174),
        new("70cm", "FT8", 432.174),

        // FT8 — secondary / overflow (WSJT-X optional defaults)
        new("40m", "FT8", 7.071, Primary: false),
        new("40m", "FT8", 7.056, Primary: false),
        new("30m", "FT8", 10.133, Primary: false),
        new("20m", "FT8", 14.071, Primary: false),
        new("20m", "FT8", 14.090, Primary: false),
        new("6m", "FT8", 50.310, Primary: false),
        new("6m", "FT8", 50.323, Primary: false),

        // FT4
        new("80m", "FT4", 3.575),
        new("40m", "FT4", 7.0475),
        new("30m", "FT4", 10.140),
        new("20m", "FT4", 14.080),
        new("17m", "FT4", 18.104),
        new("15m", "FT4", 21.140),
        new("12m", "FT4", 24.919),
        new("10m", "FT4", 28.180),
        new("6m", "FT4", 50.318),
        new("2m", "FT4", 144.170),
        new("70cm", "FT4", 432.170),
    ];

    public static BandPreset? Find(string band, string mode) =>
        Presets.FirstOrDefault(p =>
            p.Primary &&
            p.Band.Equals(band, StringComparison.OrdinalIgnoreCase) &&
            p.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<BandPreset> ForMode(string mode)
    {
        string m = string.IsNullOrWhiteSpace(mode) ? "FT8" : mode.Trim();
        return Presets.Where(p => p.Mode.Equals(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>ADIF-style band name from dial MHz (USB dial, not RF+audio).</summary>
    public static string BandFromMhz(double mhz)
    {
        // Wavelength-ish ADIF bands used by WSJT-X / LoTW.
        if (mhz is >= 0.135 and < 0.140) return "2190m";
        if (mhz is >= 0.472 and < 0.480) return "630m";
        if (mhz is >= 1.8 and < 2.0) return "160m";
        if (mhz is >= 3.5 and < 4.0) return "80m";
        if (mhz is >= 5.0 and < 5.5) return "60m";
        if (mhz is >= 7.0 and < 7.3) return "40m";
        if (mhz is >= 10.1 and < 10.15) return "30m";
        if (mhz is >= 14.0 and < 14.35) return "20m";
        if (mhz is >= 18.068 and < 18.168) return "17m";
        if (mhz is >= 21.0 and < 21.45) return "15m";
        if (mhz is >= 24.89 and < 24.99) return "12m";
        if (mhz is >= 28.0 and < 29.7) return "10m";
        if (mhz is >= 50.0 and < 54.0) return "6m";
        if (mhz is >= 70.0 and < 71.0) return "4m";
        if (mhz is >= 144.0 and < 148.0) return "2m";
        if (mhz is >= 222.0 and < 225.0) return "1.25m";
        if (mhz is >= 420.0 and < 450.0) return "70cm";
        if (mhz is >= 1240.0 and < 1300.0) return "23cm";
        return $"{mhz:0.###}";
    }

    public static string FormatPreset(BandPreset p)
    {
        string tag = p.Primary ? "" : " *";
        return $"{p.Band} {p.Mode}  {p.DialMhz:0.0000}{tag}";
    }
}
