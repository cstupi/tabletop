# Task 12: deal-wounds

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (deal-wounds; Task 13 adds resolve-hits and attack-sequence to the same file)
- Create: `server/game/Rules/GrimdarkFuture/GfAnswers.cs` (every Grimdark Future step reads a closed Ask's answer through it; Tasks 14, 16, 17 and 19 use it)
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `GfAttack.DealWounds()`)
- Test: `tests/Tabletop.GameServer.Tests/GfDealWoundsTests.cs`
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences`, the decorator that registers a parked parent frame's sequence)

**Interfaces:**
- Consumes: Tasks 6–11; `Allocation`, `Allocated`, `SelectedPieces`, `WoundsSet`, `ModelsMoved`, `WoundsThisActivationSet` (Protocol).
- Produces: `static class GfAnswers` — `IRL<Guid> Pieces(AskAnswer?, Ask?)` (offered pieces, each once, at most `Max`), `IRL<Guid> Pieces(GameState, FrameView, string step, string question)` (an earlier step's answer against `FrameReader.Recorded`), `AskOption? Option(AskRecord)` (null for a pass, another type or an unoffered id), `Dictionary<Guid,int> Allocated(AskRecord, IReadOnlyCollection<Guid> alive, int owed)` (offered and alive models, counts ≥ 1, total ≤ owed), `IRL<ModelRelocation> Moves(GameState, AskRecord)` (models the `MoveRegion` listed and the projection holds, one each, `From` replaced by the projection's location, an `Inside` not in `Containment.Enterable` dropped). `static class GfAttack` with `Sequence DealWounds()` (name `deal-wounds`, one step `assign`; Task 19 adds `disgorge`); `static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IRL<Guid> weapons, AttackKind? kind, string? action)` (range from `Battlefield.UnitDistance`, terrain from `Battlefield.TerrainUnder(target)`); `static int WoundsToDeal(FrameView frame)` (failures of every `defense` roll in the parent frame's completed steps, or the parent's `Values["wounds"]` when it rolled none — dangerous terrain); `static IRL<Guid> EligibleForNextWound(GameState, PropertySet, Guid unit)` (p.15, in that order: heroes last even if already wounded; a Tough model that joined a unit without it is removed last, so while models without Tough remain they are the pool; among Tough models alone the most-wounded one until it dies; over `Lookup.AliveModels`, so a unit inside a container is wounded like any other); `static Guid? TakedownModel(FrameView frame)` (the model a `Takedown` group named, read off the nearest ancestor `attack-sequence` frame's closed `pick-target-model` answer; Task 13 adds the step that writes it).
- **Task 19 comes back to this sequence.** `DealWounds()` gains a second step, `disgorge`, and `assign`'s casualty line gains one condition: a model at its capacity that is a container holding somebody is *not* shelved here, because p.15 places its occupants *"before removal"*. Nothing else in this task changes, and a container with nobody inside is an ordinary casualty.
- Context of a `deal-wounds` frame: `ActorUnitId` = the attacker (null when terrain wounds), `TargetUnitIds = [target]`, `WeaponIds` = the attacking group's weapons (so `Deadly`, `Bane`'s and `Rending`'s suppression of `Regeneration` are live), `Action` = the attacker's action. `assign` is a repeat loop (`Complete(Repeat: true)` while wounds remain and a model can take them): the first segment takes its wounds from `WoundsToDeal`, each later one from the previous segment's `Values["wounds_left"]`, because p.8 and p.15 remove one model per wound and an ordinary wound past a killed model is assigned to the next one. `wounds_dealt` is cumulative across the segments. The Ask is `Allocation(Targets = eligible models, Amount = wounds)` to the target's team, and any member of that team may answer it; **with one eligible model there is no Ask**, the wounds go to it; the answer is applied as given (an out-of-set allocation accepted under `warn`/`silent` is applied too — nothing is an invariant). **A takedown wound opens no Ask**: when `TakedownModel` names a living model of the target, every wound of that group is assigned to that one model and the defender is not asked (p.14 `Takedown`; ruling 7). What that skips is exactly what building the prompt's targets decides — `EligibleForNextWound`, so p.15's wounded-`Tough`-model-first and heroes-last, and p.8's free choice of removal order. Everything after the assignment is unchanged: `Deadly` multiplies, `Regeneration` rolls, the capacity read shelves the model, and a Takedown or Deadly wound's excess past that capacity is lost. A name that is not a living model of the target — which only a menu edit can produce — is ignored and the ordinary Ask opens. Per assigned wound: multiplied by `WoundMultiplier` (`Deadly(X)`, first by ordinal 0; p.13 and p.14 discard what a multiplied or Takedown wound has past the model's capacity, and every other wound past it carries over); `Regeneration` live on the target unit, which 3.5.1 needs every model to carry → one die at 5+ per wound (a `RollRecord` named `Regeneration`), success ignores it; the model's `Wounds` rises; at `Tough(X)` (default 1) it goes to the shelf. `WoundsThisActivation` on the target rises by the wounds that landed in each segment.

Spec: action sequences → Grimdark Future's sequences (deal-wounds: `Tough` stacking, heroes last, `Deadly` first and no carry-over, `Regeneration` per wound unless suppressed, casualties to the shelf, wounds-this-activation); rulebook p.8 ("the defending player may remove models from the target in any order"), p.13 `Deadly`, p.14 `Regeneration`, `Tough`, `Hero`.

- [ ] **Step 1: Write the failing tests**

`WithSequences`, appended to `StubRules.cs`:

```csharp
/// <summary>Another ruleset plus a few sequences resolved first: how a test parks a parent frame the child reads from.</summary>
public sealed class WithSequences(IGameRules inner, params Sequence[] extra) : IGameRules
{
    public static Sequence Parent(string name, params string[] steps) =>
        new(name, steps.Select(s => (IStep)new LambdaStep(s, _ => throw new InvalidOperationException($"{name}: a parked parent never runs"))).ToList());
    public string Id => inner.Id;
    public string HitRoll => inner.HitRoll;
    public Sequence Sequence(string name) => extra.FirstOrDefault(s => s.Name == name) ?? inner.Sequence(name);
    public string? ActionSequence(string actionName) => inner.ActionSequence(actionName);
    public RollSpec RollSpec(string roll) => inner.RollSpec(roll);
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => inner.ReactionsAt(state, properties, point, frame);
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, Scheduler.TurnContext turn) => inner.EndOfTurnSequences(state, properties, turn);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => inner.OnRoundStarted(state, properties, round);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, Scheduler.TurnContext turn) => inner.OnTurnEnded(state, properties, turn);
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => inner.OrderReactions(state, actorTeam, reactingTeams);
    public IReadOnlyList<string> PhasesBeforeRounds => inner.PhasesBeforeRounds;
    public IReadOnlyList<string> PhasesInTurn => inner.PhasesInTurn;
    public Scheduler.FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => inner.FirstTeam(state, round, teams);
    public Scheduler.TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => inner.LegalTurn(state, properties, teamId);
    public Scheduler.Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => inner.ReadAnswer(state, teamId, answer);
    public bool ActivationComplete(GameState state, Scheduler.Activation activation) => inner.ActivationComplete(state, activation);
    public Scheduler.HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => inner.RoundBoundaryStep(state, properties, round, order);
}
```

Appended to `RulesEngineTests` (Task 10), the decorator's own fact:

```csharp
    [Fact]
    public void WithSequences_shadows_a_parked_parent_so_its_child_finishes_and_the_stack_empties()
    {
        var parent = Guid.CreateVersion7();
        var rules = new WithSequences(new StubRules(Roller()), WithSequences.Parent("test-parent", "wounds"));
        var sim = new Sim(State([A], [B]), rules, TestProperties.Core, new ScriptedDice(4));
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(A.Id, [], [], null)), new StepCompleted(parent, "wounds", null, [], [])]);
        Assert.Equal(["sequence_finished"], sim.Run().Select(EventKinds.Of));
        Assert.Empty(sim.State.Projection.Stack);
    }
