using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class GlidePlanTests
{
    static readonly Guid Chargers = Guid.CreateVersion7(), Target = Guid.CreateVersion7();
    static readonly Guid Charger = Guid.CreateVersion7(), Defender = Guid.CreateVersion7();
    static Location At(double x) => new OnTable(new Placement(new Point3(x, 0, 0), new Yaw(0)));
    static ModelRelocation Move(Guid model, double from, double to) => new(model, At(from), At(to));
    static GameEvent Ev(EventPayload p) => new(1, null, p, null);
    static Guid? UnitOf(Guid model) => model == Charger ? Chargers : model == Defender ? Target : null;

    readonly GlidePlan _plan = new();

    Guid Open(string sequence)
    {
        var frame = Guid.CreateVersion7();
        _plan.Observe(Ev(new SequenceStarted(frame, sequence, new SequenceContext(Chargers, [Target], [], null))));
        return frame;
    }

    static GameEvent Step(Guid frame, params ModelRelocation[] moves) => Ev(new StepCompleted(frame, "move", null, [], [new ModelsMoved(moves)]));

    [Fact]
    public void A_move_walks_longer_the_further_it_goes_within_its_bounds()
    {
        var frame = Open("advance");
        var near = _plan.Of(Step(frame, Move(Charger, 0, 1)), UnitOf).Single();
        var far = _plan.Of(Step(frame, Move(Charger, 0, 12)), UnitOf).Single();
        Assert.Equal(GlideKind.Walk, near.Kind);
        Assert.True(far.Seconds > near.Seconds);
        Assert.InRange(near.Seconds, 0.35, 0.9); Assert.InRange(far.Seconds, 0.35, 0.9);
        Assert.Equal(12, far.Inches, 6);
    }

    [Fact]
    public void The_charging_units_move_in_its_charge_frame_is_a_charge_and_the_targets_close_in_a_walk()
    {
        var frame = Open("charge");
        var glides = _plan.Of(Step(frame, Move(Charger, 0, 8), Move(Defender, 10, 9)), UnitOf);
        Assert.Equal([GlideKind.Charge, GlideKind.Walk], glides.Select(g => g.Kind));
        Assert.InRange(glides[0].Seconds, 0.35, 0.6);
    }

    [Fact]
    public void A_finished_charge_frame_no_longer_makes_a_charge()
    {
        var frame = Open("charge");
        _plan.Observe(Ev(new SequenceFinished(frame)));
        Assert.Equal(GlideKind.Walk, _plan.Of(Step(frame, Move(Charger, 0, 8)), UnitOf).Single().Kind);
    }

    [Fact]
    public void A_model_coming_onto_the_table_is_placed_and_one_leaving_it_has_no_glide()
    {
        var frame = Open("deploy");
        var glides = _plan.Of(Step(frame, new ModelRelocation(Charger, new OnShelf(), At(3)), new ModelRelocation(Defender, At(3), new OnShelf())), UnitOf);
        Assert.Equal(GlideKind.Place, glides.Single().Kind);
        Assert.Equal(Charger, glides.Single().Move.ModelId);
    }

    [Fact]
    public void A_walk_eases_in_and_out_from_start_to_end_and_bobs_low()
    {
        var g = new Glide(Move(Charger, 0, 6), GlideKind.Walk, 6, 0.7);
        Assert.Equal(0, GlideCurve.At(g, 0).Along, 9); Assert.Equal(1, GlideCurve.At(g, 1).Along, 9);
        var speedStart = GlideCurve.At(g, 0.05).Along - GlideCurve.At(g, 0).Along;
        var speedMid = GlideCurve.At(g, 0.55).Along - GlideCurve.At(g, 0.5).Along;
        Assert.True(speedMid > 3 * speedStart, "starts slow, fastest midway");
        Assert.All(Enumerable.Range(0, 21).Select(i => GlideCurve.At(g, i / 20.0)), p => { Assert.InRange(p.Along, 0, 1); Assert.InRange(p.Lift, 0, GlideCurve.Bob); });
        Assert.Equal(0, GlideCurve.At(g, 1).Lift, 9);
    }

    [Fact]
    public void A_charge_pulls_back_then_runs_in_faster_and_faster_overshoots_and_settles_at_the_end()
    {
        var g = new Glide(Move(Charger, 0, 8), GlideKind.Charge, 8, 0.5);
        Assert.True(GlideCurve.At(g, GlideCurve.Wind).Along < 0, "pulls back first");
        Assert.True(GlideCurve.At(g, GlideCurve.Wind).Along * g.Inches >= -GlideCurve.PullBack - 1e-9);
        double Speed(double u) => GlideCurve.At(g, u + 0.02).Along - GlideCurve.At(g, u).Along;
        Assert.True(Speed(0.8) > 3 * Speed(0.3), "accelerates into contact");
        Assert.True(GlideCurve.At(g, GlideCurve.Run).Along > 1, "overshoots");
        Assert.Equal(1, GlideCurve.At(g, 1).Along, 9);
        Assert.Equal(1, GlideCurve.At(g, GlideCurve.Wind).Turn, 9);
    }

    [Fact]
    public void A_placed_model_drops_onto_its_spot()
    {
        var g = new Glide(new ModelRelocation(Charger, new OnShelf(), At(3)), GlideKind.Place, 0, GlidePlan.PlaceSeconds);
        Assert.Equal(GlideCurve.Drop, GlideCurve.At(g, 0).Lift, 9);
        Assert.Equal(0, GlideCurve.At(g, 1).Lift, 9);
        Assert.All(new[] { 0.0, 0.5, 1.0 }, u => Assert.Equal(1, GlideCurve.At(g, u).Along));
    }
}
