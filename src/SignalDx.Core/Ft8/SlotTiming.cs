namespace SignalDx.Ft8;

public sealed class SlotTiming
{
    public string Mode { get; private set; } = "FT8";
    public int SlotSeconds { get; private set; } = 15;
    public DateTime UtcNow { get; private set; } = DateTime.UtcNow;
    public int SlotIndex { get; private set; }
    public double Progress { get; private set; }
    public double SecondsIntoSlot { get; private set; }
    public bool IsEvenSlot { get; private set; }
    public bool JustRolled { get; private set; }

    int _lastSlot = -1;

    public void SetMode(string mode)
    {
        Mode = string.IsNullOrWhiteSpace(mode) ? "FT8" : mode.Trim().ToUpperInvariant();
        SlotSeconds = Mode switch
        {
            "FT4" => 7,
            "WSPR2" or "WSPR" => 120,
            _ => 15
        };
        _lastSlot = -1;
    }

    public bool IsWspr =>
        Mode is "WSPR2" or "WSPR";

    public void Tick(DateTime utc)
    {
        UtcNow = utc;
        long epoch = (long)(utc - DateTime.UnixEpoch).TotalSeconds;
        SlotIndex = (int)(epoch / SlotSeconds);
        double rem = utc.TimeOfDay.TotalSeconds % SlotSeconds;
        SecondsIntoSlot = rem;
        Progress = rem / SlotSeconds;
        IsEvenSlot = (SlotIndex & 1) == 0;
        JustRolled = SlotIndex != _lastSlot && _lastSlot >= 0;
        _lastSlot = SlotIndex;
    }
}