```

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfDealWoundsTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does. A named model makes that parent an attack-sequence whose pick-target-model step answered, which is what a Takedown group leaves behind (Task 13).</summary>
    static Sim Start(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, Guid? takedown = null, params Guid[] weapons) =>
        StartWith(attacker, target, wounds, dice, takedown, Enforcement.Warn, [], weapons);

    static Sim StartWith(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, Guid? takedown, Enforcement enforcement, (Guid Model, int Wounds)[] wounded, Guid[] weapons)
    {
        var state = State([attacker], [target], enforcement: enforcement);
        foreach (var (woundedModel, w) in wounded) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(woundedModel, w), null));
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds"), WithSequences.Parent(GfNames.AttackSequence, "wounds", "pick-target-model")), TestProperties.Core, new ScriptedDice(dice));
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, takedown is null ? "test-parent" : GfNames.AttackSequence, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        if (takedown is { } model) sim.Append([new StepCompleted(parent, "pick-target-model", new SelectedPieces([model]), [], [])]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold));
        return sim;
    }

    [Fact]
    public void The_defender_allocates_and_a_joined_Tough_model_takes_wounds_only_after_the_plain_models_are_gone()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, []) with { Models = [.. Unit("T", "4+", "4+", [], 2, []).Models, Unit("Big", "4+", "4+", [], 1, [P("Tough", 3)]).Models[0] with { Ordinal = 3 }] };
        var sim = Start(a, t, 3, [1]);
        var (p1, p2, big) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id);
        var legal = Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal(3, legal.Amount);
        Assert.Equal([p1, p2], legal.Targets);                                                 // p.15: the joined Tough model is removed last
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [p1] = 2, [p2] = 1 }));                   // the second wound on a one-wound model spills back
        Assert.Equal(1, sim.Alive(t.Id));
        Assert.Equal(1, sim.Model(big).Wounds);                                                                        // and the wound left with a single eligible model asked nothing
        Assert.Equal(3, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal([big], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));
    }

    [Fact]
    public void The_most_wounded_Tough_model_keeps_taking_wounds_until_it_dies()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 2, [P("Tough", 3)]);
        var (x, y) = (t.Models[0].Id, t.Models[1].Id);
        var sim = StartWith(a, t, 3, [1], null, Enforcement.Warn, [(y, 1)], []);
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                                  // one eligible model: nothing to choose
        Assert.IsType<OnShelf>(sim.Model(y).Location);
        Assert.Equal((1, 1), (sim.Alive(t.Id), sim.Model(x).Wounds));                                                  // two wounds killed y, the third goes to the next Tough model
    }

    [Fact]
    public void Wounds_carry_over_a_wounded_Tough_model_that_dies_and_a_joined_one_waits_for_the_plain_models_even_under_strict_enforcement()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, []) with { Models = [.. Unit("T", "4+", "4+", [], 2, []).Models, Unit("Big", "4+", "4+", [], 1, [P("Tough", 3)]).Models[0] with { Ordinal = 3 }] };
        var (p1, p2, big) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id);
        var sim = StartWith(a, t, 3, [1], null, Enforcement.Strict, [(big, 2)], []);
        Assert.Equal([p1, p2], Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Targets);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [p1] = 2, [p2] = 1 }));
        Assert.Equal(0, sim.Alive(t.Id));                                                                              // the Tough model dies and both plain models are removed
        Assert.Equal(3, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_joined_non_hero_Tough_model_is_not_eligible_until_the_plain_models_are_gone()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, []) with { Models = [.. Unit("T", "4+", "4+", [], 2, []).Models, Unit("Big", "4+", "4+", [], 1, [P("Tough", 3)]).Models[0] with { Ordinal = 3 }] };
        var (p1, p2, big) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id);
        var sim = Start(a, t, 0, [1]);
        Assert.Equal([p1, p2], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));
        var later = Start(a, t, 2, [1]);
        later.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [p1] = 1, [p2] = 1 }));
        Assert.Equal([big], GfAttack.EligibleForNextWound(later.State, TestProperties.Core, t.Id));
    }

    [Fact]
    public void Deadly_multiplies_per_model_without_carry_over_and_heroes_are_eligible_last()
    {
        var gun = Gun("Cannon", "24\"", 1, P("Deadly", 3));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var weapons = a.Models[0].Weapons.Select(w => w.Id).ToArray();
        var sim = Start(a, t, 1, [1], null, weapons);
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                            // the hero waits, so the plain model is the one eligible model and nothing is asked
        Assert.Equal(1, sim.Alive(t.Id));
        Assert.Equal([t.Models[1].Id], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));
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
        var sim = Start(plain, troll, 2, [5, 2]);                                                              // one eligible model: no Ask
        var regen = sim.Steps("assign").Single().Rolls.Single();
        Assert.Equal(("Regeneration", 5, 2), (regen.Roll, regen.Threshold, regen.Dice.Count));
        Assert.Equal(1, sim.Model(troll.Models[0].Id).Wounds);
        Assert.Equal(1, sim.Unit(troll.Id).WoundsThisActivation);
        var baner = Unit("B", "4+", "4+", [], 1, [], Gun("Dart", "24\"", 1, P("Bane")));
        var sim2 = Start(baner, troll, 2, [5, 5], null, baner.Models[0].Weapons.Select(w => w.Id).ToArray());
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
    public void Deadly_wounds_on_a_plain_unit_kill_one_model_and_the_excess_does_not_carry_over()
    {
        var gun = Gun("Cannon", "24\"", 1, P("Deadly", 3));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 3, []);
        var sim = Start(a, t, 1, [1], null, a.Models[0].Weapons.Select(w => w.Id).ToArray());
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[1].Id] = 1 }));
        Assert.Equal(2, sim.Alive(t.Id));
        Assert.Equal((0, 0), (sim.Model(t.Models[0].Id).Wounds, sim.Model(t.Models[2].Id).Wounds));
        Assert.Equal(1, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Single(sim.Steps("assign"));                                                                            // no second segment: the multiplied wounds are not carried
    }

    [Fact]
    public void Wounds_beyond_the_units_last_model_are_lost_and_the_sequence_finishes()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = Start(a, t, 5, [1]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 3, [t.Models[1].Id] = 2 }));
        Assert.Equal(0, sim.Alive(t.Id));
        Assert.Equal(2, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        var one = Unit("O", "4+", "4+", [], 1, []);
        var single = Start(a, one, 3, [1]);
        Assert.Equal((0, 1), (single.Alive(one.Id), single.Unit(one.Id).WoundsThisActivation));
        Assert.Empty(single.State.Projection.Stack);
    }

    [Fact]
    public void Wounds_dealt_is_the_cumulative_total_across_the_segments_of_the_sequence()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 4, []);
        var sim = Start(a, t, 3, [1]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 3 }));                // one dies, two wounds spill back
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[1].Id] = 2 }));
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[2].Id] = 1 }));
        Assert.Equal([1, 2, 3], sim.Steps("assign").Select(s => s.Values!["wounds_dealt"]));
        Assert.Equal((1, 3), (sim.Alive(t.Id), sim.Unit(t.Id).WoundsThisActivation));
        Assert.Empty(sim.State.Projection.Stack);
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

- [ ] **Step 2: Run; expect compile errors. Write `GfAnswers.cs`**

Under `warn` and `silent` the dispatcher accepts an answer outside the Ask's legal set and logs it, and a throw inside `Advance` is replayed by every rehydrate. So no step casts `AskRecord.Single` or a `CompletedStep.Answer`: it reads the answer against the legal set of the Ask that recorded it (`AskRecord.Ask`, or `FrameReader.Recorded` for an earlier step's answer), and what the set did not offer is dropped. A wrong type is an empty answer.

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Answers read through the Ask that recorded them. Under warn and silent an answer outside the legal set is accepted, so a step
/// never trusts its type or its ids: what is not in the recorded set is dropped, and an answer of the wrong type is an empty one.
/// A throw inside Advance is replayed by every rehydrate, so nothing here may throw.
/// </summary>
public static class GfAnswers
{
    /// <summary>The pieces an answer named that the Ask offered, each once, at most the Ask's maximum.</summary>
    public static IReadOnlyList<Guid> Pieces(AskAnswer? answer, Ask? ask) =>
        answer is SelectedPieces picked && ask?.Legal is PieceSelection set ? picked.PieceIds.Distinct().Where(set.Eligible.Contains).Take(set.Max).ToList() : [];

    /// <summary>An earlier step's piece answer, read against the Ask that frame recorded for it.</summary>
    public static IReadOnlyList<Guid> Pieces(GameState state, FrameView frame, string step, string question) =>
        Pieces(frame.Frame.Steps.LastOrDefault(s => s.Step == step)?.Answer, FrameReader.Recorded(state, frame.Id, question));

    /// <summary>The option an answer chose, looked up among the Ask's own choices; null for a pass, another type or an id the Ask did not offer.</summary>
    public static AskOption? Option(AskRecord closed) =>
        closed.Single is Chosen chosen && closed.Ask.Legal is Options set ? set.Choices.FirstOrDefault(c => c.Id == chosen.OptionId) : null;

    /// <summary>Wounds per model: only models the Ask offered and that are still in the unit, none below one, and no more in total than the wounds owed.</summary>
    public static Dictionary<Guid, int> Allocated(AskRecord closed, IReadOnlyCollection<Guid> alive, int owed)
    {
        var result = new Dictionary<Guid, int>();
        if (closed.Single is not Allocated answer || closed.Ask.Legal is not Allocation set) return result;
        var left = owed;
        foreach (var (model, count) in answer.Counts)
        {
            if (!set.Targets.Contains(model) || !alive.Contains(model) || count <= 0 || left <= 0) continue;
            var take = Math.Min(count, left);
            result[model] = take; left -= take;
        }
        return result;
    }

    /// <summary>
    /// The relocations of models the move Ask named and the projection holds, one per model. From is the projection's location, never the
    /// answer's, so a forged From cannot skip the distance check; a move Inside a piece the Ask did not list as enterable is dropped.
    /// </summary>
    public static IReadOnlyList<ModelRelocation> Moves(GameState state, AskRecord closed)
    {
        if (closed.Single is not Destinations answer || closed.Ask.Legal is not MoveRegion region) return [];
        var enterable = region.Containment?.Enterable ?? [];
        return answer.Moves
            .Where(m => region.ModelIds.Contains(m.ModelId) && state.Projection.Models.ContainsKey(m.ModelId) && (m.To is not Inside into || enterable.Contains(into.ContainerId)))
            .DistinctBy(m => m.ModelId)
            .Select(m => m with { From = state.Projection.Models[m.ModelId].Location }).ToList();
    }
}
```

