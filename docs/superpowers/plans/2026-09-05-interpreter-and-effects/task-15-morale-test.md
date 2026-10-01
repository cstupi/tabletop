# Task 15: The morale test

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMorale.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfMoraleTests.cs`

**Interfaces:**
- Consumes: Tasks 7, 11, 12.
- Produces: `GfMorale.MoraleTest()` (`morale-test`, one step `test`). Context: `ActorUnitId` = the unit testing, `Action` = `"wounds"` (end of activation) or `"melee"` (lost a melee). Values: `passed` (0/1), `routed` (0/1).
- p.10: one Quality test; a Shaken unit always fails (no die is rolled); `Fearless` (all models) on a failure, a Shaken unit's automatic one included (p.13), rolls the property's own roll (`Fearless`, threshold from the effect row) and a success counts as passed, so a Shaken Fearless unit rolls only the Fearless die. A failed wounds test → Shaken. A failed melee test → Rout when at half or less of starting size (or Tough value, single model) — every model to the shelf — otherwise Shaken. A unit with no living models finishes without testing.

Spec: action sequences → Grimdark Future's sequences (Morale test; removal only on the melee path); rulebook p.10 (both morale paragraphs), p.13 `Fearless`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMoraleTests
{
    static Sim WithLosses(Tabletop.Content.Unit u, int dead, params int[] dice)
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [u]), dice);
        sim.Append(u.Models.Take(dead).Select(m => (EventPayload)new ModelsMoved([new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf())])));
        return sim;
    }

    [Fact]
    public void A_failed_wounds_test_shakes_and_a_passed_one_does_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var sim = WithLosses(u, 2, 3);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal((GfNames.Morale, 4, false), (step.Rolls[0].Roll, step.Rolls[0].Threshold, step.Rolls[0].Dice[0].Success));
        Assert.Contains(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        Assert.Equal(2, sim.Alive(u.Id));
        var ok = WithLosses(u, 2, 5);
        ok.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        Assert.DoesNotContain(GfNames.Shaken, ok.Unit(u.Id).Conditions);
        Assert.Equal(1, ok.Steps("test").Single().Values!["passed"]);
    }

    [Fact]
    public void A_failed_melee_test_at_half_or_less_routs_and_above_half_shakes()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var routed = WithLosses(u, 2, 1);
        routed.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(0, routed.Alive(u.Id)); Assert.Equal(1, routed.Steps("test").Single().Values!["routed"]);
        var shaken = WithLosses(u, 1, 1);
        shaken.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(3, shaken.Alive(u.Id)); Assert.Contains(GfNames.Shaken, shaken.Unit(u.Id).Conditions);
    }

    [Fact]
    public void Fearless_rerolls_a_failure_at_four_plus_and_a_shaken_unit_rolls_no_quality_die()
    {
        var u = Unit("U", "4+", "4+", [], 3, [P("Fearless")]);
        var sim = WithLosses(u, 1, 1, 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal([GfNames.Morale, "Fearless"], step.Rolls.Select(r => r.Roll));
        Assert.Equal(4, step.Rolls[1].Threshold);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);

        var already = WithLosses(Unit("S", "4+", "4+", [], 2, []), 0, 6);
        var s = already.State.Board.Armies[1].Snapshot.Units[0].Id;
        already.Append([new ConditionSet(new Subject(SubjectKind.Unit, s), GfNames.Shaken, true)]);
        already.Start(GfNames.MoraleTest, new SequenceContext(s, [], [], "melee"));
        Assert.Empty(already.Steps("test").Single().Rolls);                                     // no die: Shaken always fails
        Assert.Equal(2, already.Alive(s));                                                     // above half: a failed melee test shakes, and it already is

        var fearless = Unit("F", "4+", "4+", [], 2, [P("Fearless")]);
        var shakenFearless = WithLosses(fearless, 0, 5);
        shakenFearless.Append([new ConditionSet(new Subject(SubjectKind.Unit, fearless.Id), GfNames.Shaken, true)]);
        shakenFearless.Start(GfNames.MoraleTest, new SequenceContext(fearless.Id, [], [], "wounds"));
        var f = shakenFearless.Steps("test").Single();
        Assert.Equal(["Fearless"], f.Rolls.Select(r => r.Roll));                               // the automatic failure still triggers Fearless
        Assert.Equal(1, f.Values!["passed"]);
    }

    [Fact]
    public void A_unit_with_no_living_models_finishes_without_testing()
    {
        var u = Unit("U", "4+", "4+", [], 2, []);
        var sim = WithLosses(u, 2, 1);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Empty(sim.Steps("test").Single().Rolls);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfMorale.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMorale
{
    public static Sequence MoraleTest() => new(GfNames.MoraleTest, [new TestStep()]);

    /// <summary>
    /// p.10: one Quality test; a Shaken unit "always fails", so no die is rolled for it. p.13 Fearless: "fails a morale test, roll one die.
    /// On a 4+ it counts as passed instead", which a Shaken unit's automatic failure also triggers. A failed test after a melee routs a unit at
    /// half or less of its starting size (every model to the shelf) and shakes it otherwise; after wounds it only shakes.
    /// </summary>
    sealed class TestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var living = Lookup.AliveModels(ctx.State, unit);                                     // in play, not positioned: a unit carried in a transport still takes its test
            if (living.Count == 0) return new Finish([]);
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit)));
            var passed = false;
            if (!ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken))
                passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.MoralePlan(ctx, live), 1)) == 1;
            if (!passed)
                foreach (var (source, roll) in RollEffects.OwnRolls(live, Side.Attacker, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.MoraleTest }))
                    passed |= Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Morale).Convention, null, new HashSet<int>()), roll.EffectiveAmount(source.Parameter))) > 0;
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0, ["routed"] = 0 };
            if (passed) return new Complete([], values);
            if (ctx.Frame.Context.Action == "melee" && GfHooks.AtHalfOrLess(ctx.State, ctx.Properties, unit))
            {
                values["routed"] = 1;
                return new Complete([new ModelsMoved(living.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())], values);
            }
            return new Complete([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true)], values);
        }
    }
}
```

`GfRules._sequences` gains `GfMorale.MoraleTest()`.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfMoraleTests`; expect 4 passed. Commit: `Add the morale test: Shaken, Rout on the melee path, Fearless`.**
