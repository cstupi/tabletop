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