- [ ] **Step 3: Write `GfAttack.cs` (deal-wounds part)**

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

    /// <summary>
    /// p.15, in the order the rule writes them: heroes are assigned wounds last even if already wounded; a Tough model that joined a unit
    /// without it "is removed last", so while models without Tough remain they are the pool; among Tough models alone the most-wounded one
    /// keeps taking wounds until it is killed. The in-play reading, so a unit inside a wrecked transport can be wounded before it is placed (Task 19).
    /// </summary>
    public static IReadOnlyList<Guid> EligibleForNextWound(GameState state, PropertySet props, Guid unit)
    {
        var living = Lookup.AliveModels(state, unit);
        // Heroes are deferred before the Tough ordering is read, not after it. p.15 writes "heroes must be assigned wounds last,
        // even if already wounded" as a parenthetical inside the continuation clause, so it is an exception to that clause: a
        // wounded Tough hero waits for every other model rather than soaking the next wound.
        var notHeroes = living.Where(m => !state.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero)).ToList();
        var pool = notHeroes.Count > 0 ? notHeroes : living;
        bool IsTough(Guid m) => (Lookup.ModelParameter(state, m, "Tough") ?? 1) > 1;
        var plain = pool.Where(m => !IsTough(m)).ToList();
        if (plain.Count > 0) return plain;
        var woundedTough = pool.Where(m => state.Projection.Models[m].Wounds > 0).OrderByDescending(m => state.Projection.Models[m].Wounds).ToList();
        return woundedTough.Count > 0 ? [woundedTough[0]] : pool;
    }

    /// <summary>
    /// One segment per run, repeating (p.8: one model removed for each wound, so ordinary wounds carry over past a dead model): the
    /// wounds left come from the parent's failed defense rolls on the first run and from the previous segment's "wounds_left" after.
    /// Deadly's multiplied wounds and a Takedown's (a unit of one, p.14) do not carry over, so only their excess is lost.
    /// </summary>
    sealed class AssignStep : IStep
    {
        public string Name => "assign";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var wounds = ctx.Frame.Values.ContainsKey("wounds_left") ? ctx.Frame.Value("wounds_left") : WoundsToDeal(ctx.Frame);
            if (wounds <= 0) return new Finish([]);
            var team = ctx.TeamOf(target) ?? throw new InvalidOperationException("target unit has no team");
            // A Takedown group named its model, so nothing is asked and no ordering is consulted: p.15's Tough-first and heroes-last
            // decide the prompt's targets, and there is no prompt (p.14).
            var takedown = TakedownModel(ctx.Frame) is { } named && Lookup.AliveModels(ctx.State, target).Contains(named) ? named : (Guid?)null;
            var eligible = takedown is null ? EligibleForNextWound(ctx.State, ctx.Properties, target) : [];
            Dictionary<Guid, int> allocation;
            if (takedown is { } one) allocation = new Dictionary<Guid, int> { [one] = wounds };
            else if (eligible.Count == 1) allocation = new Dictionary<Guid, int> { [eligible[0]] = wounds };   // nothing to choose
            else if (ctx.History.LastClosed() is { Single: Allocated } closed) allocation = GfAnswers.Allocated(closed, Lookup.AliveModels(ctx.State, target), wounds);
            else return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(eligible, wounds))]);

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

            var changes = new List<StateChange>(); var landed = 0; var spilled = 0; var moves = new List<ModelRelocation>(); var dead = new HashSet<Guid>();
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
                var applied = Math.Min(capacity - model.Wounds, incoming);
                if (multiplier == 1) spilled += incoming - applied;   // an ordinary wound past a dead model is removed from the next one (p.8)
                landed += applied;
                changes.Add(new WoundsSet(modelId, model.Wounds + applied));
                if (model.Wounds + applied < capacity) continue;
                dead.Add(modelId);
                moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
            }
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            var total = ctx.Frame.Value("wounds_dealt") + landed;
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            var left = takedown is null ? wounds - allocation.Values.Sum() + spilled : 0;
            var survivors = Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m));
            var values = new Dictionary<string, int> { ["wounds_dealt"] = total, ["wounds_left"] = left };
            return new Complete(changes, values, Repeat: left > 0 && survivors);
        }
    }
}
```

`GfRules._sequences` gains `GfAttack.DealWounds()`. A property's own roll is recorded under the entry's name (`Regeneration`) with the effect row's threshold, so nothing here knows the number 5 (effect grammar → `Roll` nests).

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "GfDealWoundsTests|RulesEngineTests"`; expect 13 and 6 passed. Commit: `Deal wounds: the defender allocates, Tough and Deadly and Regeneration resolve, casualties shelve`.**
