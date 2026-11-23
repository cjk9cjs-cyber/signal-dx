using System.ComponentModel;
using System.Runtime.CompilerServices;
using SignalDx.Geo;

namespace SignalDx.Models;

public sealed class DecodeHit : INotifyPropertyChanged
{
    bool _isToMe;
    bool _isWorking;
    bool _isPreviouslyWorked;
    bool _isWanted;
    bool _isUnverified;

    public required DateTime Utc { get; init; }
    public required string Mode { get; init; }
    public required int Hz { get; init; }
    public required double SnrDb { get; init; }
    public required double Dt { get; init; }
    public required string Message { get; init; }
    public string? DxCall { get; init; }
    public string? DxGrid { get; init; }
    public string? Country { get; init; }
    public string? State { get; init; }
    public bool IsNew { get; init; }
    public bool IsCq { get; init; }

    /// <summary>Message is addressed to my call (someone calling / working me).</summary>
    public bool IsToMe
    {
        get => _isToMe;
        set => SetField(ref _isToMe, value);
    }

    /// <summary>Row involves the DX call currently being worked.</summary>
    public bool IsWorking
    {
        get => _isWorking;
        set => SetField(ref _isWorking, value);
    }

    /// <summary>Transmitter appears in the worked-calls history file (prior logged QSO).</summary>
    public bool IsPreviouslyWorked
    {
        get => _isPreviouslyWorked;
        set => SetField(ref _isPreviouslyWorked, value);
    }

    /// <summary>Transmitter matches the operator Wanted list (call / state / country).</summary>
    public bool IsWanted
    {
        get => _isWanted;
        set => SetField(ref _isWanted, value);
    }

    /// <summary>Transmitter is not on the bundled ALLCALL7 list (possible false decode). Display-only; never gates alerts.</summary>
    public bool IsUnverified
    {
        get => _isUnverified;
        set => SetField(ref _isUnverified, value);
    }

    public double? DistanceMiles { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>CQ or 73/RR73 — same band-activity background highlight.</summary>
    public bool IsCqStyleHighlight
    {
        get
        {
            if (IsCq) return true;
            string msg = Message ?? "";
            if (msg.StartsWith('→') || msg.StartsWith("->", StringComparison.Ordinal))
                msg = msg.TrimStart('→', '-', ' ').Trim();
            var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string last = parts[^1];
            return last.Equals("73", StringComparison.OrdinalIgnoreCase) ||
                   last.Equals("RR73", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string DistanceText =>
        DistanceMiles is double mi ? $"{mi:0}" : "";

    public string CountryText =>
        !string.IsNullOrEmpty(State) && Country is "USA" or "United States"
            ? $"USA · {State}"
            : !string.IsNullOrEmpty(State) && Country is "Alaska" or "Hawaii" or "Puerto Rico"
                ? $"{Country} · {State}"
                : Country ?? "";

    public string StateText => State ?? "";

    /// <summary>ISO country code for the transmitter (drives the flag image).</summary>
    public string? FlagIso => CountryFlag.ToIso(Country, State);

    /// <summary>US/CA subdivision shown beside the national flag image.</summary>
    public string FlagStateBadge =>
        !string.IsNullOrWhiteSpace(State) && Country is "USA" or "United States" or "Canada"
            ? State.Trim().ToUpperInvariant()
            : "";

    /// <summary>Legacy emoji text (Windows often shows letters — prefer FlagIso + image).</summary>
    public string FlagText => CountryFlag.From(Country, State);

    void SetField(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
