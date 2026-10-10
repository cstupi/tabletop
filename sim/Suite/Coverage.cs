using Tabletop.GameServer.Properties;

namespace Tabletop.Sim.Suite;

/// <summary>Own: a game names the entry. Pattern: a generated wrapper, covered by its pattern's game and its base. NotAutomated: a text-only entry.</summary>
public enum CoverageState { Own, Pattern, NotAutomated }

/// <summary>
/// One catalog entry's coverage: the games that cover it, whether it fired in every one of them (a wrapper: its pattern's games passed and its
/// base fired), the exact checks about it (from any game that ran), and how it is covered. Kind is rule or spell.
/// </summary>
public sealed record CoverageEntry(string Name, string Kind, IReadOnlyList<string> Games, bool Fired, IReadOnlyList<ExactResult> Exact,
    CoverageState State = CoverageState.Own)
{
    /// <summary>No selected game covers the entry; a full run fails on any. A text-only entry is never uncovered.</summary>
    public bool Uncovered => State != CoverageState.NotAutomated && Games.Count == 0;
    /// <summary>Whether the entry counts against coverage: every entry but a text-only one (army-book rules spec → Coverage).</summary>
    public bool Counts => State != CoverageState.NotAutomated;
}

/// <summary>
/// Every catalog entry of the pinned set (rules and spells), against the games that ran. Entries with no effect rows are hook rules the
/// ruleset implements by name, so they count. A text-only entry (Played false) is listed, on a full run, as not automated and is not
/// counted. A generated wrapper is covered by pattern: by the games whose Pattern is its name's last word, when those passed and its base
/// (its one row's target) is covered and fired. A run limited by --only lists only the entries its games cover, so it never fails on coverage.
/// </summary>
public static class Coverage
{
    public static IReadOnlyList<CoverageEntry> Of(PropertySet set, IEnumerable<SuiteGameRun> runs, bool fullRun,
        IReadOnlySet<string>? generated = null, IReadOnlySet<string>? passed = null)
    {
        var ran = runs.ToList();
        bool Wrapper(CatalogEntry e) => generated?.Contains(e.Name) == true && e.Effects.Count == 1;
        IReadOnlyList<ExactResult> Exact(string name) => ran.SelectMany(r => r.Exact.GetValueOrDefault(name) ?? []).ToList();
        var own = set.Entries.Where(e => e.Played && !Wrapper(e)).Select(entry =>
        {
            var covering = ran.Where(r => r.Game.Covers.Contains(entry.Name)).ToList();
            return new CoverageEntry(entry.Name, entry.Kind == CatalogKind.Spell ? "spell" : "rule", covering.Select(r => r.Game.Name).ToList(),
                covering.Count > 0 && covering.All(r => r.Game.FiredIn(r.EffectiveLog, entry.Name)), Exact(entry.Name));
        }).ToDictionary(c => c.Name);
        var wrappers = set.Entries.Where(e => e.Played && Wrapper(e)).Select(entry =>
        {
            var pattern = entry.Name[(entry.Name.LastIndexOf(' ') + 1)..];
            var games = ran.Where(r => r.Game.Pattern == pattern).Select(r => r.Game.Name).ToList();
            var x = own.GetValueOrDefault(entry.Effects[0].TargetEntry ?? "");
            var covered = games.Count > 0 && x is { Uncovered: false };
            return new CoverageEntry(entry.Name, "rule", covered ? games : [], covered && x!.Fired && games.All(g => passed?.Contains(g) ?? true),
                Exact(entry.Name), CoverageState.Pattern);
        });
        var textOnly = fullRun ? set.Entries.Where(e => !e.Played).Select(e => new CoverageEntry(e.Name, "rule", [], false, [], CoverageState.NotAutomated)) : [];
        return own.Values.Concat(wrappers).Concat(textOnly)
            .Where(c => fullRun || !c.Uncovered)
            .OrderBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A full run is complete when no counted entry is uncovered; a limited run always is.</summary>
    public static bool Complete(IReadOnlyList<CoverageEntry> coverage) => coverage.All(c => !c.Uncovered);
}
