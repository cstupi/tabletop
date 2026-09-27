# Task 18: The nine constraints as tests, and the record in `CLAUDE.md`

**Files:**
- Create: `tests/Tabletop.GameServer.Tests/InterfaceConstraintTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing new; the tests pin what the generic half must not contain.

Spec: action sequences → What the interface must not assume (the nine); Global Constraints (no ruleset name outside `Rules/GrimdarkFuture/`).

- [ ] **Step 1: Write the tests**

```csharp
using System.Reflection;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Action sequences → What the interface must not assume. Each test names the constraint it pins; 5 (granularity) and 8 (interrupts) are pinned by shape in Tasks 8 and 9.</summary>
public class InterfaceConstraintTests
{
    static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static IEnumerable<string> GenericSources() => new[] { "Interpreter", "Effects", "Dice", "Engine", "Properties" }
        .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, "server/game", d), "*.cs", SearchOption.AllDirectories));

    [Fact]
    public void Constraint_1_roll_names_are_rows_and_the_generic_half_names_none()
    {
        Assert.Equal(4, TestProperties.Core.Rolls.Count);
        foreach (var file in GenericSources())
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "\"hit\"", "\"defense\"", "\"morale\"", "\"casting\"", "\"Quality\"", "\"Defense\"", "\"Hold\"", "\"Advance\"", "\"Rush\"", "\"Charge\"", "\"fatigued\"", "\"shaken\"", "Regeneration", "Tough", "Transport" })
                Assert.False(text.Contains(forbidden), $"{Path.GetFileName(file)} names {forbidden}");
        }
        // The two weapon stat names StatReader needs are the one exception, whitelisted (Task 7).
        Assert.Contains("\"Range\"", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Effects/StatReader.cs")));
    }

    [Fact]
    public void Constraints_2_3_4_threshold_is_a_function_and_convention_and_cap_are_per_roll()
    {
        Assert.IsAssignableFrom<Delegate>(typeof(RollSpec).GetProperty(nameof(RollSpec.Threshold))!.GetValue(GrimdarkFutureRules.Instance.RollSpec(GfNames.Hit)));
        var specs = TestProperties.Core.Rolls.Select(r => GrimdarkFutureRules.Instance.RollSpec(r.Name)).ToList();
        Assert.All(specs, s => Assert.Equal(new RollConvention(6, null), s.Convention));
        var capped = new RollPlan("x", 4, [new RollModifier("a", 2), new RollModifier("b", 1)], new RollConvention(6, 1), null, new HashSet<int>());
        Assert.Equal(1, capped.NetModifier);                                                     // the cap is the convention's, applied after summing
        Assert.Equal(3, (capped with { Convention = new RollConvention(6, null) }).NetModifier);
        Assert.DoesNotContain(typeof(Roller).GetMethods(), m => m.Name.Contains("Six") || m.Name.Contains("D6"));
    }

    [Fact]
    public void Constraint_6_deal_wounds_reads_a_capacity_not_a_rule_name_outside_the_ruleset()
    {
        var generic = string.Join("\n", GenericSources().Select(File.ReadAllText));
        Assert.DoesNotContain("Tough", generic);
        Assert.Contains("ModelParameter(ctx.State, modelId, \"Tough\") ?? 1", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Rules/GrimdarkFuture/GfAttack.cs")));
    }

    [Fact]
    public void Constraint_7_morale_is_a_sequence_with_ruleset_owned_triggers()
    {
        Assert.Equal(GfNames.MoraleTest, GrimdarkFutureRules.Instance.Sequence(GfNames.MoraleTest).Name);
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.AttackSequence).Steps, s => s.Name.Contains("morale"));
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.ResolveHits).Steps, s => s.Name.Contains("morale"));
        Assert.Contains(typeof(IGameRules).GetMethods(), m => m.Name == nameof(IGameRules.EndOfTurnSequences));
    }

    [Fact]
    public void Constraint_9_an_ask_names_its_team_and_the_interpreter_never_assumes_the_actor()
    {
        Assert.Equal(typeof(IReadOnlyList<Guid>), typeof(Ask).GetProperty(nameof(Ask.Teams))!.PropertyType);
        var interpreter = File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/SequenceInterpreter.cs"));
        Assert.DoesNotContain("Defender", interpreter); Assert.DoesNotContain("Attacker", interpreter);
        Assert.Contains("Guid team", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/StepContext.cs")));   // Ask(..., Guid team, ...)
    }
}
```

- [ ] **Step 2: Edit `CLAUDE.md`**

**Ownership rule for `CLAUDE.md`** (plans execute in parallel): a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

In **Layout**, under the `server/game/` bullet the skeleton plan wrote, append these child bullets (after any child already there; do not edit the parent):

```
  - `Properties/` (the pinned set, its authored `Content/*.properties.json` form, the store and the startup seeder),
    `Dice/` (the seeded counter-based roll primitive), `Effects/` (the two passes over live properties), `Interpreter/`
    (frames from the log, steps, windows, the battlefield reader), `Engine/` (`RulesEngine`, the one `IRulesEngine`, and
    the scheduler seam `IScheduler`/`ISchedulerFactory`), `Scheduler/SchedulerTypes.cs` and `ActivationOption.cs` (the
    seam records the scheduler plan reads).
  - `Rules/GrimdarkFuture/` — OPR's sequences, rolls, hooks and names: the only place a rule is named.
```

In **Conventions**, append: "- The engine never appends or broadcasts and holds no state between calls: a step reads its position, values and history from the log and returns one outcome. Dice are a function of the game's stored `rng_seed` and the count of dice drawn in the effective log. Authored `Properties` are `server/game/Content/*.properties.json`, seeded by hash at startup; editing one makes a new set with a new hash — old games keep theirs."

- [ ] **Step 3: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d && dotnet build && dotnet test
find server tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix
git status --short
```

Expected: build clean; every test project green (this plan adds 87 tests to `Tabletop.GameServer.Tests`); the `find` prints nothing.

- [ ] **Step 4: Commit: `Pin the interpreter's nine constraints and record the engine in CLAUDE.md`.**
