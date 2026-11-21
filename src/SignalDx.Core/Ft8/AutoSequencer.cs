using SignalDx.Models;

namespace SignalDx.Ft8;

public enum QsoPhase
{
    Idle,
    CallingCq,
    CallingDx,
    SentReport,
    SentRogerReport,
    /// <summary>Next TX is RR73 (after their R-report / RRR).</summary>
    Sent73,
    /// <summary>Next TX is plain 73 (after they sent 73 in reply to our R+report).</summary>
    SendingSeventyThree,
    Complete
}

/// <summary>Classified last token / shape of an FT8 QSO message.</summary>
public enum Ft8QsoToken
{
    Other,
    Grid,
    Report,
    RogerReport,
    Rrr,
    Rr73,
    SeventyThree
}

public sealed class AutoSequencer
{
    public QsoPhase Phase { get; private set; } = QsoPhase.Idle;
    public string DxCall { get; private set; } = "";
    public string DxGrid { get; private set; } = "";
    public string Report { get; set; } = "-10";

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

    /// <summary>Operator/auto-seq: next TX is RR73 (Tx4) to the current DX.</summary>
    public void QueueRr73(string? dxCall = null, string? dxGrid = null, string? report = null)
    {
        if (!string.IsNullOrWhiteSpace(dxCall))
            DxCall = dxCall.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(dxGrid))
            DxGrid = dxGrid.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(report))
            Report = report;
        if (string.IsNullOrWhiteSpace(DxCall))
            return;
        Phase = QsoPhase.Sent73;
    }

    public void MarkComplete() => Phase = QsoPhase.Complete;

    public void StartWorking(string dxCall, string? dxGrid, string report, bool forceRestart = false)
    {
        string call = dxCall.Trim().ToUpperInvariant();
        string grid = dxGrid?.Trim().ToUpperInvariant() ?? "";
        Report = report;

        // Do not rewind an in-progress QSO with the same station back to the grid call (Tx1),
        // unless the operator explicitly re-applies a typed call (forceRestart).
        bool sameMidQso =
            !forceRestart &&
            !string.IsNullOrWhiteSpace(DxCall) &&
            DxCall.Equals(call, StringComparison.OrdinalIgnoreCase) &&
            Phase is QsoPhase.CallingDx or QsoPhase.SentReport
                or QsoPhase.SentRogerReport or QsoPhase.Sent73
                or QsoPhase.SendingSeventyThree;

        DxCall = call;
        if (!string.IsNullOrWhiteSpace(grid))
            DxGrid = grid;

        if (sameMidQso)
            return;

        Phase = QsoPhase.CallingDx;
    }

    /// <summary>
    /// Arm TX for a station based on what we just decoded from them.
    /// CQ → Tx1 (grid). Grid-to-us → Tx2 (report). Report-to-us → Tx3 (R+report). R-report → Tx4 (RR73).
    /// </summary>
    public void ArmFromDecode(string message, StationSettings station, string measuredReport)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        string my = station.MyCall.Trim().ToUpperInvariant();
        Report = string.IsNullOrWhiteSpace(measuredReport) ? Report : measuredReport;

        // Their CQ — we call them with grid.
        if (parts[0].Equals("CQ", StringComparison.OrdinalIgnoreCase))
        {
            string call = parts.Length > 1 ? parts[1] : "";
            if (parts.Length >= 4 && call is "DX" or "NA" or "EU" or "AS" or "SA" or "OC" or "AF")
                call = parts[2];
            if (string.IsNullOrWhiteSpace(call)) return;
            DxCall = call.ToUpperInvariant();
            DxGrid = parts.Length >= 3 && LooksLikeGrid(parts[^1]) ? parts[^1].ToUpperInvariant() : DxGrid;
            Phase = QsoPhase.CallingDx;
            return;
        }

        bool toMe = parts[0].Equals(my, StringComparison.OrdinalIgnoreCase);
        // Always select the transmitter (CALLER = parts[1]), never the called party.
        string transmitter = parts[1].ToUpperInvariant();
        if (transmitter.Equals(my, StringComparison.OrdinalIgnoreCase))
            return;

        DxCall = transmitter;
        var token = ExtractQsoToken(message);

        if (toMe)
        {
            switch (token)
            {
                case Ft8QsoToken.Grid:
                    DxGrid = parts[^1].ToUpperInvariant();
                    Phase = QsoPhase.SentReport; // next TX = report (Tx2)
                    break;
                case Ft8QsoToken.Report:
                    Report = NormalizeSnrToken(parts[^1]);
                    if (!string.IsNullOrWhiteSpace(measuredReport))
                        Report = measuredReport;
                    Phase = QsoPhase.SentRogerReport; // next TX = R+report (Tx3)
                    break;
                case Ft8QsoToken.RogerReport:
                    Report = NormalizeRogerReport(parts[^1]);
                    if (!string.IsNullOrWhiteSpace(measuredReport))
                        Report = measuredReport;
                    Phase = QsoPhase.Sent73; // next TX = RR73 (Tx4)
                    break;
                case Ft8QsoToken.Rrr:
                    Phase = QsoPhase.Sent73;
                    break;
                case Ft8QsoToken.Rr73:
                    Phase = QsoPhase.Complete;
                    break;
                case Ft8QsoToken.SeventyThree:
                    // They sent 73 — next TX is RR73 (Tx4).
                    Phase = QsoPhase.Sent73;
                    break;
                default:
                    Phase = QsoPhase.CallingDx;
                    break;
            }
            return;
        }

        // Not to us — call the transmitter we double-clicked (their CQ reply to someone else, etc.).
        if (LooksLikeGrid(parts[^1]))
            DxGrid = parts[^1].ToUpperInvariant();
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
            QsoPhase.SendingSeventyThree => Ft8MessageBuilder.Build("{dxcall} {mycall} 73", station, DxCall, Report),
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

        var token = ExtractQsoToken(message);
        string last = parts[^1];

        if (Phase == QsoPhase.CallingCq && toMe)
        {
            string theirCall = parts[1].ToUpperInvariant();
            if (theirCall.Equals(my, StringComparison.OrdinalIgnoreCase))
                return false;

            // Classic first reply to CQ is MYCALL THEIRCALL GRID only.
            if (token != Ft8QsoToken.Grid)
                return false;

            DxCall = theirCall;
            DxGrid = last.ToUpperInvariant();
            Phase = QsoPhase.SentReport; // Tx2 report
            return true;
        }

        if (!toMe || string.IsNullOrWhiteSpace(DxCall)) return false;
        if (!parts[1].Equals(DxCall, StringComparison.OrdinalIgnoreCase))
            return false;

        // We called them with grid (Tx1); their reply picks our next TX.
        if (Phase == QsoPhase.CallingDx)
        {
            switch (token)
            {
                case Ft8QsoToken.Report:
                    Report = NormalizeSnrToken(last);
                    Phase = QsoPhase.SentRogerReport; // Tx3 R+report (skip bare report)
                    return true;
                case Ft8QsoToken.RogerReport:
                    Report = NormalizeRogerReport(last);
                    Phase = QsoPhase.Sent73; // Tx4
                    return true;
                case Ft8QsoToken.Rrr:
                    Phase = QsoPhase.Sent73;
                    return true;
                case Ft8QsoToken.SeventyThree:
                    Phase = QsoPhase.Sent73; // Tx4 RR73
                    return true;
                case Ft8QsoToken.Rr73:
                    Phase = QsoPhase.Complete;
                    return true;
                case Ft8QsoToken.Grid:
                    DxGrid = last.ToUpperInvariant();
                    Phase = QsoPhase.SentReport; // Tx2
                    return true;
                default:
                    return false;
            }
        }

        // We are/were sending a report (Tx2); their R-report or 73 → RR73 (Tx4).
        if (Phase == QsoPhase.SentReport)
        {
            switch (token)
            {
                case Ft8QsoToken.RogerReport:
                case Ft8QsoToken.Rrr:
                case Ft8QsoToken.SeventyThree:
                    Phase = QsoPhase.Sent73;
                    return true;
                case Ft8QsoToken.Rr73:
                    Phase = QsoPhase.Complete;
                    return true;
                case Ft8QsoToken.Report:
                    // They sent a bare report instead of R+report — roger it.
                    Report = NormalizeSnrToken(last);
                    Phase = QsoPhase.SentRogerReport;
                    return true;
                default:
                    return false;
            }
        }

        // We are/were sending R+report (Tx3). Their 73 → RR73 (Tx4). Their RR73 → done.
        if (Phase == QsoPhase.SentRogerReport)
        {
            if (token is Ft8QsoToken.SeventyThree)
            {
                Phase = QsoPhase.Sent73; // Tx4
                return true;
            }
            if (token is Ft8QsoToken.Rr73)
            {
                Phase = QsoPhase.Complete;
                return true;
            }
            if (token is Ft8QsoToken.Rrr or Ft8QsoToken.RogerReport)
            {
                Phase = QsoPhase.Sent73;
                return true;
            }
            return false;
        }

        // We sent / are sending RR73 (Tx4); their 73 or RR73 ends the QSO.
        if (Phase == QsoPhase.Sent73 && token is Ft8QsoToken.SeventyThree or Ft8QsoToken.Rr73)
        {
            Phase = QsoPhase.Complete;
            return true;
        }

        // Legacy plain-73 phase: their 73/RR73 ends the call.
        if (Phase == QsoPhase.SendingSeventyThree &&
            token is Ft8QsoToken.SeventyThree or Ft8QsoToken.Rr73)
        {
            Phase = QsoPhase.Complete;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Classify the QSO payload token from a full FT8 message.
    /// Handles <c>R-03</c>, <c>R+03</c>, and split <c>R -03</c> forms; ignores trailing junk.
    /// </summary>
    public static Ft8QsoToken ExtractQsoToken(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return Ft8QsoToken.Other;
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return Ft8QsoToken.Other;

        // Prefer last non-junk token; also detect split "R" + "-03".
        for (int i = parts.Length - 1; i >= 1; i--)
        {
            string p = parts[i];
            if (IsDecodeJunk(p)) continue;

            // "K9CJS W1ABC R -03" → RogerReport (before bare Report wins)
            if (IsSnrBody(p.Replace('\u2212', '-')) &&
                parts[i - 1].Equals("R", StringComparison.OrdinalIgnoreCase))
                return Ft8QsoToken.RogerReport;

            var token = ClassifyToken(p);
            if (token is not Ft8QsoToken.Other)
                return token;
        }

        return ClassifyToken(parts[^1]);
    }

    public static Ft8QsoToken ClassifyToken(string last)
    {
        if (string.IsNullOrEmpty(last)) return Ft8QsoToken.Other;
        string u = last.Trim().ToUpperInvariant().Replace('\u2212', '-');

        if (u is "RR73") return Ft8QsoToken.Rr73;
        if (u is "73") return Ft8QsoToken.SeventyThree;
        if (u is "RRR") return Ft8QsoToken.Rrr;

        // R-12 / R+05 / R12
        if (u.Length >= 2 && u[0] == 'R' && IsSnrBody(u.AsSpan(1)))
            return Ft8QsoToken.RogerReport;

        if (IsSnrBody(u))
            return Ft8QsoToken.Report;

        if (LooksLikeGrid(u))
            return Ft8QsoToken.Grid;

        return Ft8QsoToken.Other;
    }

    static bool IsDecodeJunk(string s) =>
        s.Equals("a1", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("a2", StringComparison.OrdinalIgnoreCase) ||
        s is "?" or "!" or "*";

    static bool IsSnrBody(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return false;
        int i = 0;
        if (s[0] is '+' or '-' or '\u2212') // ASCII or unicode minus
            i = 1;
        if (i >= s.Length) return false;
        for (; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) return false;
        }
        return true;
    }

    static string NormalizeSnrToken(string last)
    {
        string t = last.Trim().Replace('\u2212', '-');
        if (int.TryParse(t, out int v))
            return v >= 0 ? $"+{v:00}" : $"{v:00}";
        if (t.StartsWith('+') || t.StartsWith('-'))
            return t;
        return t;
    }

    static string NormalizeRogerReport(string last)
    {
        string t = last.Trim().Replace('\u2212', '-');
        if (t.StartsWith('R') || t.StartsWith('r'))
            t = t[1..];
        return NormalizeSnrToken(t);
    }

    static bool LooksLikeGrid(string s)
    {
        if (s.Length is not (4 or 6)) return false;
        if (s.Equals("RR73", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("RRR", StringComparison.OrdinalIgnoreCase))
            return false;
        return char.IsLetter(s[0]) && char.IsLetter(s[1])
               && char.IsDigit(s[2]) && char.IsDigit(s[3]);
    }
}
