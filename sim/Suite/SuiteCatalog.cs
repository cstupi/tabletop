using Tabletop.Content;

namespace Tabletop.Sim.Suite;

/// <summary>
/// Every test game, one partial file per group: Core (core), Special (special), Army (army:&lt;edition&gt;:&lt;book uid&gt;), Spells
/// (spells:&lt;edition&gt;:&lt;book uid&gt;). The groups and game names are what --only selects by.
/// </summary>
public static partial class SuiteCatalog
{
    public const string CoreGroup = "core", SpecialGroup = "special", ArmyPrefix = "army:", SpellsPrefix = "spells:";

    public static IReadOnlyList<TestGame> All { get; } = Expand([.. Core(), .. Special(), .. Army(), .. Spells()]);

    private static partial IEnumerable<TestGame> Core();
    private static partial IEnumerable<TestGame> Special();
    private static partial IEnumerable<TestGame> Army();
    private static partial IEnumerable<TestGame> Spells();

    /// <summary>
    /// One game per edition a game runs under (TestGame.Editions, else its Edition), in order: the scenario plays under that edition, a
    /// game under 3.6 is named &lt;name&gt;@3.6 while the 3.5.1 one keeps the bare name, and a book group names the edition.
    /// </summary>
    public static IReadOnlyList<TestGame> Expand(IEnumerable<TestGame> games) =>
        [.. games.SelectMany(g => EditionsOf(g).Select(edition => g with
        {
            Name = edition == RuleSets.Gf351 ? g.Name : $"{g.Name}@{EditionLabel(edition)}",
            Group = InEdition(g.Group, edition),
            Scenario = g.Scenario with { Rules = edition },
            Edition = edition,
            Editions = null,
        }))];

    private static IReadOnlyList<string> EditionsOf(TestGame g)
    {
        var editions = g.Editions ?? [g.Edition];
        if (editions.Count > 1 && (g.Group.StartsWith(ArmyPrefix, StringComparison.Ordinal) || g.Group.StartsWith(SpellsPrefix, StringComparison.Ordinal)))
            throw new InvalidOperationException($"'{g.Name}' is an army or spell game and runs under one edition: the books' uids differ between editions");
        return editions;
    }

    /// <summary>The game runs under 3.5.1 and 3.6: its checks quote no rule the 3.6 set changes. Not for an army or spell game.</summary>
    public static TestGame BothEditions(this TestGame game) => game with { Editions = [RuleSets.Gf351, RuleSets.Gf360] };

    /// <summary>A book's army group under an edition: army:3.5:&lt;uid&gt;.</summary>
    public static string ArmyGroup(string rulesId, string uid) => $"{ArmyPrefix}{RuleSets.EditionOf(rulesId)}:{uid}";

    /// <summary>A book's spells group under an edition: spells:3.5:&lt;uid&gt;.</summary>
    public static string SpellsGroup(string rulesId, string uid) => $"{SpellsPrefix}{RuleSets.EditionOf(rulesId)}:{uid}";

    /// <summary>The book uid an army or spells group names.</summary>
    public static string BookOf(string group) =>
        group.Split(':') is [_, _, var uid] ? uid : throw new ArgumentException($"'{group}' is not an army:<edition>:<uid> or spells:<edition>:<uid> group", nameof(group));

    /// <summary>A rules id as the report shows it: its army edition (3.5, 3.6), or the id itself when it is no shipped set.</summary>
    public static string EditionLabel(string rulesId) => RuleSets.All.Contains(rulesId) ? RuleSets.EditionOf(rulesId) : rulesId;

    private static string InEdition(string group, string rulesId) =>
        group.StartsWith(ArmyPrefix, StringComparison.Ordinal) ? ArmyGroup(rulesId, BookOf(group))
        : group.StartsWith(SpellsPrefix, StringComparison.Ordinal) ? SpellsGroup(rulesId, BookOf(group))
        : group;

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
