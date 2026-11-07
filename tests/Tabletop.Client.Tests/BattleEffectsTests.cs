using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BattleEffectsTests
{
    static readonly Guid Frame = Guid.CreateVersion7(), Other = Guid.CreateVersion7(), Squad = Guid.CreateVersion7(), Orcs = Guid.CreateVersion7();
    static readonly Guid S1 = Guid.CreateVersion7(), S2 = Guid.CreateVersion7(), T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7();
    static Shot ShotOf(Guid shooter, Guid target, double distance, Guid? frame = null) =>
        new(Squad, shooter, new Point3(0, 0, 0.5), new Point3(distance, 0, 0.5), frame ?? Frame, target, 0);
    static CueAt Die(double delay, bool hit, int variant = SoundCues.Ballistic, Guid? frame = null) =>
        new(Cue.Shoot, delay, variant, Volley: true) { Frame = frame ?? Frame, Hit = hit, Actor = Squad, Target = Orcs };

    [Fact]
    public void Each_die_of_a_volley_flashes_a_muzzle_when_its_report_sounds_round_robin_over_the_shooters()
    {
        var fx = BattleEffects.Plan([Die(0, true), Die(0.1, true), Die(0.2, false)], [ShotOf(S1, T1, 10), ShotOf(S2, T2, 10)]);
        var muzzles = fx.Where(f => f.Kind == Fx.Muzzle).ToList();
        Assert.Equal([0, 0.1, 0.2], muzzles.Select(m => m.Delay));
        Assert.Equal([S1, S2, S1], muzzles.Select(m => m.Model!.Value));
        Assert.Equal(3, fx.Count(f => f.Kind == Fx.Tracer));
    }

    [Fact]
    public void A_hit_strikes_the_target_after_the_rounds_flight_and_a_miss_puffs_beyond_it()
    {
        var shot = ShotOf(S1, T1, 13);
        var fx = BattleEffects.Plan([Die(1, true), Die(1.1, false)], [shot]);
        var strike = fx.Single(f => f.Kind == Fx.Strike); var puff = fx.Single(f => f.Kind == Fx.Puff);
        var flight = 13 / BattleEffects.Speed(SoundCues.Ballistic);
        Assert.Equal(1 + flight, strike.Delay, 9);
        Assert.Equal(T1, strike.Model);
        Assert.Equal(shot.To, strike.To);
        Assert.Equal(1.1 + BattleEffects.Flight(shot, SoundCues.Ballistic), puff.Delay, 9);
        var p = puff.To!.Value; var beyond = Math.Sqrt((p.X - 13) * (p.X - 13) + p.Y * p.Y);
        Assert.InRange(beyond, BattleEffects.ScatterNear, BattleEffects.ScatterFar);
        Assert.True(p.X > 13, "past the target along the line of fire");
        Assert.Equal(0, p.Z);
    }

    [Fact]
    public void A_las_bolt_is_all_but_instant_and_no_round_flies_longer_than_the_cap()
    {
        Assert.True(BattleEffects.Flight(ShotOf(S1, T1, 24), SoundCues.Las) < 0.05);
        Assert.Equal(BattleEffects.MaxFlight, BattleEffects.Flight(ShotOf(S1, T1, 30), SoundCues.Flame));
    }

    [Fact]
    public void A_miss_lands_in_the_same_place_every_time()
    {
        var shot = ShotOf(S1, T1, 10);
        Assert.Equal(BattleEffects.Scatter(shot, Frame, 3), BattleEffects.Scatter(shot, Frame, 3));
        Assert.NotEqual(BattleEffects.Scatter(shot, Frame, 3), BattleEffects.Scatter(shot, Frame, 4));
    }

    [Fact]
    public void Each_weapon_group_fires_its_own_frames_shots()
    {
        var fx = BattleEffects.Plan([Die(0, true), Die(0.5, true, frame: Other)], [ShotOf(S1, T1, 10), ShotOf(S2, T2, 10, Other)]);
        Assert.Equal([S1, S2], fx.Where(f => f.Kind == Fx.Muzzle).Select(m => m.Model!.Value));
    }

    [Fact]
    public void A_volley_with_no_shots_drawn_draws_nothing()
    {
        Assert.Empty(BattleEffects.Plan([Die(0, true)], []));
    }

    [Fact]
    public void A_melee_die_is_a_jab_and_a_hit_strikes_when_the_jab_reaches()
    {
        CueAt Strike(Cue c, double d) => new(c, d, Volley: true) { Frame = Frame, Hit = c == Cue.Melee, Actor = Squad, Target = Orcs };
        var fx = BattleEffects.Plan([Strike(Cue.Melee, 0), Strike(Cue.Whiff, 0.1)], []);
        Assert.Equal([Fx.Jab, Fx.Strike, Fx.Jab], fx.Select(f => f.Kind));
        Assert.Equal([0, 1], fx.Where(f => f.Kind == Fx.Jab).Select(f => f.Index));
        Assert.Equal(BattleEffects.JabContact * BattleEffects.JabSeconds, fx[1].Delay, 9);
        Assert.All(fx, f => { Assert.Equal(Squad, f.Unit); Assert.Equal(Orcs, f.TargetUnit); });
    }

    [Fact]
    public void A_wound_shakes_each_wounded_model_and_fells_each_removed_one_when_it_sounds()
    {
        var fx = BattleEffects.Plan([new CueAt(Cue.Wound, 1.5) { Wounded = [T1, T2], Removed = [T2] }], []);
        Assert.Equal([(Fx.Shake, T1), (Fx.Fall, T2)], fx.Select(f => (f.Kind, f.Model!.Value)));
        Assert.All(fx, f => Assert.Equal(1.5, f.Delay));
    }

    [Fact]
    public void A_block_deflects_off_the_target_unit()
    {
        var fx = BattleEffects.Plan([new CueAt(Cue.Block, 0.9) { Target = Orcs, Actor = Squad }], []);
        Assert.Equal(Fx.Deflect, fx.Single().Kind);
        Assert.Equal(Orcs, fx.Single().TargetUnit);
    }
}
