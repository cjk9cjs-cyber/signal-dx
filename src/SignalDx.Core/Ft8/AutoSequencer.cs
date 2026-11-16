using SignalDx.Models;

namespace SignalDx.Ft8;

public enum QsoPhase
{
    Idle,
    CallingCq,
    CallingDx,
    SentReport,
    SentRogerReport,
    Sent73,
    Complete
}

public sealed class AutoSequencer
{
    public QsoPhase Phase { get; private set; } = QsoPhase.Idle;
    public string DxCall { get; private set; } = "";
    public string DxGrid { get; private set; } = "";
    public string Report { get; private set; } = "-10";

    public void Reset()
    {
        Phase = QsoPhase.Idle;
        DxCall = "";
        DxGrid = "";
    }

    public void StartCq()
    {
        Phase = QsoPhase.CallingCq;
        DxCall = "";
        DxGrid = "";
    }

    public void StartWorking(string dxCall, string? dxGrid, string report)
    {
        DxCall = dxCall.Trim().ToUpperInvariant();
        DxGrid = dxGrid?.Trim().ToUpperInvariant() ?? "";
        Report = report;
        Phase = QsoPhase.CallingDx;
    }

    public string? NextTxMessage(StationSettings station)
    {
        return Phase switch
        {
            QsoPhase.CallingCq => Ft8MessageBuilder.Build("CQ {mycall} {mygrid}", station, DxCall, Report),
            QsoPhase.CallingDx => Ft8MessageBuilder.Build("{dxcall} {mycall} {mygrid}", station, DxCall, Report),
            QsoPhase.SentReport => Ft8MessageBuilder.Build("{dxcall} {mycall} {snr}", station, DxCall, Report),
            QsoPhase.SentRogerReport => Ft8MessageBuilder.Build("{dxcall} {mycall} R{snr}", station, DxCall, Report),
            QsoPhase.Sent73 => Ft8MessageBuilder.Build("{dxcall} {mycall} RR73", station, DxCall, Report),
            _ => null
        };
    }

    public bool TryAdvanceFromDecode(string message, StationSettings station)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        string my = station.MyCall.Trim().ToUpperInvariant();
        bool toMe = parts[0].Equals(my, StringComparison.OrdinalIgnoreCase);

        if (Phase == QsoPhase.CallingCq && toMe && parts.Length >= 2)
        {
            DxCall = parts[1].ToUpperInvariant();
            if (parts.Length >= 3 && LooksLikeGrid(parts[^1]))
                DxGrid = parts[^1].ToUpperInvariant();
            Phase = QsoPhase.SentReport;
            return true;
        }

        if (!toMe || string.IsNullOrWhiteSpace(DxCall)) return false;
        if (!parts[1].Equals(DxCall, StringComparison.OrdinalIgnoreCase) &&
            Phase is not QsoPhase.CallingDx)
            return false;

        string last = parts[^1].ToUpperInvariant();
        if (Phase == QsoPhase.CallingDx)
        {
            Phase = QsoPhase.SentReport;
            return true;
        }

        if (Phase == QsoPhase.SentReport && (last.StartsWith('R') || last is "RRR" or "RR73" or "73"))
        {
            Phase = last is "RR73" or "73" ? QsoPhase.Complete : QsoPhase.Sent73;
            return true;
        }

        if (Phase == QsoPhase.SentRogerReport)
        {
            Phase = QsoPhase.Sent73;
            return true;
        }

        if (Phase == QsoPhase.Sent73 && last is "73" or "RR73")
        {
            Phase = QsoPhase.Complete;
            return true;
        }

        // They answered with a report — send R+report
        if (Phase == QsoPhase.SentReport && (last.StartsWith('+') || last.StartsWith('-') || char.IsDigit(last[0])))
        {
            Phase = QsoPhase.SentRogerReport;
            return true;
        }

        return false;
    }

    static bool LooksLikeGrid(string s) =>
        s.Length is 4 or 6
        && char.IsLetter(s[0]) && char.IsLetter(s[1])
        && char.IsDigit(s[2]) && char.IsDigit(s[3]);
}
