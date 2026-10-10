# Task 4: The pick mechanism

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfPick.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs`, `GfHooks.cs` (`ReactionsAt`), `GfCasting.cs` (`Targets`),
  `GfRules.Sequences.cs` (registration)
- Test: `tests/Tabletop.GameServer.Tests/GfPickTests.cs` (new), `tests/Tabletop.Sim.Tests/AnswerPolicyTests.cs`

**Interfaces:**
- Consumes: pick entries (Task 3): `CatalogEntry { Kind: Model, Targeting: not null, Played: true }`, rows `once` with
  `at_point before-attacking`; `SpellTargeting.LineOfSight`.
- Produces: `GfNames.PickRuleSeq = "pick-rule"`, `GfNames.RuleTarget = "rule_target"` (the Ask question),
  `GfNames.RuleTargetStep = "rule-target"`; `GfPick.Key/Parse/Offers/Uses/PickRule` (README → Fixed interfaces);
  `GfCasting.Targets(GameState, Guid casterModel, SpellTargeting)`; a `react` option per carrying model at
  `before-attacking`, id `GfPick.Key(entry, model)`, label `"<entry> (<model slot name>): pick a target"`; the
  `pick-rule` frame's context `(unit, [], [], GfPick.Key(entry, model))`; its `rule-target` step completes with
  `Values["picked"] = 1` and one `GrantAdded(unit, Grant(id, rowId, entry, carrierUnit, 1))` per row.

Spec *Buff, Mark, Debuff: the pick*: "`GfHooks.ReactionsAt` … also offers one `use` reaction per carrying model whose
pick entry has not been used this activation. A Shaken unit is offered none" (p.10: idle units can't *"use any active
special rules, such as casting spells, using buffs/debuffs on units"*); "A new sequence `pick-rule`: an Ask
`rule_target` with `PieceSelection` over the eligible units (the spell-targeting reading, `GfCasting.SpellTargets`,
measured from the carrying model), pass allowed; then one `GrantAdded` per `once` row on the picked unit
(`Grant(Id, EffectId, EntryName, CasterUnitId, 1)`, as `GfCasting.cs:227`), and the use counted"; "Each carrying model
has its own use per activation (*"this model"*). The pick may be passed, as a cast may."

A use is counted when a target was picked (`picked = 1`), so a pass leaves the use, as a passed `pick-spell` leaves the
spell untried. A reaction is offered only when its pick has a target: `CastingWindowStep` repeats after a child, and a
pick with no target would finish and be offered again. A target already holding the entry's grants gets none (p.13:
*"Effects from multiple instances of the same special rule or spell don't stack"*, as `EffectsStep` reads it); the use
still counts.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/GfPickTests.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfPickTests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static EffectDocument Grant(string entry, EffectSubject subject) =>
        new(subject, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [BeforeAttacking], []);
    static readonly EntryDocument TestBuff = new("Test Buff", CatalogKind.Model, false, Aggregation.None, null, null, "Test buff.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false), [Grant("Stealth", EffectSubject.Self)]);
    static readonly EntryDocument TestMark = new("Test Mark", CatalogKind.Model, false, Aggregation.None, null, null, "Test mark.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [Grant("Furious", EffectSubject.Opponent)]);
    static readonly PropertySet Props = TestProperties.WithSpells(TestBuff, TestMark);

    /// <summary>Team 1's units at x = -12 (unit i at y = 12i - 20 + 1.5 per model), team 2's at x = +12: 24" apart, so nothing of team 2 is within 18".</summary>
    static Sim Holding(Tabletop.Content.Unit actor, IReadOnlyList<Tabletop.Content.Unit> friends, IReadOnlyList<Tabletop.Content.Unit> enemies)
    {
        var sim = new Sim(State([actor, .. friends], enemies), GrimdarkFutureRules.Instance, Props, new Dice.ScriptedDice([4, 4, 4, 4]));
        sim.Start(GfNames.HoldSeq, new SequenceContext(actor.Id, [], [], GfNames.Hold));
        return sim;
    }

    static IReadOnlyList<string> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices.Select(c => c.Id).ToList() : [];

    [Fact]
    public void Before_attacking_offers_a_use_per_carrying_model()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Equal(new[] { GfPick.Key("Test Buff", a.Models[0].Id), GfPick.Key("Test Buff", a.Models[1].Id) }.Order(), Offered(sim).Order());
        Assert.Equal("Test Buff (A 1): pick a target", ((Options)sim.AskFor(Samples.Team1).Legal).Choices.First(c => c.Id == GfPick.Key("Test Buff", a.Models[0].Id)).Label);
    }

    [Fact]
    public void A_shaken_unit_is_offered_no_use()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var sim = new Sim(State([a], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new Dice.ScriptedDice([4]));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Empty(Offered(sim));
    }

    [Fact]
    public void Using_it_asks_for_a_target_in_range_and_grants_each_once_row_to_the_picked_unit()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Holding(a, [b], [Unit("E", "4+", "4+", [], 1, [])]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", a.Models[0].Id)));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfNames.RuleTarget, ask.Question);
        Assert.True(ask.AllowPass);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), Assert.IsType<PieceSelection>(ask.Legal).Eligible.Order());   // friendly units within 12", the enemy is not
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        var grant = Assert.Single(sim.Unit(b.Id).Grants);
        Assert.Equal((Props.Entry("Test Buff")!.Effects.Single().Id, "Test Buff", a.Id, 1), (grant.EffectId, grant.EntryName, grant.CasterUnitId, grant.RemainingUses));
        Assert.Contains("Test Buff", sim.Steps(GfNames.RuleTargetStep).Single().Applied!);
        Assert.Equal(1, GfPick.Uses(sim.State, a.Id, GfPick.Key("Test Buff", a.Models[0].Id)));
    }

    [Fact]
    public void A_model_uses_its_pick_once_per_activation_and_another_carrier_still_may()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([a.Id]));
        Assert.Equal([GfPick.Key("Test Buff", a.Models[1].Id)], Offered(sim));
    }

    [Fact]
    public void Passing_the_target_uses_nothing_and_the_pick_is_offered_again()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        var key = GfPick.Key("Test Buff", a.Models[0].Id);
        sim.Answer(Samples.Team1, new Chosen(key));
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.Unit(a.Id).Grants);
        Assert.Equal(0, GfPick.Uses(sim.State, a.Id, key));
        Assert.Equal([key], Offered(sim));
    }

    [Fact]
    public void No_use_is_offered_when_no_unit_is_in_range()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Mark")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Empty(Offered(sim));
    }

    [Fact]
    public void A_target_already_holding_the_grants_gets_none_and_the_use_counts()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        foreach (var m in a.Models)
        {
            sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", m.Id)));
            sim.Answer(Samples.Team1, new SelectedPieces([a.Id]));
        }
        Assert.Single(sim.Unit(a.Id).Grants);
        Assert.Equal(1, GfPick.Uses(sim.State, a.Id, GfPick.Key("Test Buff", a.Models[1].Id)));
    }

    [Fact]
    public void A_key_splits_at_its_last_bar_so_an_entry_name_may_hold_one()
    {
        var m = Guid.CreateVersion7();
        Assert.Equal(("Hit | Run Buff", m), GfPick.Parse(GfPick.Key("Hit | Run Buff", m)));
        Assert.Null(GfPick.Parse("no model"));
    }
}
```

