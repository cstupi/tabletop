using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0, written from the army-book text.
///
/// Crossing Attack (army rule): "Once per activation, when this model moves through enemy units, pick one of them and roll X dice. For each 6+ the
/// target takes one wound."
///   1. only when the model moved through an enemy unit (ArmyRuleAuditHoldersTests, 3.5.1; here 3.6).  2. "this model": each holder its own X.
///   3. pick one of the units crossed.  4. once per activation: one pick, one roll, however many units were crossed.  5. X dice, each 6+ a wound,
///   no Defense roll.
/// Crossing Strike (army rule): "Once per activation, when this model moves through enemy units, pick one of them and attack it with this weapon as if it
/// was shooting. This weapon may only be used in this way."
///   1. attacks a crossed unit with that weapon (ArmyRules36Tests).  2. never in a shooting (ArmyRules36Tests).  3. "as if it was shooting": a hit roll
///   at Quality and the target's Defense roll.
/// </summary>
public class ClausesAttackCrossingTests
{
    /// <summary>The carrier unit at x -3 advances 5.5" along x; every crossing pick is answered with the given unit, else the first offered.</summary>
    static (Sim Sim, List<Ask> Picks) Cross(Unit a, IReadOnlyList<Unit> enemies, Guid? pick, int[] dice, IReadOnlyList<Point3>? enemyAt = null)
    {
        var sim = Sim.Gf36(State([a], enemies, x1: -3, x2: 3), dice);
        if (enemyAt is not null)
            sim.Append([new ModelsMoved([.. enemies.SelectMany(u => u.Models).Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(new Placement(enemyAt[i], new Yaw(0)))))])]);
        sim.Append([new ActionRecorded(a.Id, GfNames.Advance)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, new Destinations(a.Models.Select(m => sim.Model(m.Id).Location is OnTable { Placement: var p } t
            ? new ModelRelocation(m.Id, t, new OnTable(p with { Position = p.Position with { X = p.Position.X + 5.5 } })) : throw new InvalidOperationException()).ToList()));
        var picks = new List<Ask>();
        for (var i = 0; i < 20 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
        {
            if (ask.Question == "crossing_target") { picks.Add(ask); sim.Answer(Samples.Team1, new SelectedPieces([pick ?? ((PieceSelection)ask.Legal).Eligible[0]])); }
            else if (ask.Question == "shoot_or_not") sim.Answer(Samples.Team1, new Chosen("pass"));
            else sim.Answer(Samples.Team1, new Passed());
            if (sim.AskForOrNull(Samples.Team2) is { Legal: Allocation al }) sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = al.Amount }));
        }
        return (sim, picks);
    }

    static Unit Carrying(params int?[] x)
    {
        var u = Unit("A", "4+", "4+", [], x.Length, [P("Flying")]);
        return u with { Models = u.Models.Select((m, i) => x[i] is { } n ? m with { Properties = [.. m.Properties, P(GfNames.CrossingAttack, n)] } : m).ToList() };
    }

    static List<RollRecord> Rolls(Sim sim, string roll) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == roll)];

    [Fact]
    public void Crossing_attack_rolls_x_dice_and_each_six_is_a_wound_with_no_defense_roll()
    {
        var e = Unit("E", "4+", "2+", [], 1, [P("Tough", 9)]);
        var (sim, picks) = Cross(Carrying(3), [e], null, [6, 5, 6, .. Enumerable.Repeat(1, 20)]);
        Assert.Single(picks);
        Assert.Equal(3, Rolls(sim, GfNames.CrossingAttack).Single().Dice.Count);
        Assert.Empty(Rolls(sim, GfNames.DefenseRoll));
        Assert.Equal(2, sim.Model(e.Models[0].Id).Wounds);
    }

    [Fact]
    public void A_model_without_crossing_attack_crossing_an_enemy_sets_nothing_off()
    {
        // Only the first model (y -18.5) moves through E's one model; the second, which holds Crossing Attack, passes beside it.
        var (sim, picks) = Cross(Carrying(null, 3), [Unit("E", "4+", "4+", [], 1, [P("Tough", 9)])], null, Enumerable.Repeat(6, 20).ToArray());
        Assert.Empty(picks);
        Assert.Empty(Rolls(sim, GfNames.CrossingAttack));
    }

    [Fact]
    public void Crossing_attack_picks_one_of_two_units_crossed_once_in_the_activation()
    {
        var (e1, e2) = (Unit("E1", "4+", "4+", [], 1, [P("Tough", 9)]), Unit("E2", "4+", "4+", [], 1, [P("Tough", 9)]));
        var (sim, picks) = Cross(Carrying(2), [e1, e2], e2.Id, [6, 6, .. Enumerable.Repeat(1, 20)], [new Point3(0.3, -18.5, 0), new Point3(-1.4, -18.5, 0)]);
        var pick = Assert.Single(picks);
        var legal = Assert.IsType<PieceSelection>(pick.Legal);
        Assert.Equal(1, legal.Max);
        Assert.Equal(new HashSet<Guid> { e1.Id, e2.Id }, legal.Eligible.ToHashSet());
        Assert.Single(Rolls(sim, GfNames.CrossingAttack));
        Assert.Equal((0, 2), (sim.Model(e1.Models[0].Id).Wounds, sim.Model(e2.Models[0].Id).Wounds));
    }

    [Fact]
    public void Crossing_strike_attacks_as_if_shooting_with_a_hit_roll_and_a_defense_roll()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Flying")], Gun("Strafer", "24\"", 2, P(GfNames.CrossingStrike)));
        var e = Unit("E", "4+", "4+", [], 1, [P("Tough", 9)]);
        var (sim, picks) = Cross(a, [e], null, [6, 2, 1, .. Enumerable.Repeat(1, 20)]);                // one hit, one miss; the block fails
        Assert.Single(picks);
        var hit = Rolls(sim, GfNames.Hit).Single();
        Assert.Equal((4, 2), (hit.Threshold!.Value, hit.Dice.Count));
        Assert.Single(Rolls(sim, GfNames.DefenseRoll).SelectMany(r => r.Dice));
        Assert.Equal(1, sim.Model(e.Models[0].Id).Wounds);
    }
}
