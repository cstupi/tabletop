namespace Tabletop.Sim.Suite;

/// <summary>
/// Every test game, one partial file per group: Core (core), Special (special), Army (army:&lt;book uid&gt;), Spells (spells:&lt;book uid&gt;).
/// The groups and game names are what --only selects by.
/// </summary>
public static partial class SuiteCatalog
{
    public const string CoreGroup = "core", SpecialGroup = "special", ArmyPrefix = "army:", SpellsPrefix = "spells:";

    public static IReadOnlyList<TestGame> All { get; } = [.. Core(), .. Special(), .. Army(), .. Spells()];

    private static partial IEnumerable<TestGame> Core();
    private static partial IEnumerable<TestGame> Special();
    private static partial IEnumerable<TestGame> Army();
    private static partial IEnumerable<TestGame> Spells();

    /// <summary>
    /// The games --only names, each item a game name or a group, in the games' own order. No items is every game. An item that
    /// matches nothing is a usage error, so a misspelt selection never runs as an empty, passing suite.
    /// </summary>
    public static IReadOnlyList<TestGame> Select(IReadOnlyList<TestGame> games, IReadOnlyList<string> only)
    {
        if (only.Count == 0) return games;
        var unknown = only.Where(item => !games.Any(g => Matches(g, item))).ToList();
        if (unknown.Count > 0)
            throw new UsageException($"--only: no game or group '{string.Join("', '", unknown)}'; groups: {string.Join(", ", games.Select(g => g.Group).Distinct())}");
        return games.Where(g => only.Any(item => Matches(g, item))).ToList();
    }

    private static bool Matches(TestGame game, string item) => game.Name == item || game.Group == item;
}