In `tests/Tabletop.Sim.Tests/AnswerPolicyTests.cs`, add (a guard: the policy answers every `PieceSelection`, so it passes
from the start):

```csharp
    [Fact]
    public void A_rule_target_ask_is_answered_with_one_eligible_unit()
    {
        var ask = new Ask(Guid.CreateVersion7(), "rule_target", "Stealth Buff: pick a target", [A], new PieceSelection([B, C], 1, 1), true, false, null, null);
        var answer = Assert.IsType<SelectedPieces>(Policy().Answer(ask, EmptyViewForOtherTests()));
        Assert.Contains(Assert.Single(answer.PieceIds), (Guid[])[B, C]);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfPickTests"`
Expected: build fails with `CS0103: The name 'GfPick' does not exist in the current context`.

- [ ] **Step 3: Names**

In `server/game/Rules/GrimdarkFuture/GfNames.cs`, after the `BeforeAttacking` line:

```csharp
    /// <summary>Army-book rules spec → the pick: the sequence a Buff, Mark or Debuff's use pushes, its Ask's question and its one step.</summary>
    public const string PickRuleSeq = "pick-rule", RuleTarget = "rule_target", RuleTargetStep = "rule-target";
```

- [ ] **Step 4: `GfCasting.Targets`**

In `server/game/Rules/GrimdarkFuture/GfCasting.cs`, replace `SpellTargets` with:

