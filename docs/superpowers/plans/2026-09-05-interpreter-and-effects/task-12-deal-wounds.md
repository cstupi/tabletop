# Task 12: deal-wounds

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (deal-wounds; Task 13 adds resolve-hits and attack-sequence to the same file)
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `GfAttack.DealWounds()`)
- Test: `tests/Tabletop.GameServer.Tests/GfDealWoundsTests.cs`

**Interfaces:**
- Consumes: Tasks 6–11; `Allocation`, `Allocated`, `SelectedPieces`, `WoundsSet`, `ModelsMoved`, `WoundsThisActivationSet` (Protocol).
- Produces: `static class GfAttack` with `Sequence DealWounds()` (name `deal-wounds`, one step `assign`; Task 19 adds `disgorge`); `static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IRL<Guid> weapons, AttackKind? kind, string? action)` (range from `Battlefield.UnitDistance`, terrain from `Battlefield.TerrainUnder(target)`); `static int WoundsToDeal(FrameView frame)` (failures of every `defense` roll in the parent frame's completed steps, or the parent's `Values["wounds"]` when it rolled none — dangerous terrain); `static IRL<Guid> EligibleForNextWound(GameState, PropertySet, Guid unit)` (p.15, in that order: heroes last even if already wounded, then a wounded Tough model first until it dies, over `Lookup.AliveModels`, so a unit inside a container is wounded like any other); `static Guid? TakedownModel(FrameView frame)` (the model a `Takedown` group named, read off the nearest ancestor `attack-sequence` frame's closed `pick-target-model` answer; Task 13 adds the step that writes it).
- **Task 19 comes back to this sequence.** `DealWounds()` gains a second step, `disgorge`, and `assign`'s casualty line gains one condition: a model at its capacity that is a container holding somebody is *not* shelved here, because p.15 places its occupants *"before removal"*. Nothing else in this task changes, and a container with nobody inside is an ordinary casualty.
- Context of a `deal-wounds` frame: `ActorUnitId` = the attacker (null when terrain wounds), `TargetUnitIds = [target]`, `WeaponIds` = the attacking group's weapons (so `Deadly`, `Bane`'s and `Rending`'s suppression of `Regeneration` are live), `Action` = the attacker's action. The Ask is `Allocation(Targets = eligible models, Amount = wounds)` to the target's team, and any member of that team may answer it; the answer is applied as given (an out-of-set allocation accepted under `warn`/`silent` is applied too — nothing is an invariant). **A takedown wound opens no Ask**: when `TakedownModel` names a living model of the target, every wound of that group is assigned to that one model and the defender is not asked (p.14 `Takedown`; ruling 7). What that skips is exactly what building the prompt's targets decides — `EligibleForNextWound`, so p.15's wounded-`Tough`-model-first and heroes-last, and p.8's free choice of removal order. Everything after the assignment is unchanged: `Deadly` multiplies, `Regeneration` rolls, the capacity read shelves the model, and wounds past that capacity are lost exactly as they are when a defender over-allocates to one model. A name that is not a living model of the target — which only a menu edit can produce — is ignored and the ordinary Ask opens. Per assigned wound: multiplied by `WoundMultiplier` (`Deadly(X)`, first by ordinal 0, no carry-over past the model's capacity); `Regeneration` live on the target unit, which 3.5.1 needs every model to carry → one die at 5+ per wound (a `RollRecord` named `Regeneration`), success ignores it; the model's `Wounds` rises; at `Tough(X)` (default 1) it goes to the shelf. `WoundsThisActivation` on the target rises by the wounds that landed.

Spec: action sequences → Grimdark Future's sequences (deal-wounds: `Tough` stacking, heroes last, `Deadly` first and no carry-over, `Regeneration` per wound unless suppressed, casualties to the shelf, wounds-this-activation); rulebook p.8 ("the defending player may remove models from the target in any order"), p.13 `Deadly`, p.14 `Regeneration`, `Tough`, `Hero`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfDealWoundsTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does. A named model makes that parent an attack-sequence whose pick-target-model step answered, which is what a Takedown group leaves behind (Task 13).</summary>
    static Sim Start(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, Guid? takedown = null, params Guid[] weapons)
    {
        var sim = Sim.Gf(State([attacker], [target]), dice);
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, takedown is null ? "test-parent" : GfNames.AttackSequence, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        if (takedown is { } model) sim.Append([new StepCompleted(parent, "pick-target-model", new SelectedPieces([model]), [], [])]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold));
        return sim;
    }

    [Fact]
    public void The_defender_allocates_and_tough_models_absorb_until_they_die()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, []) with { Models = [.. Unit("T", "4+", "4+", [], 2, []).Models, Unit("Big", "4+", "4+", [], 1, [P("Tough", 3)]).Models[0] with { Ordinal = 3 }] };
        var sim = Start(a, t, 3, [1]);
        var ask = sim.AskFor(Samples.Team2);
        var legal = Assert.IsType<Allocation>(ask.Legal);
        Assert.Equal((3, 3), (legal.Amount, legal.Targets.Count));
        var (plain, big) = (t.Models[0].Id, t.Models[2].Id);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [plain] = 1, [big] = 2 }));
        Assert.Equal(2, sim.Alive(t.Id));
        Assert.IsType<OnShelf>(sim.Model(plain).Location);
        Assert.Equal(2, sim.Model(big).Wounds);
        Assert.Equal(3, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal([big], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));                    // a wounded Tough model takes the next wound (p.15)
    }

    [Fact]
    public void Deadly_multiplies_per_model_without_carry_over_and_heroes_are_eligible_last()
    {
        var gun = Gun("Cannon", "24\"", 1, P("Deadly", 3));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var weapons = a.Models[0].Weapons.Select(w => w.Id).ToArray();
        var sim = Start(a, t, 1, [1], null, weapons);
        Assert.Equal([t.Models[0].Id], Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Targets);      // the hero waits
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(t.Id));
        Assert.Equal(0, sim.Model(t.Models[1].Id).Wounds);                                                            // nothing carried over
        Assert.Equal(1, sim.Unit(t.Id).WoundsThisActivation);                                                         // one wound landed on a one-wound model
    }

    [Fact]
    public void A_wounded_hero_is_assigned_wounds_last_even_though_it_is_the_most_wounded_Tough_model()
    {
        var gun = Gun("Long Rifle", "30\"", 1, P("Takedown"));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var (plain, hero) = (t.Models[0].Id, t.Models[1].Id);
        var sim = Start(a, t, 1, [1], hero, a.Models[0].Weapons.Select(w => w.Id).ToArray());
        Assert.Equal(1, sim.Model(hero).Wounds);                                                                      // the takedown named it, so no prompt and no ordering
        Assert.Equal(2, sim.Alive(t.Id));
        Assert.Equal([plain], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));                    // p.15: last even if already wounded, so the Tough ordering never reaches it
    }

    [Fact]
    public void Regeneration_rolls_per_wound_unless_the_weapon_ignores_it_or_a_model_lacks_it()
    {
        var troll = Unit("Troll", "4+", "4+", [], 1, [P("Regeneration"), P("Tough", 6)]);
        var plain = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var sim = Start(plain, troll, 2, [5, 2]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [troll.Models[0].Id] = 2 }));
        var regen = sim.Steps("assign").Single().Rolls.Single();
        Assert.Equal(("Regeneration", 5, 2), (regen.Roll, regen.Threshold, regen.Dice.Count));
        Assert.Equal(1, sim.Model(troll.Models[0].Id).Wounds);
        Assert.Equal(1, sim.Unit(troll.Id).WoundsThisActivation);
        var baner = Unit("B", "4+", "4+", [], 1, [], Gun("Dart", "24\"", 1, P("Bane")));
        var sim2 = Start(baner, troll, 2, [5, 5], null, baner.Models[0].Weapons.Select(w => w.Id).ToArray());
        sim2.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [troll.Models[0].Id] = 2 }));
        Assert.Empty(sim2.Steps("assign").Single().Rolls);
        Assert.Equal(2, sim2.Model(troll.Models[0].Id).Wounds);
        var mixed = Unit("Pack", "4+", "4+", [], 2, []) with { Models = [Unit("R", "4+", "4+", [], 1, [P("Regeneration"), P("Tough", 6)]).Models[0], Unit("P", "4+", "4+", [], 1, [P("Tough", 6)]).Models[0] with { Ordinal = 2 }] };
        var sim3 = Start(plain, mixed, 2, [5, 5]);
        sim3.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [mixed.Models[0].Id] = 2 }));
        Assert.Empty(sim3.Steps("assign").Single().Rolls);                                                      // aggregation all: 3.5.1 needs "a unit where all models have this rule"
        Assert.Equal(2, sim3.Model(mixed.Models[0].Id).Wounds);
    }

    [Fact]
    public void A_takedown_wound_names_its_model_and_opens_no_prompt_so_no_ordering_is_consulted()
    {
        var gun = Gun("Long Rifle", "30\"", 1, P("Takedown"), P("Deadly", 3));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var (plain, hero) = (t.Models[0].Id, t.Models[1].Id);
        var sim = Start(a, t, 1, [1], hero, a.Models[0].Weapons.Select(w => w.Id).ToArray());
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                     // heroes-last is decided while building the prompt's targets, and there is no prompt
        Assert.IsType<OnShelf>(sim.Model(hero).Location);                                                 // Deadly(3) still multiplies, into a Tough(3) hero
        Assert.Equal(0, sim.Model(plain).Wounds);
        Assert.Equal(3, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Wounds_past_the_named_models_capacity_are_lost_and_a_name_outside_the_target_falls_back_to_the_prompt()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = Start(a, t, 2, [1], t.Models[0].Id);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal((1, 0), (sim.Alive(t.Id), sim.Model(t.Models[1].Id).Wounds));                        // the second wound is lost, as an over-allocation to one model is
        Assert.Equal(1, sim.Unit(t.Id).WoundsThisActivation);
        var stray = Start(a, t, 1, [1], a.Models[0].Id);                                                  // not a living model of the target: only a menu edit writes this
        Assert.Equal(2, Assert.IsType<Allocation>(stray.AskFor(Samples.Team2).Legal).Targets.Count);
    }

    [Fact]
    public void Zero_wounds_finishes_without_asking()
    {
        var sim = Start(Unit("A", "4+", "4+", [], 1, []), Unit("T", "4+", "4+", [], 2, []), 0, [1]);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Empty(sim.State.Projection.Stack.Where(f => f.Sequence == GfNames.DealWounds));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfAttack.cs` (deal-wounds part)**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep()]);

    public static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IReadOnlyList<Guid> weapons, AttackKind? kind, string? action)
    {
        var state = ctx.State;
        if (actor is not { } a) return new Interaction(state, ctx.Properties, Party.Whole(state, target), null, kind, action, null, Battlefield.TerrainUnder(state, target));
        var models = weapons.Count > 0 ? state.Projection.Models.Values.Where(m => m.Slot.Weapons.Any(w => weapons.Contains(w.Id))).Select(m => m.Id).ToList() : Lookup.LivingModels(state, a);
        return new Interaction(state, ctx.Properties, Party.Of(state, a, models, weapons), Party.Whole(state, target), kind, action, Battlefield.UnitDistance(state, a, target), Battlefield.TerrainUnder(state, target));
    }

    /// <summary>Failures of the parent's defense rolls as they now stand (a reroll between steps counts), or the parent's declared wounds.</summary>
    public static int WoundsToDeal(FrameView frame)
    {
        var parent = frame.Parent ?? throw new InvalidOperationException("deal-wounds needs a parent frame");
        var defense = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        return defense.Count > 0 ? defense.Sum(Dice.RollOutcome.Failures) : parent.Value("wounds");
    }

    /// <summary>
    /// The model a Takedown group named for its wound (p.14). Values holds ints only and SequenceContext has no model, so the choice is
    /// read back off the nearest ancestor attack-sequence frame, the way HitsFrom reads the parent's rolls. Null for every other group.
    /// </summary>
    public static Guid? TakedownModel(FrameView frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.AttackSequence)
                return f.Frame.Steps.FirstOrDefault(s => s.Step == "pick-target-model")?.Answer is SelectedPieces { PieceIds.Count: 1 } p ? p.PieceIds[0] : null;
        return null;
    }

    /// <summary>p.15, in the order the rule writes them: heroes are assigned wounds last even if already wounded, and among the rest the most-wounded Tough model keeps taking wounds until it dies. The in-play reading, so a unit inside a wrecked transport can be wounded before it is placed (Task 19).</summary>
    public static IReadOnlyList<Guid> EligibleForNextWound(GameState state, PropertySet props, Guid unit)
    {
        var living = Lookup.AliveModels(state, unit);
        // Heroes are deferred before the Tough ordering is read, not after it. p.15 writes "heroes must be assigned wounds last,
        // even if already wounded" as a parenthetical inside the continuation clause, so it is an exception to that clause: a
        // wounded Tough hero waits for every other model rather than soaking the next wound.
        var notHeroes = living.Where(m => !state.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero)).ToList();
        var pool = notHeroes.Count > 0 ? notHeroes : living;
        var woundedTough = pool.Where(m => state.Projection.Models[m].Wounds > 0 && (Lookup.ModelParameter(state, m, "Tough") ?? 1) > 1).OrderByDescending(m => state.Projection.Models[m].Wounds).ToList();
        return woundedTough.Count > 0 ? [woundedTough[0]] : pool;
    }

    sealed class AssignStep : IStep
    {
        public string Name => "assign";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var wounds = WoundsToDeal(ctx.Frame);
            if (wounds <= 0) return new Finish([]);
            var team = ctx.TeamOf(target) ?? throw new InvalidOperationException("target unit has no team");
            // A Takedown group named its model, so nothing is asked and no ordering is consulted: p.15's Tough-first and heroes-last
            // decide the prompt's targets, and there is no prompt (p.14).
            var takedown = TakedownModel(ctx.Frame) is { } named && Lookup.AliveModels(ctx.State, target).Contains(named) ? named : (Guid?)null;
            AskAnswer? answer = null;
            if (takedown is null)
            {
                if (ctx.History.LastClosed() is not { Single: { } closed })
                    return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(EligibleForNextWound(ctx.State, ctx.Properties, target), wounds))]);
                answer = closed;
            }

            var actor = ctx.Frame.Context.ActorUnitId;
            var ix = AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, null, ctx.Frame.Context.Action);
            var live = LiveProperties.Settle(ix);
            var targetSide = actor is null ? Side.Attacker : Side.Defender;
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(live, Side.Attacker);
            // Regeneration is aggregation all (3.5.1: "a unit where all models have this rule"), so LiveProperties.Gather settles it
            // as one unit-level property with no ModelId, and it applies to every wound the unit takes. p.13: instances of a rule
            // without (X) do not stack, so one roll is taken however many are live, a granted instance included. Threshold is the row's.
            var regen = RollEffects.OwnRolls(live, targetSide, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound })
                .FirstOrDefault();
            var allocation = takedown is { } one ? new Dictionary<Guid, int> { [one] = wounds }
                : answer is Allocated a ? a.Counts : new Dictionary<Guid, int> { [EligibleForNextWound(ctx.State, ctx.Properties, target)[0]] = wounds };

            var changes = new List<StateChange>(); var landed = 0; var moves = new List<ModelRelocation>();
            foreach (var (modelId, count) in allocation.Where(kv => kv.Value > 0))
            {
                var model = ctx.Projection.Models[modelId];
                var capacity = Lookup.ModelParameter(ctx.State, modelId, "Tough") ?? 1;
                var incoming = count * multiplier;
                if (regen.Roll is { } roll)
                {
                    var record = ctx.Roll(new Dice.RollPlan(regen.Source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), incoming);
                    incoming = Dice.RollOutcome.Failures(record);   // each success overrides one wound
                }
                var total = Math.Min(capacity, model.Wounds + incoming);
                landed += total - model.Wounds;
                changes.Add(new WoundsSet(modelId, total));
                if (total >= capacity) moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
            }
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            return new Complete(changes, new Dictionary<string, int> { ["wounds_dealt"] = landed });
        }
    }
}
```

`GfRules._sequences` gains `GfAttack.DealWounds()`. A property's own roll is recorded under the entry's name (`Regeneration`) with the effect row's threshold, so nothing here knows the number 5 (effect grammar → `Roll` nests).

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfDealWoundsTests`; expect 7 passed. Commit: `Deal wounds: the defender allocates, Tough and Deadly and Regeneration resolve, casualties shelve`.**
