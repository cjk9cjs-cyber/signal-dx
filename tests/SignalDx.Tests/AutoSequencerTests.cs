using SignalDx.Ft8;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class AutoSequencerTests
{
    [Fact]
    public void AdvancesFromCqWhenCalled()
    {
        var station = new StationSettings { MyCall = "W1ABC", MyGrid = "FN42" };
        var seq = new AutoSequencer();
        seq.StartCq();
        Assert.True(seq.TryAdvanceFromDecode("W1ABC K1XYZ FN31", station));
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("K1XYZ", seq.DxCall);
        Assert.Equal("K1XYZ W1ABC -10", seq.NextTxMessage(station));
    }

    [Fact]
    public void CqIgnoresStaleRr73AndKeepsCallingCq()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();
        Assert.False(seq.TryAdvanceFromDecode("K9CJS W1OLD RR73", station));
        Assert.Equal(QsoPhase.CallingCq, seq.Phase);
        Assert.Equal("CQ K9CJS EN52", seq.NextTxMessage(station));
    }

    [Fact]
    public void AfterCallingDx_ReportAdvancesToTx3_NotTx1()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");
        Assert.Equal(QsoPhase.CallingDx, seq.Phase);
        Assert.Equal("W1ABC K9CJS EN52", seq.NextTxMessage(station));

        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS R-12", seq.NextTxMessage(station));
    }

    [Fact]
    public void StartWorkingSameStation_DoesNotRewindAfterReport()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);

        seq.StartWorking("W1ABC", "FN42", "-12");
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS R-12", seq.NextTxMessage(station));
    }

    [Fact]
    public void ArmFromDecode_ReportGoesToTx3()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.ArmFromDecode("K9CJS W1ABC -12", station, "-09");
        Assert.Equal("W1ABC", seq.DxCall);
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS R-09", seq.NextTxMessage(station));
    }

    [Fact]
    public void ArmFromDecode_GridGoesToTx2()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.ArmFromDecode("K9CJS W1ABC FN42", station, "-11");
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS -11", seq.NextTxMessage(station));
    }

    [Fact]
    public void ArmFromDecode_CqGoesToTx1()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.ArmFromDecode("CQ W1ABC FN42", station, "-10");
        Assert.Equal(QsoPhase.CallingDx, seq.Phase);
        Assert.Equal("W1ABC K9CJS EN52", seq.NextTxMessage(station));
    }

    [Fact]
    public void ArmFromDecode_OtherQsoSelectsTransmitterNotCalled()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        // K1ABC is being called; W2XYZ is transmitting — we must work W2XYZ.
        seq.ArmFromDecode("K1ABC W2XYZ FN42", station, "-10");
        Assert.Equal("W2XYZ", seq.DxCall);
        Assert.Equal(QsoPhase.CallingDx, seq.Phase);
        Assert.Equal("W2XYZ K9CJS EN52", seq.NextTxMessage(station));
    }

    [Fact]
    public void CqFoxCompletesWithRr73()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52", };
        var seq = new AutoSequencer();
        seq.StartCq();
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC EN52", station));
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS -10", seq.NextTxMessage(station));

        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC R-10", station));
        Assert.Equal(QsoPhase.Sent73, seq.Phase);
        Assert.Equal("W1ABC K9CJS RR73", seq.NextTxMessage(station));
    }

    [Fact]
    public void SentRogerReport_IgnoresRepeatReport_StaysOnTx3()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);

        // Same report again must not kick us off R-report back toward Tx1.
        Assert.False(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS R-12", seq.NextTxMessage(station));
    }

    [Fact]
    public void AfterRReport_Their73_WeSendRr73_ThenTheir73EndsQso()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC K9CJS R-12", seq.NextTxMessage(station));

        // They return 73 after our Rxx → Tx4 RR73 (not another Rxx / Tx3).
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC 73", station));
        Assert.Equal(QsoPhase.Sent73, seq.Phase);
        Assert.Equal("W1ABC K9CJS RR73", seq.NextTxMessage(station));

        // They send 73 again after we sent RR73 → QSO ends.
        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC 73", station));
        Assert.Equal(QsoPhase.Complete, seq.Phase);
        Assert.Null(seq.NextTxMessage(station));
    }

    [Fact]
    public void QueueRr73_ForcesTx4()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.QueueRr73("W1ABC", "FN42", "-10");
        Assert.Equal(QsoPhase.Sent73, seq.Phase);
        Assert.Equal("W1ABC K9CJS RR73", seq.NextTxMessage(station));
    }

    [Fact]
    public void AfterTx2_TheirRReport_AdvancesToRr73()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();
        Assert.True(seq.TryAdvanceFromDecode("K9CJS N6YYZ EM12", station));
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("N6YYZ K9CJS -10", seq.NextTxMessage(station));

        // Station returns R-03 while we are still on Tx2 → RR73, not another report.
        Assert.True(seq.TryAdvanceFromDecode("K9CJS N6YYZ R-03", station));
        Assert.Equal(QsoPhase.Sent73, seq.Phase);
        Assert.Equal("N6YYZ K9CJS RR73", seq.NextTxMessage(station));
    }

    [Fact]
    public void ExtractQsoToken_SplitRogerReport()
    {
        Assert.Equal(Ft8QsoToken.RogerReport, AutoSequencer.ExtractQsoToken("K9CJS N6YYZ R -03"));
        Assert.Equal(Ft8QsoToken.RogerReport, AutoSequencer.ExtractQsoToken("K9CJS N6YYZ R+03"));
        Assert.Equal(Ft8QsoToken.RogerReport, AutoSequencer.ExtractQsoToken("K9CJS N6YYZ R-03"));
    }

    [Fact]
    public void QsoReports_SentAndReceivedAreTrackedSeparately()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");

        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station));
        seq.Report = "-08";
        seq.RecordTransmitted("W1ABC K9CJS -08");
        Assert.Equal("-08", seq.SentReport);
        Assert.Equal("-12", seq.RcvdReport);

        Assert.True(seq.TryAdvanceFromDecode("K9CJS W1ABC R-05", station));
        seq.RecordTransmitted("W1ABC K9CJS R-08");
        Assert.Equal("-08", seq.SentReport);
        Assert.Equal("-05", seq.RcvdReport);
    }

    [Fact]
    public void QsoReports_ClearWhenDxStationChanges()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");
        seq.TryAdvanceFromDecode("K9CJS W1ABC -12", station);
        seq.RecordTransmitted("W1ABC K9CJS -08");

        seq.StartWorking("K1XYZ", "FN31", "-10");
        Assert.Equal("", seq.SentReport);
        Assert.Equal("", seq.RcvdReport);
    }

    [Fact]
    public void HashedCallsign_IsUnwrappedForReplyAndAdvance()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC", "FN42", "-08");

        Assert.True(seq.TryAdvanceFromDecode("K9CJS <W1ABC> -12", station));
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("W1ABC", seq.DxCall);
        Assert.Equal("W1ABC K9CJS R-12", seq.NextTxMessage(station));
    }

    [Fact]
    public void HashedCq_ArmsReplyWithPlainCallsign()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();

        Assert.True(seq.TryAdvanceFromDecode("K9CJS <W1ABC> FN42", station));
        Assert.Equal("W1ABC", seq.DxCall);
        Assert.Equal("W1ABC K9CJS -10", seq.NextTxMessage(station));
    }

    [Fact]
    public void UnresolvedHashedCall_IsNotArmed()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();

        Assert.False(seq.TryAdvanceFromDecode("K9CJS <...> FN42", station));
        Assert.Equal(QsoPhase.CallingCq, seq.Phase);
    }

    [Fact]
    public void CompoundSlashCall_FullQsoFlowWithHashedTxAndRx()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();

        // Type 4 nonstandard call answering CQ without a grid → Tx2 report, wrapped in <>
        Assert.True(seq.TryAdvanceFromDecode("<K9CJS> W1ABC/7", station));
        Assert.Equal("W1ABC/7", seq.DxCall);
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("<W1ABC/7> K9CJS -10", seq.NextTxMessage(station));

        seq.RecordTransmitted("<W1ABC/7> K9CJS -10");
        Assert.Equal("-10", seq.SentReport);

        // Their hashed R-report advances to Tx4 RR73
        Assert.True(seq.TryAdvanceFromDecode("K9CJS <W1ABC/7> R-08", station));
        Assert.Equal(QsoPhase.Sent73, seq.Phase);
        Assert.Equal("-08", seq.RcvdReport);
        Assert.Equal("<W1ABC/7> K9CJS RR73", seq.NextTxMessage(station));

        // Their hashed 73 finishes QSO
        Assert.True(seq.TryAdvanceFromDecode("<K9CJS> W1ABC/7 73", station));
        Assert.Equal(QsoPhase.Complete, seq.Phase);
    }

    [Fact]
    public void CompoundSlashCall_CqDirectReportReply_AdvancesToTx3()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartCq();

        Assert.True(seq.TryAdvanceFromDecode("K9CJS <W1ABC/7> -12", station));
        Assert.Equal("W1ABC/7", seq.DxCall);
        Assert.Equal("-12", seq.RcvdReport);
        Assert.Equal(QsoPhase.SentRogerReport, seq.Phase);
        Assert.Equal("<W1ABC/7> K9CJS R-10", seq.NextTxMessage(station));
    }

    [Fact]
    public void ArmFromDecode_Type4TwoTokenCall_ArmsTx2NotTx1()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();

        seq.ArmFromDecode("<K9CJS> W1ABC/7", station, "-09");
        Assert.Equal("W1ABC/7", seq.DxCall);
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("<W1ABC/7> K9CJS -09", seq.NextTxMessage(station));
    }

    [Fact]
    public void SetManualPhase_SwitchesFromTx1ToTx2WithoutReverting()
    {
        var station = new StationSettings { MyCall = "K9CJS", MyGrid = "EN52" };
        var seq = new AutoSequencer();
        seq.StartWorking("W1ABC/7", "", "-07");
        Assert.Equal(QsoPhase.CallingDx, seq.Phase);
        Assert.Equal("<W1ABC/7> K9CJS EN52", seq.NextTxMessage(station));

        // Operator manually selects Tx2 — sequencer stays on Tx2 for NextTxMessage.
        seq.SetManualPhase(QsoPhase.SentReport, "W1ABC/7", "", "-07");
        Assert.Equal(QsoPhase.SentReport, seq.Phase);
        Assert.Equal("<W1ABC/7> K9CJS -07", seq.NextTxMessage(station));
    }
}