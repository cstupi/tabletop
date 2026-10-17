using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>p.7: "Models may never be within 1” of models from other units, unless they are taking a Charge action, and may never move through other models".</summary>
public class GfContactTests
{
    // A's one model at (0, -18.5), B's at (x2, -18.5); bases 1.26".
    static Sim Game(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    {
        a = Unit("A", "4+", "4+", rules, 1, [], Melee("Claws", 1)); b = Unit("B", "4+", "6+", [], 1, []);
        return Sim.Gf(State([a], [b], x1: 0, x2: x2, enforcement: e), 6, 6, 6, 6, 6, 6);
    }
    static Sim Advancing(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    { var sim = Game(e, out a, out b, x2, rules); sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance)); return sim; }
    static MoveRegion Region(Sim sim) => Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
    static ModelRelocation To(Sim sim, Guid m, double x) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, -18.5, 0), new Yaw(0))));

    [Fact]
    public void An_advance_keeps_one_inch_a_charge_does_not_and_a_flyer_passes_models()
    {
        var advance = Advancing(Enforcement.Strict, out _, out _);
        Assert.Equal(((double?)1, false), (Region(advance).Separation, Region(advance).PassesModels));
        var charge = Game(Enforcement.Strict, out var c, out var target, x2: 12);
        charge.Start(GfNames.ChargeSeq, new SequenceContext(c.Id, [], [], GfNames.Charge));
        charge.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Null(Region(charge).Separation);
        Assert.True(Region(Advancing(Enforcement.Strict, out _, out _, 3, P("Flying"))).PassesModels);
    }

    /// <summary>A reposition move (army-rule hooks) of 3" for A, whose unit rules are given; p.13 exempts an Aircraft's move from the 1" rule.</summary>
    static MoveRegion Reposition(params PropertyInstance[] rules)
    {
        var hop = new EntryDocument("Hop", CatalogKind.Model, false, Aggregation.All, null, null, "Hop.", null,
            [new(EffectSubject.Self, 10, EffectKind.Move, null, null, null, null, null, null, null, 3, false, null, null, null, EffectDuration.Interaction,
                [new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.AfterShooting])], [])]);
        var a = Unit("A", "4+", "4+", rules, 1, [P("Hop")]);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, TestProperties.WithSpells(hop), new Tabletop.GameServer.Dice.ScriptedDice(4));
        sim.Start(GfNames.RepositionSeq, new SequenceContext(a.Id, [], [], "Hop"));
        return Region(sim);
    }

    [Fact]
    public void A_reposition_keeps_one_inch_unless_the_unit_is_an_aircraft()
    {
        Assert.Equal(1, Reposition().Separation);
        Assert.Null(Reposition(P(GfNames.Aircraft)).Separation);
    }

    /// <summary>A placement (army-rule hooks → A placement) of 3" for A at (0, -18.5), whose unit rules are given; B at (2.5, -18.5), an impassable
    /// 1" block at (0, -15.5).</summary>
    static Sim Blink(out Guid a, params PropertyInstance[] rules)
    {
        var blink = new EntryDocument("Blink", CatalogKind.Model, false, Aggregation.All, null, null, "Blink.", null,
            [new(EffectSubject.Self, 10, EffectKind.Place, null, null, null, null, null, null, null, 3, false, null, null, null, EffectDuration.Interaction,
                [new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.AfterShooting])], [])]);
        var block = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -15.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 1), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);
        var ua = Unit("A", "4+", "4+", rules, 1, [P("Blink")]);
        var sim = new Sim(State([ua], [Unit("B", "4+", "4+", [], 1, [])], x1: 0, x2: 2.5, enforcement: Enforcement.Strict, terrain: [block]),
            GrimdarkFutureRules.Instance, TestProperties.WithSpells(blink), new Tabletop.GameServer.Dice.ScriptedDice(4));
        sim.Start(GfNames.RepositionSeq, new SequenceContext(ua.Id, [], [], "Blink"));
        a = ua.Models[0].Id;
        return sim;
    }

    static ModelRelocation ToXY(Sim sim, Guid m, double x, double y) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    [Fact]
    public void A_placement_keeps_one_inch_and_lands_clear_of_impassable_terrain()
    {
        var sim = Blink(out var a);
        Assert.Equal(GfNames.MoveSeparationRule, Breach(sim, ToXY(sim, a, 1, -17)));                  // 0.86" from B
        Assert.Equal(GfNames.MoveImpassableRule, Breach(sim, ToXY(sim, a, 0, -15.5)));                // on the block
    }

    /// <summary>p.13 Aircraft: "Aircraft ignore all units and terrain when moving and stopping": a placement is a stop.</summary>
    [Fact]
    public void An_aircraft_placement_ignores_the_one_inch_rule_and_impassable_terrain()
    {
        var sim = Blink(out var a, P(GfNames.Aircraft));
        var region = Region(sim);
        Assert.Equal(((double?)null, true), (region.Separation, region.IgnoresTerrain));
        Assert.Null(Breach(sim, ToXY(sim, a, 1, -17)));
        Assert.Null(Breach(sim, ToXY(sim, a, 0, -15.5)));
        Assert.IsType<Applied>(Move(sim, ToXY(sim, a, 0, -15.5)));
        sim.Run();
        var at = ((OnTable)sim.Model(a).Location).Placement.Position;
        Assert.Equal((0, -15.5), (at.X, at.Y));
    }

    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) => sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);
    static string? Breach(Sim sim, params ModelRelocation[] moves) => GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations(moves))?.Rule;

    [Fact]
    public void Under_strict_landing_on_crossing_or_within_an_inch_of_another_unit_is_refused()
    {
        var sim = Advancing(Enforcement.Strict, out var a, out _);                       // B at x 3
        var m = a.Models[0].Id;
        foreach (var (x, rule) in new[] { (3.5, GfNames.MoveOverlapRule), (5.5, GfNames.MoveThroughModelsRule), (1.5, GfNames.MoveSeparationRule) })
        {
            Assert.Equal(rule, Breach(sim, To(sim, m, x)));
            Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, To(sim, m, x))).Reason);
        }
        Assert.IsType<Applied>(Move(sim, To(sim, m, 0.5)));                              // 1.24" edge to edge
    }

    [Fact]
    public void A_placement_has_no_path_so_crossing_a_model_is_not_judged_only_its_landing_is()
    {
        var sim = Game(Enforcement.Strict, out var a, out _, 2, P("Teleport"));                  // B at x 2; Teleport after a Rush: 6"
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new Chosen($"Teleport|{a.Id:N}"));
        var m = a.Models[0].Id;
        Assert.Null(Breach(sim, To(sim, m, 5)));                                                 // the straight line crosses B; 1.74" clear
        Assert.Equal(GfNames.MoveSeparationRule, Breach(sim, To(sim, m, 3.6)));                  // the landing is still judged
        sim.Answer(Samples.Team1, new Destinations([To(sim, m, 5)]));                            // accepted under strict, then applied
        Assert.Equal(5, ((OnTable)sim.Model(m).Location).Placement.Position.X, 3);
    }

    /// <summary>A plain Solid block 4" tall, x 22..26, y 10..40; B's one model on the ground at its foot, (26.8, 24), clear of it.
    /// A's one model has a 3" base, so standing at x 25 its edge reaches x 26.5, over B's base.</summary>
    static Sim Roof(bool onRoof, out Guid a, out Guid b)
    {
        var roof = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(24, 25, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(4, 30), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.None, null))]);
        var ua = Unit("A", "4+", "4+", [], 1, []); ua = ua with { Models = [ua.Models[0] with { Base = Base.Circle(3) }] };
        var ub = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([ua], [ub], enforcement: Enforcement.Strict, terrain: onRoof ? [roof] : []), 4);
        (a, b) = (ua.Models[0].Id, ub.Models[0].Id);
        sim.Append([new ModelsMoved([
            new ModelRelocation(a, sim.Model(a).Location, new OnTable(new Placement(new Point3(25, 21, onRoof ? 4 : 0), new Yaw(0)))),
            new ModelRelocation(b, sim.Model(b).Location, new OnTable(new Placement(new Point3(26.8, 24, 0), new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(ua.Id, [], [], GfNames.Advance));
        return sim;
    }

    [Fact]
    public void A_model_crossing_above_a_lower_model_does_not_move_through_it()
    {
        ModelRelocation Along(Sim sim, Guid m) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(25, 27, 0), new Yaw(0))));
        var ground = Roof(false, out var a, out _);
        Assert.Equal(GfNames.MoveThroughModelsRule, Breach(ground, Along(ground, a)));
        var roof = Roof(true, out a, out var b);
        Assert.Null(Breach(roof, Along(roof, a)));
        Assert.IsType<Applied>(Move(roof, Along(roof, a)));
        roof.Run();
        Assert.Equal(4, ((OnTable)roof.Model(a).Location).Placement.Position.Z, 6);
    }

    [Fact]
    public void One_inch_is_measured_in_three_dimensions()
    {
        var sim = Roof(true, out var a, out _);                                             // A on the roof, 2.5" above B's top
        var over = new ModelRelocation(a, sim.Model(a).Location, new OnTable(new Placement(new Point3(25, 24, 0), new Yaw(0))));
        Assert.Null(Breach(sim, over));                                                     // its edge is over B's base, 2.5" up
    }

    [Fact]
    public void Under_warn_an_overlap_is_applied_with_a_warning()
    {
        var sim = Advancing(Enforcement.Warn, out var a, out _);
        Assert.NotNull(Assert.IsType<Applied>(Move(sim, To(sim, a.Models[0].Id, 3.5))).Warning);
    }

    [Fact]
    public void A_charge_ends_in_base_contact_but_not_on_the_base()
    {
        var sim = Game(Enforcement.Strict, out var a, out var b, x2: 12);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Equal(GfNames.MoveOverlapRule, Breach(sim, To(sim, a.Models[0].Id, 11)));
        Assert.IsType<Applied>(Move(sim, To(sim, a.Models[0].Id, 10.74)));             // touching: within 1" is the Charge's
    }

    [Fact]
    public void A_flyer_moves_through_a_model_but_not_onto_it()
    {
        var sim = Advancing(Enforcement.Strict, out var a, out _, 3, P("Flying"));
        Assert.Null(Breach(sim, To(sim, a.Models[0].Id, 5.5)));
        Assert.Equal(GfNames.MoveOverlapRule, Breach(sim, To(sim, a.Models[0].Id, 3.5)));
    }

    [Fact]
    public void Leaving_a_model_it_overlaps_is_not_moving_through_it()
    {
        var sim = Game(Enforcement.Strict, out var a, out _);
        sim.Append([new ModelsMoved([To(sim, a.Models[0].Id, 2.5)])]);                   // left on B's base by a silent hand move
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.Null(Breach(sim, To(sim, a.Models[0].Id, -2)));
    }

    [Fact]
    public void A_wrapped_Grimdark_Future_still_judges_a_placement_overlap()
    {
        var sim = Game(Enforcement.Strict, out var a, out _);
        Assert.Equal(GfNames.MoveOverlapRule, new WithSequences(GrimdarkFutureRules.Instance).PlacementBreach(sim.State, [To(sim, a.Models[0].Id, 3)])!.Rule);
    }

    [Fact]
    public void A_model_touching_an_oval_base_near_its_diagonal_moves_away()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var b = Unit("B", "4+", "6+", [], 1, []);
        b = b with { Models = [b.Models[0] with { Base = Base.Oval(4, 2) }] };           // x 2..4, y -20.5..-16.5 at (3, -18.5)
        var sim = Sim.Gf(State([a], [b], x1: 0, x2: 3, enforcement: Enforcement.Strict), 6, 6, 6, 6, 6, 6);
        var m = a.Models[0].Id;
        ModelRelocation At(double x, double y) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
        // The oval's point at 45° is (3.707, -17.086), its normal (0.894, 0.447): A's base touches it there, inside the oval's rectangle.
        sim.Append([new ModelsMoved([At(3.707 + 0.894 * 0.635, -17.086 + 0.447 * 0.635)])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.IsType<Applied>(Move(sim, At(6.5, -15.5)));
    }
}
