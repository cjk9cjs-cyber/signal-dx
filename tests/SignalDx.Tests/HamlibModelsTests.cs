using SignalDx.Cat;
using Xunit;

namespace SignalDx.Tests;

public class HamlibModelsTests
{
    [Fact]
    public void EmbeddedOrLiveCatalog_ContainsManyModels()
    {
        var models = HamlibCatalog.GetModels(forceRefresh: true);
        Assert.True(models.Count > 100, $"Expected full Hamlib list, got {models.Count}");
        Assert.Contains(models, m => m.Id == 1 && m.Model.Contains("Dummy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Filter_FindsIcomByName()
    {
        var hits = HamlibCatalog.Filter("IC-7300");
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void ParseList_ReadsFixedColumns()
    {
        const string sample =
            " Rig #  Mfg                    Model                   Version         Status      Macro\n" +
            "     1  Hamlib                 Dummy                   20240709.0      Stable      RIG_MODEL_DUMMY\n" +
            "  2037  Icom                   IC-7300                 20240101.0      Stable      RIG_MODEL_IC7300\n";

        var list = HamlibCatalog.ParseList(sample);
        Assert.Equal(2, list.Count);
        Assert.Equal("IC-7300", list[1].Model);
        Assert.Equal("Icom", list[1].Manufacturer);
    }

    [Fact]
    public void FindRigctld_DoesNotThrow()
    {
        var path = HamlibPaths.FindRigctld();
        Assert.True(path is null || File.Exists(path));
    }

    [Fact]
    public void ExtractPort_FromFriendlyName()
    {
        Assert.Equal("COM7", HamlibRigctldLauncher.ExtractPort("COM7 — Silicon Labs Dual CP2105"));
        Assert.Equal("COM5", HamlibRigctldLauncher.ExtractPort("COM5"));
    }
}
