using Tabletop.GameServer.Properties;

namespace Tabletop.Sim.Suite;

/// <summary>
/// One catalog entry's coverage: the games whose Covers name it, whether it fired in every one of them, and the exact checks about it
/// (from any game that ran). Kind is rule or spell.
/// </summary>
public sealed record CoverageEntry(string Name, string Kind, IReadOnlyList<string> Games, bool Fired, IReadOnlyList<ExactResult> Exact)
{
    /// <summary>No selected game covers the entry; a full run fails on any.</summary>
    public bool Uncovered => Games.Count == 0;
}

/// <summary>
/// Every catalog entry of the pinned set (rules and spells), against the games that ran. Entries with no effect rows are hook
/// rules the ruleset implements by name, so every entry the catalog holds counts against coverage. A full run lists every entry;
/// a run limited by --only lists only the entries its games cover, so it never fails on coverage.
/// </summary>
public static class Coverage
{
    public static IReadOnlyList<CoverageEntry> Of(PropertySet set, IEnumerable<SuiteGameRun> runs, bool fullRun)
    {
        var ran = runs.ToList();
        return set.Entries
            .Select(entry =>
            {
                var covering = ran.Where(r => r.Game.Covers.Contains(entry.Name)).ToList();
                return new CoverageEntry(entry.Name, entry.Kind == CatalogKind.Spell ? "spell" : "rule",
                    covering.Select(r => r.Game.Name).ToList(),
                    covering.Count > 0 && covering.All(r => r.Game.FiredIn(r.EffectiveLog, entry.Name)),
                    ran.SelectMany(r => r.Exact.GetValueOrDefault(entry.Name) ?? []).ToList());
            })
            .Where(c => fullRun || !c.Uncovered)
            .OrderBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A full run is complete when no entry is uncovered; a limited run always is.</summary>
    public static bool Complete(IReadOnlyList<CoverageEntry> coverage) => coverage.All(c => !c.Uncovered);
}
