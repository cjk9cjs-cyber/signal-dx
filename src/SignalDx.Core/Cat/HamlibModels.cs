namespace SignalDx.Cat;

/// <summary>Compatibility helpers over <see cref="HamlibCatalog"/> (full Hamlib model list).</summary>
public static class HamlibModels
{
    public static IReadOnlyList<HamlibModel> All(string? hamlibHintPath = null) =>
        HamlibCatalog.GetModels(hamlibHintPath);

    public static HamlibModel? Find(int id, string? hamlibHintPath = null) =>
        HamlibCatalog.Find(id, hamlibHintPath);

    public static string Display(HamlibModel m) => m.DisplayName;
}
