using SignalDx.Audio;
using SignalDx.Ft8;
using SignalDx.Models;
using Xunit;

namespace SignalDx.Tests;

public class JtdxDecoderTests
{
    [Fact]
    public void Downsample48kTo12k_reduces_by_factor_of_4()
    {
        var src = new float[48000];
        for (int i = 0; i < src.Length; i++)
            src[i] = (i % 4) / 4f;

        var dst = Wav12kWriter.Downsample48kTo12k(src);
        Assert.Equal(12000, dst.Length);
    }

    [Fact]
    public void WritePcm16Mono_creates_valid_riff_header()
    {
        string path = Path.Combine(Path.GetTempPath(), $"signaldx_wav_{Guid.NewGuid():N}.wav");
        try
        {
            var samples = new float[1200];
            Wav12kWriter.WritePcm16Mono(path, samples, 12000);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'R', bytes[0]);
            Assert.Equal((byte)'I', bytes[1]);
            Assert.Equal((byte)'F', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
            Assert.True(bytes.Length > 44);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ParseOutput_reads_classic_jt9_lines()
    {
        var svc = new JtdxDecoderService();
        var station = new StationSettings { MyCall = "W1ABC", MyGrid = "FN42", Mode = "FT8" };
        string stdout =
            """
            153000  -12  0.2 1234 ~  CQ K1JT FN20
            153000   -8  0.1 1500 ~  W1ABC K2XYZ R-10
            153000 -24  0.3 1099 ~ 4X1TI JA3JTG -13          *
            <DecodeFinished><avexdt>  0.00<ncand>    2<rxm>  0<rxs>   0.16
            """;

        var hits = svc.ParseOutput(stdout, station, new DateTime(2026, 11, 15, 15, 30, 0, DateTimeKind.Utc), "FT8");
        Assert.Equal(3, hits.Count);
        Assert.Equal("CQ K1JT FN20", hits.First(h => h.Hz == 1234).Message);
        Assert.True(hits.First(h => h.Hz == 1234).IsCq);
        Assert.Equal("K1JT", hits.First(h => h.Hz == 1234).DxCall);
        Assert.Equal("K2XYZ", hits.First(h => h.Hz == 1500).DxCall);
        Assert.Equal("4X1TI JA3JTG -13", hits.First(h => h.Hz == 1099).Message);
    }

    [Fact]
    public void ParseOutput_reads_real_jtdx_bench_lines()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalDx", "decode", "bench-out.txt");
        if (!File.Exists(path)) return;

        var svc = new JtdxDecoderService();
        var station = new StationSettings { MyCall = "W1ABC", MyGrid = "FN42", Mode = "FT8" };
        var hits = svc.ParseOutput(File.ReadAllText(path), station, DateTime.UtcNow, "FT8");
        Assert.True(hits.Count > 10, $"expected many decodes, got {hits.Count}");
    }

    [Fact]
    public void FindJt9_locates_installed_binary_when_present()
    {
        string? path = JtdxPaths.FindJt9();
        if (path is null)
            return; // CI without JTDX
        Assert.True(File.Exists(path));
        Assert.Contains("jt9", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
    }
}
