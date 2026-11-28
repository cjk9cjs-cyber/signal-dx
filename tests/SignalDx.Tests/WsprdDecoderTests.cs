using SignalDx.Audio;
using SignalDx.Models;
using SignalDx.Wspr;
using Xunit;

namespace SignalDx.Tests;

public class WsprdDecoderTests
{
    const string SampleStdout =
        """
        0000  37 -0.0  14.097100  0  W1ABC FN42 37 
        0000  21 -0.1  14.097120  0  K9CJS EM65 30 
        <DecodeFinished>
        """;

    static StationSettings Station() => new()
    {
        MyCall = "N0CALL",
        MyGrid = "EM15",
        Mode = "WSPR2",
        DialMhz = 14.0956
    };

    [Fact]
    public void ParseOutput_maps_spot_lines_to_decode_hits()
    {
        var svc = new WsprdDecoderService();
        var slot = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var hits = svc.ParseOutput(SampleStdout, Station(), slot);

        Assert.Equal(2, hits.Count);
        Assert.Equal(1500, hits[0].Hz);
        Assert.Equal(1520, hits[1].Hz);
        Assert.Equal("W1ABC FN42 37", hits[0].Message);
        Assert.Equal("W1ABC", hits[0].DxCall);
        Assert.Equal("FN42", hits[0].DxGrid);
        Assert.Equal(37.0, hits[0].SnrDb);
        Assert.Equal("WSPR2", hits[0].Mode);
        Assert.Equal(slot, hits[0].Utc);
        Assert.False(hits[0].IsCq);
    }

    [Fact]
    public void ParseOutput_skips_lines_without_a_grid()
    {
        var svc = new WsprdDecoderService();
        const string stdout = "0000  20  0.1  14.097100  0  PJ4/K1ABC 37 \n<DecodeFinished>\n";

        var hits = svc.ParseOutput(stdout, Station(), DateTime.UtcNow);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task DecodeSlotAsync_decodes_synthetic_wspr_slot_when_wsprd_installed()
    {
        if (WsprdPaths.Find() is null)
            return;

        var slot = new float[Wav12kWriter.InputRate * 120];
        var rng = new Random(7);
        for (int i = 0; i < slot.Length; i++)
            slot[i] = (float)((rng.NextDouble() * 2 - 1) * 0.01);
        AddSignal(slot, "W1ABC", "FN42", 37, 1500, 0.2);
        AddSignal(slot, "K9CJS", "EM65", 30, 1520, 0.03);

        using var svc = new WsprdDecoderService();
        var hits = await svc.DecodeSlotAsync(slot, Station(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Contains(hits, h => h.DxCall == "W1ABC" && h.Hz == 1500);
        Assert.Contains(hits, h => h.DxCall == "K9CJS" && h.Hz == 1520);
    }

    static void AddSignal(float[] dst, string call, string grid, int dbm, int baseHz, double amplitude)
    {
        int[] symbols = WsprEncoder.EncodeSymbols(call, grid, dbm);
        float[] sig = WsprToneGenerator.Synthesize(symbols, baseHz, amplitude, includeLeadIn: true);
        int n = Math.Min(sig.Length, dst.Length);
        for (int i = 0; i < n; i++)
            dst[i] += sig[i];
    }
}
