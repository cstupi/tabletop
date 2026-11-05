namespace Tabletop.Client.Core;

/// <summary>What the create-game dialog opens with: the default rule set selected, each set shown by its name.</summary>
public static class CreateDefaults
{
    public static int DefaultIndex(IReadOnlyList<PropertiesSummary> packs) => Math.Max(0, packs.ToList().FindIndex(p => p.Default));
    public static string PackLabel(PropertiesSummary p) => p.Name;
}
