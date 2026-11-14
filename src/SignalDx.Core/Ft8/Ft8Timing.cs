namespace SignalDx.Ft8;

public sealed class Ft8Timing
{
    public const int SlotSeconds = 15;
    public const double ToneSpacingHz = 6.25;
    public const int SymbolCount = 79;
    public const double SymbolDurationSec = 0.16;

    public DateTime UtcNow { get; private set; } = DateTime.UtcNow;
    public int SlotIndex { get; private set; }
    public double Progress { get; private set; }
    public double SecondsIntoSlot { get; private set; }
    public bool IsEvenSlot { get; private set; }
    public bool JustRolled { get; private set; }

    int _lastSlot = -1;

    public void Tick(DateTime utc)
    {
        UtcNow = utc;
        long epoch = (long)(utc - DateTime.UnixEpoch).TotalSeconds;
        SlotIndex = (int)(epoch / SlotSeconds);
        SecondsIntoSlot = utc.Second % SlotSeconds + utc.Millisecond / 1000.0;
        Progress = SecondsIntoSlot / SlotSeconds;
        IsEvenSlot = (SlotIndex & 1) == 0;
        JustRolled = SlotIndex != _lastSlot && _lastSlot >= 0;
        _lastSlot = SlotIndex;
    }
}