```csharp
    /// <summary>A spell's targets from its caster model (Targets).</summary>
    public static IReadOnlyList<Guid> SpellTargets(StepContext ctx, Guid casterModel, CatalogEntry spell) => Targets(ctx.State, casterModel, spell.Targeting!);

    /// <summary>
    /// p.15 first: containment answers before range and sight, so no spell crosses a container's wall either way, and a caster inside
    /// one reaches the units in there with it and the container itself with nothing to measure. Otherwise a unit is a target when one
    /// of its models is within range of the caster model and in its line of sight (p.13), or within range alone when the targeting
    /// says so (a Buff: "pick one friendly unit within 12""). A model-grain targeting offers those models. Spells and pick entries share it.
    /// </summary>
    public static IReadOnlyList<Guid> Targets(GameState state, Guid casterModel, SpellTargeting t)
    {
        var casterUnit = state.Projection.Models[casterModel].UnitId; var team = Teams.Of(state, casterUnit);
        var candidates = state.Projection.Units.Values.Where(u => (Teams.Of(state, u.Id) == team) == (t.Side == TargetSide.Friendly)).Select(u => u.Id).ToList();
        var result = new List<Guid>();
        var sight = new Lazy<Sight>(() => Sight.From(state, casterUnit));                                            // built once for every candidate model
        foreach (var unit in candidates)
        {
            var reaches = Battlefield.InsideReach(state, casterUnit, unit);
            var models = reaches switch
            {
                true => Lookup.AliveModels(state, unit),
                false => [],
                null => Lookup.LivingModels(state, unit).Where(m => Battlefield.Distance(state, casterModel, m) <= t.Range
                    && (m == casterModel || t.LineOfSight == false || sight.Value.CanSee(casterModel, m))).ToList(),
            };
            if (models.Count == 0) continue;
            if (t.Grain == TargetGrain.Unit) result.Add(unit); else result.AddRange(models);
        }
        return result;
    }
```

- [ ] **Step 5: `GfPick`**

