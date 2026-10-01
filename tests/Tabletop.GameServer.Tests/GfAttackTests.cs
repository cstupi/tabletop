using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfAttackTests
{
    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t, string action) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    [Fact]
    public void Page_8_three_warriors_shoot_the_dwarves_two_hits_one_wound_one_casualty()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, []);
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -6, x2: 6), 3, 4, 5, 3, 4);
        sim.Start(GfNames.AttackSequence, Attack(warriors, dwarves, GfNames.Hold));
        var determine = sim.Steps("determine-attacks").Single();
        Assert.Equal(3, determine.Values!["attacks"]);
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal((GfNames.Hit, 4, 2), (hit.Roll, hit.Threshold, hit.Dice.Count(d => d.Success)));
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((GfNames.DefenseRoll, 4, 2, 1), (block.Roll, block.Threshold, block.Dice.Count, block.Dice.Count(d => !d.Success)));
        Assert.Equal([GfNames.AttackSequence, GfNames.ResolveHits, GfNames.DealWounds], sim.State.Projection.Stack.Select(f => f.Sequence));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal(1, Assert.IsType<Allocation>(ask.Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));
        Assert.Equal(3, sim.Alive(dwarves.Id));
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(5, Dice.DiceCursor.DrawnSoFar(sim.State));
    }

    [Fact]
    public void Out_of_range_models_add_no_attacks_and_no_attacks_finishes_early()
    {
        var far = Unit("Far", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 2));
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([far], [t], x1: -12, x2: 12), 6);
        sim.Start(GfNames.AttackSequence, Attack(far, t, GfNames.Hold));
        Assert.Equal(0, sim.Steps("determine-attacks").Single().Values!["attacks"]);
        Assert.Empty(sim.Steps("roll-to-hit")); Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Blast_multiplies_hits_up_to_the_target_size_and_rending_sixes_block_against_ap_four()
    {
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 2, P("Blast", 3)));
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([mortar], [pair], x1: -6, x2: 6), 4, 4, 1, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(mortar, pair, GfNames.Hold));
        Assert.Equal(2, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);                   // 3 capped at 2 models
        Assert.Equal(4, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);                // 2 hits × 2
        Assert.Equal(4, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);

        var render = Unit("Render", "4+", "4+", [], 2, [], Gun("Saw", "12\"", 1, P("Rending")));
        var target = Unit("T", "4+", "2+", [], 3, []);
        var sim2 = Sim.Gf(State([render], [target], x1: -4, x2: 4), 6, 4, 5, 5);
        sim2.Start(GfNames.AttackSequence, Attack(render, target, GfNames.Hold));
        var hit = sim2.Steps("roll-to-hit").Single();
        Assert.Equal(1, hit.Values!["granted_AP_4"]);
        var blocks = sim2.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(2, blocks.Count);
        var rendered = blocks.Single(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4));
        Assert.Equal((1, false), (rendered.Dice.Count, rendered.Dice[0].Success));                     // 5 − 4 = 1 against 2+: a wound
        Assert.True(blocks.Single(r => r != rendered).Dice[0].Success);                                 // the plain hit: 5 against 2+
    }

    [Fact]
    public void Furious_adds_a_hit_per_unmodified_six_only_when_charging_in_melee()
    {
        var berserkers = Unit("Berserkers", "4+", "4+", [], 2, [P("Furious")], Melee("Axe", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Charge));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["extra_hits"]);
        Assert.Equal(2, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
        var hold = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1);
        hold.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Hold));
        Assert.Equal(0, hold.Steps("roll-to-hit").Single().Values!.GetValueOrDefault("extra_hits"));
    }

    [Fact]
    public void A_takedown_model_names_its_target_before_the_hit_roll_and_the_wound_lands_on_it()
    {
        var takedown = Unit("Takedown", "5+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 2, []) with { Models = [Unit("T", "4+", "6+", [], 1, []).Models[0], Unit("H", "4+", "6+", [], 1, [P("Hero")]).Models[0] with { Ordinal = 2 }] };
        var (plain, hero) = (t.Models[0].Id, t.Models[1].Id);
        var sim = Sim.Gf(State([takedown], [t], x1: -6, x2: 6), 5, 1);
        sim.Start(GfNames.AttackSequence, Attack(takedown, t, GfNames.Hold));
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 1, 2), (pick.Min, pick.Max, pick.Eligible.Count));
        Assert.Empty(sim.Steps("roll-to-hit"));                                                        // the pick precedes the hit roll (p.8)
        sim.Answer(Samples.Team1, new SelectedPieces([hero]));
        Assert.Equal(5, sim.Steps("roll-to-hit").Single().Rolls.Single().Threshold);                   // 3.5.1 dropped Takedown's Quality 2+, so the unit's own 5+ stands
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                  // no allocation, so heroes-last never applies
        Assert.IsType<OnShelf>(sim.Model(hero).Location);
        Assert.Equal(0, sim.Model(plain).Wounds);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void No_model_is_picked_for_a_plain_group_or_a_single_model_target()
    {
        var riflemen = Unit("Riflemen", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var pair = Unit("Pair", "4+", "2+", [], 2, []);
        var plain = Sim.Gf(State([riflemen], [pair], x1: -6, x2: 6), 5, 6);
        plain.Start(GfNames.AttackSequence, Attack(riflemen, pair, GfNames.Hold));
        Assert.Null(plain.Steps("pick-target-model").Single().Answer);                                 // the step runs and asks nothing
        Assert.Single(plain.Steps("roll-to-hit"));

        var takedown = Unit("Takedown", "5+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var lone = Unit("Lone", "4+", "2+", [], 1, []);
        var single = Sim.Gf(State([takedown], [lone], x1: -6, x2: 6), 3, 6);
        single.Start(GfNames.AttackSequence, Attack(takedown, lone, GfNames.Hold));
        Assert.Null(single.AskForOrNull(Samples.Team1));                                               // one living model: nothing to choose
        Assert.Equal(5, single.Steps("roll-to-hit").Single().Rolls.Single().Threshold);                // the unit's own Quality, not 2+
    }

    [Fact]
    public void A_joined_hero_rolls_its_own_quality_pool_beside_the_squads()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var t = Unit("T", "4+", "6+", [], 4, []);
        var sim = Sim.Gf(State([joined], [t], x1: -6, x2: 6), 4, 4, 1);
        sim.Start(GfNames.AttackSequence, Attack(joined, t, GfNames.Hold));
        var determine = sim.Steps("determine-attacks").Single();
        Assert.Equal((2, 1, 1, 4), (determine.Values!["attacks"], determine.Values["attacks_q3"], determine.Values["attacks_q5"], determine.Values["defender_models"]));
        var hit = sim.Steps("roll-to-hit").Single();
        Assert.Equal([3, 5], hit.Rolls.Select(r => r.Threshold));                                      // p.4: one pool per model Quality value, best first
        Assert.Equal([true, false], hit.Rolls.Select(r => r.Dice[0].Success));                         // the same 4 hits at 3+ and misses at 5+
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.Equal(3, sim.Alive(t.Id));
    }

    [Fact]
    public void A_joined_hero_blocks_on_the_units_defense_until_every_other_model_is_dead()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var shooters = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));

        var full = Sim.Gf(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        full.Start(GfNames.AttackSequence, Attack(shooters, joined, GfNames.Hold));
        var blocked = full.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((5, false), (blocked.Threshold, blocked.Dice[0].Success));                        // p.14: the unit's Defense while anything else lives
        Assert.Null(full.AskForOrNull(Samples.Team2));                                                  // heroes last (p.14): one eligible model, so no Ask
        Assert.Equal(1, full.Alive(joined.Id));
        Assert.IsType<OnShelf>(full.Model(joined.Models[1].Id).Location);

        var alone = Sim.Gf(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        alone.Append([new ModelsMoved([new ModelRelocation(joined.Models[1].Id, alone.Model(joined.Models[1].Id).Location, new OnShelf())])]);
        alone.Start(GfNames.AttackSequence, Attack(shooters, joined, GfNames.Hold));
        var own = alone.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((2, true), (own.Threshold, own.Dice[0].Success));                                 // the same 3, blocked on the hero's own 2+
        Assert.Null(alone.AskForOrNull(Samples.Team2));
    }
}
