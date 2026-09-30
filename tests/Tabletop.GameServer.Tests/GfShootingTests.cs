using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfShootingTests
{
    static Destinations MoveAll(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location, new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    [Fact]
    public void Advance_moves_six_inches_then_offers_shooting_which_declares_groups_and_targets()
    {
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        var a = Unit("A", "4+", "4+", [], 2, []); var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([squad], [a, b], x1: -10, x2: 10), 6, 6, 6, 1, 1, 1);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((6.0, 3), (move.Allowance, move.ModelIds.Count));
        sim.Answer(Samples.Team1, MoveAll(sim, squad, 2));
        Assert.Contains(GfNames.Moved, sim.Unit(squad.Id).Conditions);
        Assert.Equal(20, sim.Steps("move").First().Values!["distance_used_tenths"]);                           // the segment; the step completes again after the checkpoint
        var shoot = sim.AskFor(Samples.Team1);
        Assert.Equal(["shoot", "pass"], Assert.IsType<Options>(shoot.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        var declare = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(["Rifle"], declare.Items.Select(i => i.Id));
        Assert.Equal((2, 2), (declare.Targets.Count, declare.MaxDistinctTargets));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = a.Id }));
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(a.Id, Assert.Single(attacks).Context.TargetUnitIds[0]);
        Assert.Equal(GfNames.Advance, attacks[0].Context.Action);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [a.Models[0].Id] = 3 }));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_takedown_weapon_type_splits_into_one_group_per_model()
    {
        // 3.5.1 p.14 gives the pick to "This model", so two models carrying one takedown weapon type are two groups, each
        // naming its own target and rolling its own attack.
        var pair = Unit("Pair", "4+", "4+", [], 2, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([pair], [t], x1: -6, x2: 6), 6, 1, 6, 1);                                    // each group hits once, and each block fails
        sim.Start(GfNames.Shooting, new SequenceContext(pair.Id, [], [], GfNames.Hold));
        var declare = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(["Long Rifle (1)", "Long Rifle (2)"], declare.Items.Select(i => i.Id).OrderBy(x => x));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Long Rifle (1)"] = t.Id, ["Long Rifle (2)"] = t.Id }));
        Assert.Equal(1, sim.Steps("determine-attacks")[0].Values!["attacks"]);                              // one model's own attack, not the weapon type's two
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[2].Id]));                                    // the first takedown model names the third model
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[0].Id]));                                    // the second makes its own, different choice
        Assert.IsType<OnShelf>(sim.Model(t.Models[2].Id).Location);
        Assert.IsType<OnShelf>(sim.Model(t.Models[0].Id).Location);
        Assert.Equal(1, sim.Alive(t.Id));                                                                   // the model neither named is untouched
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void The_second_group_rolls_the_count_settled_before_the_first_group_fired()
    {
        // p.8 fires a unit's weapons simultaneously, so the Carbine rolls its two
        // attacks even though the only model within its 12" was killed by the Blaster group that was pushed first.
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Blaster", "24\"", 1), Gun("Carbine", "12\"", 2));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([shooter], [pair], x1: -5, x2: 5), 6, 1, 6, 6, 1, 1);                       // Blaster hits and wounds; the Carbine hits twice and wounds twice
        var far = pair.Models[1].Id;
        var seat = ((OnTable)sim.Model(far).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(far, sim.Model(far).Location,
            new OnTable(new Placement(seat with { X = seat.X + 10 }, new Yaw(0))))])]);                    // 20" out: inside the Blaster's range, outside the Carbine's
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Blaster"] = pair.Id, ["Carbine"] = pair.Id }));
        var fire = sim.Steps("fire-groups").First();
        Assert.Equal((1, 2), (fire.Values!["attacks_0"], fire.Values["attacks_1"]));                       // both counts settled before either group fired
        Assert.Equal((1, 2), (fire.Values["attacks_0_q4"], fire.Values["attacks_1_q4"]));                    // one Quality in the unit, so one key each
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));    // the near model dies to the Blaster
        var determine = sim.Steps("determine-attacks");
        Assert.Equal(2, determine.Count);
        Assert.Equal((2, 0), (determine[1].Values!["attacks"], determine[1].Values!["eligible_models"]));   // the recorded count stands over an empty eligible set
        Assert.Equal(2, sim.Steps("roll-to-hit")[1].Rolls.Single().Dice.Count);
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                       // one model left: it takes both wounds with nothing to choose (deal-wounds asks only when there is a choice)
        Assert.Equal(0, sim.Alive(pair.Id));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void The_second_groups_blast_cap_is_the_defender_model_count_settled_before_the_first_group_fired()
    {
        // p.13's cap of "as many hits as models in the target unit" is fixed at the same moment as the attack counts (p.8: all
        // weapons fire simultaneously), so the Mortar's Blast(3) is still capped at 2 after the Blaster killed one of the two
        // models. It is one number under one name, because p.14's "until all other models have been killed" counts the same
        // models at the same moment.
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Blaster", "24\"", 1), Gun("Mortar", "30\"", 1, P("Blast", 3)));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        // p.11 cover: the pair stands fully inside a 3"x3" wood (x 3.5..6.5, y -19.25..-16.25), so each 1.26" base (radius 0.63) lies wholly
        // within it: model 1 spans y -19.13..-17.87, model 2 y -17.63..-16.37, both x 4.37..5.63. The shooter at x -5 is outside it.
        var wood = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(5, -17.75, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);
        var sim = Sim.Gf(State([shooter], [pair], x1: -5, x2: 5, terrain: [wood]), 6, 1, 6, 1, 1);        // Blaster hits and wounds; the Mortar hits, then two blocks fail
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Blaster"] = pair.Id, ["Mortar"] = pair.Id }));
        var fire = sim.Steps("fire-groups").First();
        Assert.Equal((2, 2), (fire.Values!["defender_models_0"], fire.Values["defender_models_1"]));       // both counts settled before either group fired
        Assert.Equal((1, 1), (fire.Values["in_cover_0"], fire.Values["in_cover_1"]));                      // p.11's majority frozen with them, so no group can shoot a unit out of cover
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));    // one model dies to the Blaster
        Assert.Equal(2, sim.Steps("roll-to-hit")[1].Values!["hit_multiplier"]);                            // the recorded 2, not the one model still standing
        Assert.Equal(2, sim.Steps("roll-to-block")[1].Rolls.Single().Dice.Count);
        Assert.Contains(sim.Steps("roll-to-block")[0].Rolls.Single().Modifiers, m => m.Source == "cover" && m.Amount == 1);   // the frozen majority reaches the Blaster's block
        Assert.DoesNotContain(sim.Steps("roll-to-block")[1].Rolls.Single().Modifiers, m => m.Source == "cover");             // and Blast suppresses it for the Mortar's
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                       // one model left: it takes both wounds with nothing to choose
        Assert.Equal(0, sim.Alive(pair.Id));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_takedown_group_fires_before_the_other_groups_whatever_their_names()
    {
        // p.14: "Takedown attacks must be resolved before other weapons". "Aardvark" sorts before "Zed", so the name order alone would fire it first.
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Aardvark", "24\"", 1), Gun("Zed", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1, 1);                                        // both hits miss: only the order is under test
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Aardvark"] = t.Id, ["Zed"] = t.Id }));
        var zed = shooter.Models[0].Weapons.Single(w => w.Name == "Zed").Id;
        List<SequenceStarted> Started() => sim.Emitted.OfType<SequenceStarted>().Where(e => e.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal([zed], Assert.Single(Started()).Context.WeaponIds);                                     // waiting on the takedown pick, and Aardvark has not started
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[0].Id]));
        Assert.Equal(2, Started().Count);
    }

    [Fact]
    public void Fast_and_slow_change_the_allowance_and_rush_never_offers_shooting()
    {
        var fast = Unit("Fast", "4+", "4+", [], 2, [P("Fast")], Gun("Rifle", "24\"", 1));
        var sim = Sim.Gf(State([fast], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim.Start(GfNames.RushSeq, new SequenceContext(fast.Id, [], [], GfNames.Rush));
        Assert.Equal(16.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, new Passed());
        Assert.Null(sim.AskForOrNull(Samples.Team1)); Assert.Empty(sim.State.Projection.Stack);
        var slow = Unit("Slow", "4+", "4+", [], 2, [P("Slow")], Gun("Rifle", "24\"", 1));
        var sim2 = Sim.Gf(State([slow], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim2.Start(GfNames.AdvanceSeq, new SequenceContext(slow.Id, [], [], GfNames.Advance));
        Assert.Equal(4.0, Assert.IsType<MoveRegion>(sim2.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void Hold_with_a_shaken_unit_is_idle_and_clears_shaken_and_hold_without_a_target_asks_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions); Assert.Empty(sim.State.Projection.Stack);
        var sim2 = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim2.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Null(sim2.AskForOrNull(Samples.Team1)); Assert.Empty(sim2.State.Projection.Stack);           // pistols out of range: nothing to ask
    }

    [Fact]
    public void Moving_into_dangerous_terrain_tests_only_the_models_that_entered_it_and_wounds_on_ones()
    {
        var swamp = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("swamp"), new Point3(-4, -18, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(8, 8), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [swamp]), 1, 3);
        sim.Start(GfNames.RushSeq, new SequenceContext(pair.Id, [], [], GfNames.Rush));
        Assert.Empty(sim.Steps("test"));                                                                   // p.11: nobody was activated in it
        sim.Answer(Samples.Team1, MoveAll(sim, pair, 8));
        var test = sim.Steps("test").Single().Rolls.Single();
        Assert.Equal((2, 1), (test.Dice.Count, test.Dice.Count(d => !d.Success)));                          // both models entered: two dice
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team1).Legal).Amount);                 // the unit's own team allocates its wound
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }

    [Fact]
    public void Cover_terrain_needs_a_majority_of_the_units_models_not_half()
    {
        // p.11: "If the majority of models in a unit are fully inside a piece of cover terrain". A majority is more than half,
        // so one of two is not one. A unit of more than one model counts a model as in cover when its whole base is inside the volume.
        // The wood is 2"x1.5" centred at (6, -18.5): x 5..7, y -19.25..-17.75. A base is 1.26" across (radius 0.63), so the first model
        // at (6, -18.5) spans x 5.37..6.63, y -19.13..-17.87, wholly inside; the second at (6, -17) spans y -17.63..-16.37, wholly outside.
        var wood = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(6, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 1.5), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var pair = Unit("Pair", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([shooter], [pair], x1: -6, x2: 6, terrain: [wood]), 6, 5);                  // a hit, then a 5 that blocks only with the cover bonus
        var (one, two) = (pair.Models[0].Id, pair.Models[1].Id);
        var edge = ((OnTable)sim.Model(one).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(two, sim.Model(two).Location,
            new OnTable(new Placement(edge with { Y = -17.9 }, new Yaw(0))))])]);                          // the second model moves to (6, -17.9): its centre is inside the wood (y < -17.75) but its base, y -18.53..-17.27, straddles the edge
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [pair.Id], shooter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = sim.Steps("roll-to-block").Single();
        Assert.DoesNotContain(block.Rolls.Single().Modifiers, m => m.Source == "cover");                   // only Pair's first model has its whole base in the wood; the second's centre is inside but its base is not
        Assert.False(block.Rolls.Single().Dice[0].Success);
        Assert.Equal(1, block.Values!["wounds"]);

        var both = Sim.Gf(State([shooter], [pair], x1: -6, x2: 6, terrain: [wood]), 6, 5);
        var (first, second) = (pair.Models[0].Id, pair.Models[1].Id);
        var inside = ((OnTable)both.Model(first).Location).Placement.Position;
        both.Append([new ModelsMoved([new ModelRelocation(second, both.Model(second).Location,
            new OnTable(new Placement(inside with { X = inside.X + 0.2 }, new Yaw(0))))])]);               // the second model moves to (6.2, -18.5): x 5.57..6.83, y -19.13..-17.87, whole base inside the wood as well
        both.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [pair.Id], shooter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var covered = both.Steps("roll-to-block").Single();
        Assert.Contains(covered.Rolls.Single().Modifiers, m => m.Source == "cover" && m.Amount == 1);      // two of two is a majority
        Assert.True(covered.Rolls.Single().Dice[0].Success);
        Assert.Equal(0, covered.Values!["wounds"]);
        Assert.Equal(2, both.Alive(pair.Id));
        Assert.Empty(both.State.Projection.Stack);
    }

    [Fact]
    public void A_move_action_embarks_the_whole_unit_when_one_model_reaches_the_transport()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 3, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        // Armies.State puts each unit in its own row 12" away, so park the bus 3" from the nearest trooper.
        var near = ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(bus.Models[0].Id, sim.Model(bus.Models[0].Id).Location,
            new OnTable(new Placement(near with { X = near.X + 3 }, new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([bus.Models[0].Id], ask.Containment!.Enterable);                                     // p.14: within 6" of one model, by a clear path
        Assert.Null(ask.Containment.ExitingId);
        Assert.Equal(3, ask.ModelIds.Count);
        sim.Answer(Samples.Team1, new Destinations(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(bus.Models[0].Id))).ToList()));
        Assert.All(troops.Models, m => Assert.Equal(bus.Models[0].Id, Assert.IsType<Inside>(sim.Model(m.Id).Location).ContainerId));
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // p.14: embarking measures nothing
        Assert.Empty(Lookup.LivingModels(sim.State, troops.Id));
        Assert.Equal(3, Lookup.AliveModels(sim.State, troops.Id).Count);
    }

    [Fact]
    public void A_unit_inside_disembarks_within_six_inches_and_may_not_embark_again_this_activation()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Empty(ask.Containment.Enterable);                                                          // p.14: never both in one activation
        Assert.Equal(2, ask.ModelIds.Count);                                                              // named although neither has a position
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        Placement Out(double dx) => new(seat with { X = seat.X + dx }, new Yaw(0));
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(troops.Models[0].Id, new Inside(container), new OnTable(Out(4))),          // inside the radius
            new ModelRelocation(troops.Models[1].Id, new Inside(container), new OnTable(Out(9))),          // outside it: refused
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.Equal(container, Assert.IsType<Inside>(sim.Model(troops.Models[1].Id).Location).ContainerId);
        Assert.Contains(GfNames.Moved, sim.Unit(troops.Id).Conditions);
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // no start position, so nothing is measured
        Assert.Equal((false, true), GfMoves.ContainerMovesThisActivation(sim.State, troops.Id));      // p.15: it disembarked, so it may not embark again
    }

    [Fact]
    public void One_model_standing_in_dangerous_terrain_tests_at_activation_and_only_that_model()
    {
        var lava = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("lava"), new Point3(-12, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);   // model 1 at y −18.5, model 2 at y −17: only the first is inside
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [lava]), 1);
        sim.Start(GfNames.HoldSeq, new SequenceContext(pair.Id, [], [], GfNames.Hold));
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);                                      // p.11: activated in it, one model, one die
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }
}
