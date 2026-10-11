# Task 7: Coverage: not automated and by pattern

**Files:**
- Modify: `server/game/Properties/PropertiesSeeder.cs` (`EmbeddedPart`)
- Modify: `sim/Suite/Coverage.cs`, `sim/Suite/SuiteReport.cs` (`Table`), `sim/Suite/SuiteRunner.cs` (`RunAsync`),
  `sim/Suite/TestGame.cs` (`Pattern`)
- Modify: `sim/AGENTS.md` (*The rules suite* → Coverage)
- Test: `tests/Tabletop.Sim.Tests/Suite/PatternCoverageTests.cs` (new)

**Interfaces:**
- Consumes: `CatalogEntry.Played` (Task 1), `PropertiesSeeder.GeneratedPart` (Task 6b).
- Produces: `PropertiesSeeder.EmbeddedPart(string fileName)`; `CoverageState { Own, Pattern, NotAutomated }`;
  `CoverageEntry(..., CoverageState State = Own)` with `Uncovered` and `Counts`; `Coverage.Of(set, runs, fullRun,
  generated, passed)`; `TestGame.Pattern` (`"Aura"`, `"Buff"`, `"Mark"`, `"Debuff"`); the report's `covered by pattern`
  lines and its `not automated (N): …` line.

Spec *Coverage*: "**Text-only entries** (`Played == false`) are listed as not automated and not counted. The report
prints the count and the names. They never fail a run. **Generated wrappers** are covered by pattern: covered when their
pattern's game passes and X is covered. The report shows them as "covered by pattern". **Hand-authored entries** keep
their own game."

A generated wrapper is an entry with effects in the embedded generated part (`PropertiesSeeder.EmbeddedPart(GeneratedPart)`):
the sim reads which entries the generator wrote, not a rule shape. Its pattern is its name's last word; its X is its
one row's `TargetEntry`. "X is covered" is X's own coverage: covered by a game and fired in all of them.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.Sim.Tests/Suite/PatternCoverageTests.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

public class PatternCoverageTests
{
    static readonly PropertySet Set = PropertiesJson.ToSet(PropertiesJson.Parse("""
    { "name": "S", "gameRules": "grimdark-future", "rolls": [ "hit" ],
      "entries": [
        { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "S.", "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1 } ] },
        { "name": "Stealth Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Stealth.", "effects": [ { "subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Stealth" } ] },
        { "name": "Banner", "kind": "model", "aggregation": "any", "ruleText": "", "played": false } ] }
    """), Guid.CreateVersion7);
    static readonly IReadOnlySet<string> Generated = new HashSet<string> { "Stealth Aura" };
    static readonly CompiledScenario Stalemate = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate"));
    static readonly Guid Frame = Guid.CreateVersion7();

