using SignalDx.Cat;
using Xunit;

namespace SignalDx.Tests;

public class HrdCatalogTests
{
    [Fact]
    public void Catalog_IncludesVirtualHrdEntry()
    {
        var models = HamlibCatalog.GetModels(forceRefresh: true);
        var hrd = models.FirstOrDefault(m => m.Id == HrdCatController.ModelId);
        Assert.NotNull(hrd);
        Assert.Contains("Deluxe", hrd!.Manufacturer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filter_Deluxe_FindsHrd()
    {
        var hits = HamlibCatalog.Filter("Deluxe");
        Assert.Contains(hits, m => m.Id == HrdCatController.ModelId);
    }
}
