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

    static Sim StartWith(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, Guid? takedown, Enforcement enforcement, (Guid Model, int Wounds)[] wounded, Guid[] weapons, Guid[]? shelved = null)
    {
        var state = State([attacker], [target], enforcement: enforcement);
        foreach (var (woundedModel, w) in wounded) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(woundedModel, w), null));
        if (shelved is { Length: > 0 })
            state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
                new ModelsMoved([.. shelved.Select(m => new ModelRelocation(m, state.Projection.Models[m].Location, new OnShelf()))]), null));
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

    /// <summary>p.15: "continue to put wounds on the tough model with most wounds in the unit until it is killed, before starting to put them on
    /// the next tough model". With none wounded the defender picks which one, and a spread answer puts every wound on the first model it names.</summary>
    [Fact]
    public void Among_unwounded_Tough_models_the_wounds_go_on_the_first_one_named_until_it_dies_and_the_defender_picks_the_next()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]);
        var (x, y, z) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id);
        var sim = Start(a, t, 4, [1]);
        var legal = Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal([x, y, z], legal.Targets);
        Assert.Equal(1, legal.MaxTargets);                                                                              // one Tough model at a time
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [y] = 1, [x] = 1, [z] = 2 }));             // spread: out of set, warned, read as its first model
        Assert.IsType<OnShelf>(sim.Model(y).Location);                                                                // three wounds killed y
        Assert.Equal((0, 0), (sim.Model(x).Wounds, sim.Model(z).Wounds));
        var next = Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal);                                         // the fourth: neither left is wounded, so the defender picks
        Assert.Equal(1, next.Amount);
        Assert.Equal([x, z], next.Targets);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [z] = 1 }));
        Assert.Equal((2, 0, 1), (sim.Alive(t.Id), sim.Model(x).Wounds, sim.Model(z).Wounds));
        Assert.Equal(4, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
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
        var regen = sim.Steps("assign").First().Rolls.Single();                                          // the rolling run; the next applies its dice
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
        Assert.Empty(sim3.Steps("assign").Single().Rolls);                                                      // aggregation all: 3.5.1 needs "a unit where all models have this rule"; 3.6.0 p.14 needs "most models"
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

    [Fact]
    public void A_later_weapon_group_whose_target_has_no_model_left_removes_nothing_and_finishes_without_asking()
    {
        // p.8: "all weapons are fired simultaneously": an earlier group destroyed the unit, so this group's wounds find no model and are lost.
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = StartWith(a, t, 2, [1], null, Enforcement.Warn, [], [], [t.Models[0].Id, t.Models[1].Id]);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal(0, sim.Unit(t.Id).WoundsThisActivation);
        Assert.DoesNotContain(sim.State.Projection.Stack, f => f.Sequence == GfNames.DealWounds);
    }

    [Fact]
    public void Wounds_carried_past_a_dead_model_keep_their_regeneration_result_and_are_not_rolled_again()
    {
        // p.14 Regeneration: "roll one die for each" wound: three wounds take three dice, however the allocation carries them.
        var t = Unit("T", "4+", "4+", [], 3, [P("Regeneration")]);
        var (p1, p2, p3) = (t.Models[0].Id, t.Models[1].Id, t.Models[2].Id);
        var sim = Start(Unit("A", "4+", "4+", [], 1, []), t, 3, [1, 1, 1, 5, 5]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [p1] = 3 }));          // three dice fail: p1 dies, two wounds carry
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [p2] = 1, [p3] = 1 }));
        Assert.Equal(3, sim.Steps("assign").SelectMany(s => s.Rolls).Sum(r => r.Dice.Count));
        Assert.Equal((0, 3), (sim.Alive(t.Id), sim.Unit(t.Id).WoundsThisActivation));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
