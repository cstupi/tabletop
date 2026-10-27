using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.11: "If a model moves in or through dangerous terrain, or is activated in it, then it must take a dangerous terrain test." One test per model
/// per activation, however many of the triggers it meets.
/// </summary>
public class GfDangerousOnceTests
{
    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    /// <summary>A dangerous strip 2" wide and 30" long centred on (x, y): no move here goes round it.</summary>
    static TerrainPlacement Lava(double x, double y) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("lava"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 30), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);

    /// <summary>A lone model at (-20, -18.5), every die a 6.</summary>
    static Sim Lone(TerrainPlacement[] terrain, out Unit unit, PropertyInstance[]? modelRules = null, bool caster = false)
    {
        unit = Unit("A", "4+", "4+", [], 1, modelRules ?? (caster ? [P("Caster", 3)] : []));
        var sim = new Sim(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: terrain), GrimdarkFutureRules.Instance,
            caster ? TestProperties.WithSpells(Ward) : TestProperties.Core, new ScriptedDice(6));
        if (caster) sim.Append([new SpellTokensSet(unit.Models[0].Id, 3)]);
        return sim;
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static int Tests(Sim sim) => sim.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain);

    static int Dice(Sim sim) => sim.Steps("test").SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DangerousTerrain).Sum(r => r.Dice.Count);

    [Fact]
    public void A_model_activated_in_dangerous_terrain_that_moves_out_tests_once()
    {
        var sim = Lone([Lava(-20, -18.5)], out var unit);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, unit.Models[0].Id, 8)]));
        Assert.Equal(1, Tests(sim));
        Assert.Equal(1, Dice(sim));
    }

    [Fact]
    public void A_model_that_moves_in_from_outside_tests_once()
    {
        var sim = Lone([Lava(-16, -18.5)], out var unit);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(unit.Id, [], [], GfNames.Advance));
        Assert.Equal(0, Tests(sim));
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, unit.Models[0].Id, 6)]));
        Assert.Equal(1, Tests(sim));
        Assert.Equal(1, Dice(sim));
    }

    [Fact]
    public void A_move_split_by_a_stop_through_two_dangerous_pieces_tests_once()
    {
        var sim = Lone([Lava(-17, -18.5), Lava(-12, -18.5)], out var unit, caster: true);
        var m = unit.Models[0].Id;
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 5)], Continues: true));    // through the first piece
        Assert.Equal(1, Tests(sim));
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Passed());
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 6)]));                     // through the second
        Assert.Equal(1, Tests(sim));
        Assert.Equal(1, Dice(sim));
    }

    [Fact]
    public void A_tough_model_activated_in_dangerous_terrain_that_moves_through_more_rolls_its_tough_dice_once()
    {
        var sim = Lone([Lava(-20, -18.5), Lava(-16, -18.5)], out var unit, [P("Tough", 3)]);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, unit.Models[0].Id, 8)]));
        Assert.Equal(1, Tests(sim));
        Assert.Equal(3, Dice(sim));
    }

    [Fact]
    public void A_unit_activated_in_dangerous_terrain_that_charges_out_tests_once()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = new Sim(State([charger], [target], x1: -20, x2: -12, terrain: [Lava(-20, -18.5), Lava(-16, -18.5)]), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(6));
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        Assert.Equal(1, Tests(sim));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        var to = ((OnTable)sim.Model(target.Models[0].Id).Location).Placement.Position.X;
        var from = ((OnTable)sim.Model(charger.Models[0].Id).Location).Placement.Position.X;
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, charger.Models[0].Id, to - from - 1)]));
        Assert.Equal(1, Tests(sim));
        Assert.Equal(1, Dice(sim));
    }

    [Fact]
    public void A_model_that_stood_outside_at_activation_still_tests_for_its_move_when_another_model_tested_at_activation()
    {
        var unit = Unit("A", "4+", "4+", [], 2, []);
        var sim = new Sim(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: [Lava(-20, -18.5), Lava(-16, -18.5)]),
            GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(6));
        var inside = unit.Models[0].Id; var outside = unit.Models[1].Id;
        var y = ((OnTable)sim.Model(inside).Location).Placement.Position.Y;
        sim.Append([new ModelsMoved([new ModelRelocation(inside, sim.Model(inside).Location, new OnTable(new Placement(new Point3(-20, y, 0), new Yaw(0)))),
            new ModelRelocation(outside, sim.Model(outside).Location, new OnTable(new Placement(new Point3(-22, y, 0), new Yaw(0))))])]);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        Assert.Equal(1, Dice(sim));                                                            // the model standing in it
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, inside, 8), Shifted(sim, outside, 8)]));
        Assert.Equal(2, Tests(sim));
        Assert.Equal(2, Dice(sim));                                                            // the other model, moving through, once
    }
}