    static GameEvent Shot(string source) => new(1, null, new StepCompleted(Frame, "roll-to-hit", null,
        [new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier(source, -1)], [new Die(4, 3, false)])], []), null);

    static SuiteGameRun Run(string name, IReadOnlyList<string> covers, string? pattern, params GameEvent[] log) =>
        new(new TestGame(name, "army:x", Stalemate.Scenario, covers, [], []) { Pattern = pattern }, new RunOutcome.RoundLimit(1), log,
            new EventApplier(Stalemate.Board, Stalemate.Info).Projection, []);

    [Fact]
    public void A_text_only_entry_is_listed_on_a_full_run_and_never_uncovered()
    {
        var coverage = Coverage.Of(Set, [], fullRun: true, Generated);
        var banner = coverage.Single(c => c.Name == "Banner");
        Assert.Equal((CoverageState.NotAutomated, false, false), (banner.State, banner.Counts, banner.Uncovered));
        Assert.DoesNotContain(Coverage.Of(Set, [], fullRun: false, Generated), c => c.Name == "Banner");
    }

    [Fact]
    public void A_wrapper_is_covered_by_its_patterns_passing_game_when_its_base_is_covered()
    {
        var runs = new[] { Run("stealth", ["Stealth"], null, Shot("Stealth")), Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth")) };
        var aura = Coverage.Of(Set, runs, fullRun: true, Generated, new HashSet<string> { "stealth", "aura" }).Single(c => c.Name == "Stealth Aura");
        Assert.Equal((CoverageState.Pattern, false, true), (aura.State, aura.Uncovered, aura.Fired));
        Assert.Equal(["aura"], aura.Games);
        Assert.True(Coverage.Complete(Coverage.Of(Set, runs, fullRun: true, Generated)));
    }

    [Fact]
    public void A_wrapper_is_uncovered_without_its_patterns_game_or_its_base_and_unfired_when_the_game_failed()
    {
        var stealth = Run("stealth", ["Stealth"], null, Shot("Stealth"));
        var game = Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth"));
        Assert.True(Coverage.Of(Set, [stealth], fullRun: true, Generated).Single(c => c.Name == "Stealth Aura").Uncovered);
        Assert.True(Coverage.Of(Set, [game with { Game = game.Game with { Covers = [] } } , Run("other", ["Stealth Aura"], "Buff")], fullRun: true, Generated)
            .Single(c => c.Name == "Stealth Aura").Uncovered);                                                         // Stealth uncovered, no Aura game for the Buff game
        var failed = Coverage.Of(Set, [stealth, game], fullRun: true, Generated, new HashSet<string> { "stealth" }).Single(c => c.Name == "Stealth Aura");
        Assert.Equal((false, false), (failed.Uncovered, failed.Fired));
    }

    [Fact]
    public void The_report_shows_covered_by_pattern_and_lists_the_not_automated_names()
    {
        var runs = new[] { Run("stealth", ["Stealth"], null, Shot("Stealth")), Run("aura", ["Stealth Aura"], "Aura", Shot("Stealth")) };
        var coverage = Coverage.Of(Set, runs, fullRun: true, Generated);
        var table = new SuiteReport(Guid.CreateVersion7(), [], coverage, true).Table();
        Assert.Contains("  rule   Stealth Aura  covered by pattern  (aura)", table);
        Assert.Contains("not automated (1): Banner", table);
        Assert.Contains("coverage (2/2 covered, 1 by pattern, 0 covered but not fired; 1 not automated)", table);
    }

    [Fact]
    public void The_generated_part_is_read_from_the_server_assembly()
    {
        Assert.Null(PropertiesSeeder.EmbeddedPart("no-such.part.json"));
        Assert.NotNull(PropertiesSeeder.EmbeddedPart("grimdark-future-army-rules.part.json"));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~PatternCoverageTests"`
Expected: build fails with `CS0103: The name 'CoverageState' does not exist` and `'TestGame' does not contain a
definition for 'Pattern'`.

- [ ] **Step 3: `EmbeddedPart`**

In `server/game/Properties/PropertiesSeeder.cs`, after `GeneratedPart`:

```csharp
    /// <summary>One embedded `*.part.json` by file name, parsed; null when the assembly embeds none of that name.</summary>
    public static PropertySetDocument? EmbeddedPart(string fileName)
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        if (asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal)) is not { } name) return null;
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return PropertiesJson.Parse(r.ReadToEnd());
    }
```

- [ ] **Step 4: `TestGame.Pattern`**

In `sim/Suite/TestGame.cs`, inside `TestGame` after `Known`:

```csharp
    /// <summary>The pattern this game plays for every generated wrapper of it (army-book rules spec → Coverage): Aura, Buff, Mark or Debuff.</summary>
    public string? Pattern { get; init; }
```

and extend the record's summary with: *"Pattern: the Army Forge pattern whose generated wrappers this game covers."*

- [ ] **Step 5: Coverage states**

Replace the body of `sim/Suite/Coverage.cs` after the `using` line:

```csharp
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
```

- [ ] **Step 6: The runner and the report**

In `sim/Suite/SuiteRunner.cs`, add as a static member:

```csharp
    /// <summary>The wrappers scripts/ArmyBooks generated (the generated part's entries with effects), which coverage counts by pattern.</summary>
    static readonly IReadOnlySet<string> Generated =
        PropertiesSeeder.EmbeddedPart(PropertiesSeeder.GeneratedPart)?.Entries.Where(e => e.Effects.Count > 0).Select(e => e.Name).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>();
```

and replace the coverage lines at the end of `RunAsync`:

```csharp
        var coverage = Coverage.Of(properties ?? ScenarioCompiler.CoreProperties, runs, fullRun: options.Only.Count == 0, Generated,
            results.Where(r => r.Passed).Select(r => r.Name).ToHashSet(StringComparer.Ordinal));
        return new SuiteReport(runId, results, coverage,
            results.All(r => r.Passed) && Coverage.Complete(coverage) && coverage.Where(c => c.Counts).All(c => c.Uncovered || c.Fired));
```

In `sim/Suite/SuiteReport.cs`, `Table`, replace from `var uncovered = …` to the final `lines.Add(...PASSED...)` with:

```csharp
        var counted = Coverage.Where(c => c.Counts).ToList();
        var uncovered = counted.Count(c => c.Uncovered);
        var silent = counted.Count(c => !c.Uncovered && !c.Fired);
        var byPattern = counted.Count(c => c.State == CoverageState.Pattern && !c.Uncovered);
        var textOnly = Coverage.Where(c => !c.Counts).Select(c => c.Name).ToList();
        lines.Add($"coverage ({counted.Count - uncovered}/{counted.Count} covered, {byPattern} by pattern, {silent} covered but not fired; {textOnly.Count} not automated)");
        foreach (var c in counted)
        {
            var state = c.Uncovered ? "UNCOVERED"
                : c.State == CoverageState.Pattern ? c.Fired ? "covered by pattern" : "PATTERN NOT PASSED"
                : c.Fired ? "fired" : "NOT FIRED";
            var exact = c.Exact.Count == 0 ? "" : $"  exact {c.Exact.Count(x => x.Passed)}/{c.Exact.Count}"
                + string.Concat(c.Exact.Where(x => !x.Passed).Select(x => $"  FAIL {x.Game}: {x.Check}"));
            lines.Add($"  {c.Kind,-5}  {c.Name}  {state}{(c.Uncovered ? "" : $"  ({string.Join(", ", c.Games)})")}{exact}");
        }
        if (textOnly.Count > 0) lines.Add($"not automated ({textOnly.Count}): {string.Join(", ", textOnly)}");

        lines.Add($"{(Passed ? "PASSED" : "FAILED")}: {Games.Count(g => g.Passed)} of {Games.Count} games passed, {known} failing on known defects, {unexpected} unexpected; "
            + $"{counted.Count - uncovered} of {counted.Count} entries covered, {textOnly.Count} not automated; run {RunId:N}");
        return string.Join("\n", lines);
```

- [ ] **Step 7: Run the sim tests**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~PatternCoverageTests|FullyQualifiedName~FiredAndCoverageTests|FullyQualifiedName~SuiteRunnerTests"`
Expected: PASS. If a `SuiteRunnerTests` assertion matches the old final line (`entries covered; run`), update its
expected text to the new one, `… entries covered, 0 not automated; run …`.

- [ ] **Step 8: Module notes**

In `sim/AGENTS.md`, *The rules suite*, replace the *Coverage* bullet with:

```markdown
- Coverage: every entry of the shipped set, with the games that cover it, whether it fired in all of them, and its exact checks. A
  run fails on a covered entry that did not fire; a full run also fails on an uncovered one, an `--only` run never does. A text-only
  entry (`played: false`, generated) is listed as not automated and never counted. A generated wrapper (an entry with effects in
  `grimdark-future-army-book-rules.part.json`) is covered by pattern: by the games whose `Pattern` is its name's last word, when
  they passed and its base is covered and fired.
```

- [ ] **Step 9: Run the sim suite and commit**

Run: `dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS.

```bash
git add server/game/Properties/PropertiesSeeder.cs sim/Suite/Coverage.cs sim/Suite/SuiteReport.cs sim/Suite/SuiteRunner.cs sim/Suite/TestGame.cs \
  sim/AGENTS.md tests/Tabletop.Sim.Tests/Suite/PatternCoverageTests.cs
git commit -m "Count a generated wrapper covered by its pattern's game; list text-only entries as not automated"
```