Create `server/game/Rules/GrimdarkFuture/GfPick.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book Buffs, Marks and Debuffs (army-book rules spec → the pick): "Once per activation, before attacking, pick one … unit within
/// 12"/18" …, which gets X once (next time the effect would apply)". A carrying model offers a use at before-attacking; the use picks a
/// unit by the spell-targeting reading measured from that model and writes one grant per once row, which the grant reading spends.
/// </summary>
public static class GfPick
{
    public static Sequence PickRule() => new(GfNames.PickRuleSeq, [new TargetStep()]);

    /// <summary>"&lt;entry&gt;|&lt;model&gt;": the react option's id and the pick-rule frame's Action, as GfCasting's pick ids name a caster.</summary>
    public static string Key(string entry, Guid model) => $"{entry}|{model:N}";

    /// <summary>The entry and carrying model of a key; split at the last '|', since an entry name may hold one. Null when it is no key.</summary>
    public static (string Entry, Guid Model)? Parse(string? key)
    {
        var cut = key?.LastIndexOf('|') ?? -1;
        return cut > 0 && Guid.TryParseExact(key!.AsSpan(cut + 1), "N", out var model) ? (key[..cut], model) : null;
    }

    static bool IsPick(CatalogEntry e) => e is { Kind: CatalogKind.Model, Targeting: not null, Played: true };

    /// <summary>
    /// Each living model of the unit with each pick entry it carries that it has not used this activation and that has a target now. A model
    /// inside a container has no position to measure from, so it offers none.
    /// </summary>
    public static IReadOnlyList<(CatalogEntry Entry, Guid Model)> Offers(GameState state, PropertySet props, Guid unit) =>
        Lookup.LivingModels(state, unit)
            .SelectMany(m => state.Projection.Models[m].Slot.Properties.Select(p => props.Entry(p.Name)).OfType<CatalogEntry>().Where(IsPick)
                .DistinctBy(e => e.Name).Select(e => (Entry: e, Model: m)))
            .Where(x => Uses(state, unit, Key(x.Entry.Name, x.Model)) == 0 && GfCasting.Targets(state, x.Model, x.Entry.Targeting!).Count > 0)
            .ToList();

    /// <summary>The picks this key made since the unit's activation began (its last root action sequence, which only the scheduler starts):
    /// rule-target steps of its pick-rule frames that picked a target.</summary>
    public static int Uses(GameState state, Guid unit, string key)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && s.Context.ActorUnitId == unit);
        var frames = new HashSet<Guid>();
        var uses = 0;
        foreach (var e in log.Skip(Math.Max(0, since)))
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: GfNames.PickRuleSeq } s when s.Context.ActorUnitId == unit && s.Context.Action == key: frames.Add(s.FrameId); break;
                case StepCompleted { Step: GfNames.RuleTargetStep } c when frames.Contains(c.FrameId) && c.Values?.GetValueOrDefault("picked") == 1: uses++; break;
            }
        return uses;
    }

    /// <summary>Ask for the target; on an answer, grant every once row to it ("a once-row's conditions say when the grant applies, not whether
    /// it is written", as EffectsStep). A pass, or an answer naming nothing offered, uses nothing.</summary>
    sealed class TargetStep : IStep
    {
        public string Name => GfNames.RuleTargetStep;
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Parse(ctx.Frame.Context.Action) is not var (name, model) || ctx.Properties.Entry(name) is not { } entry || !IsPick(entry)
                || !ctx.Projection.Models.TryGetValue(model, out var carrier) || carrier.Location is not OnTable) return new Finish([]);
            if (ctx.History.LastClosed() is { } closed)
            {
                if (GfAnswers.Pieces(closed.Single, closed.Ask) is not [var target, ..]) return new Finish([]);
                var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
                // p.13: "Effects from multiple instances of the same special rule or spell don't stack".
                var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == entry.Name);
                IReadOnlyList<StateChange> grants = already ? []
                    : entry.Effects.Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, entry.Name, unit, 1))).ToList();
                ctx.Applied.Add(entry.Name);
                return new Complete(grants, new Dictionary<string, int> { ["picked"] = 1 });
            }
            var eligible = GfCasting.Targets(ctx.State, model, entry.Targeting!);
            if (eligible.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask(GfNames.RuleTarget, $"{entry.Name}: pick a target", ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, 1), allowPass: true)]);
        }
    }
}
```

- [ ] **Step 6: Offer the use and register the sequence**

In `server/game/Rules/GrimdarkFuture/GfHooks.cs`, `ReactionsAt`, inside `foreach (var unit in units)` after the
`if (Shaken(…) …) continue;` line:

```csharp
            // Army-book rules spec → the pick: one use per carrying model that has not used it this activation and has a target now.
            if (point == GfNames.BeforeAttacking)
                foreach (var (entry, model) in GfPick.Offers(state, props, unit))
                    reactions.Add(new Reaction(team, GfPick.Key(entry.Name, model), $"{entry.Name} ({state.Projection.Models[model].Slot.Name}): pick a target",
                        GfNames.PickRuleSeq, new SequenceContext(unit, [], [], GfPick.Key(entry.Name, model))));
```

and extend the method's summary with: *"At before-attacking each carrying model also offers its Buff, Mark or Debuff
(GfPick), pushing `pick-rule`."*

In `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`, add `GfPick.PickRule(),` after `GfCasting.SpellTest(),`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfPickTests|FullyQualifiedName~GfCastingTests|FullyQualifiedName~WindowTests|FullyQualifiedName~ArmyRuleHookTests" && dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~AnswerPolicyTests"`
Expected: PASS.

- [ ] **Step 8: Run both suites**

Run: `dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add server/game/Rules/GrimdarkFuture/GfPick.cs server/game/Rules/GrimdarkFuture/GfNames.cs server/game/Rules/GrimdarkFuture/GfHooks.cs \
  server/game/Rules/GrimdarkFuture/GfCasting.cs server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs \
  tests/Tabletop.GameServer.Tests/GfPickTests.cs tests/Tabletop.Sim.Tests/AnswerPolicyTests.cs
git commit -m "Offer an army-book Buff, Mark or Debuff before attacking and grant it to the unit picked"
```
