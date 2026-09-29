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
    static Sim Start(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, Guid? takedown = null, params Guid[] weapons)
    {
        var sim = new Sim(State([attacker], [target]), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds"), WithSequences.Parent(GfNames.AttackSequence, "wounds", "pick-target-model")), TestProperties.Core, new ScriptedDice(dice));
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
