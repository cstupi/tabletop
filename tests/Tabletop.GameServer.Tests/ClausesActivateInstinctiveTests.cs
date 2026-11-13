using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause by clause, 3.6.0 Instinctive: "When this model is activated, if it is able to shoot/charge an enemy unit, then it must immediately attack the
/// closest valid target and gets +1 to hit rolls for that attack." Owner ruling: "still allowing moving (but requiring shooting the closest thing) OR
/// charging".
///   N1 able to shoot: the shot is not optional, in a Hold or an Advance (moving allowed), and Rush (no shot) is not offered;
///   N2 able to charge: Charge is offered (the alternative to shooting); a unit only able to charge must charge;
///   N3 the closest valid target: the shot's and the charge's only target;
///   N4 +1 to hit for that attack: its shooting, its charge's melee; not a strike back and not without the activation's mark;
///   N5 neither able to shoot nor charge: every action, no note; N6 a Shaken unit stays idle;
///   N7 mixed: a joined Hero without it (proven by Batch4ActivationTests, both editions).
/// </summary>
public class ClausesActivateInstinctiveTests
{
    static readonly PropertySet Core = TestProperties.Core36;
    static readonly string[] Rows = [GfNames.Hold, GfNames.Advance, GfNames.Rush, GfNames.Charge];

    static Unit Shooter(params PropertyInstance[] rules) => Unit("A", "4+", "4+", [], 1, rules, Gun("Gun", "48\"", 1));
    static Unit Brawler(params PropertyInstance[] rules) => Unit("A", "4+", "4+", [], 1, rules, Melee("Claws", 1));
    static Unit E(string n) => Unit(n, "4+", "4+", [], 1, [], Melee("Fist", 1));

    static (IReadOnlyList<string> Actions, string? Note) Choices(GameState s, Guid unit) =>
        GrimdarkFutureRules.ActivationChoices(s, Core, s.Projection.Units[unit], Rows);

    [Fact]
    public void An_instinctive_unit_that_advances_must_shoot_the_closest_enemy()
    {
        var a = Shooter(P("Instinctive"));
        var (near, far) = (E("Near"), E("Far"));
        var sim = Sim.Gf36(State([a], [near, far]), Enumerable.Repeat(4, 20).ToArray());
        Assert.Equal([GfNames.Hold, GfNames.Advance], Choices(sim.State, a.Id).Actions);                        // N1: no Rush, moving allowed
        sim.Append([new ActionRecorded(a.Id, GfNames.Advance)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        sim.Answer(Samples.Team1, new Passed());
        var declare = sim.AskFor(Samples.Team1);
        Assert.Equal("declare_targets", declare.Question);                                                        // N1: no shoot_or_not
        Assert.Equal([near.Id], ((Assignment)declare.Legal).Targets);                                             // N3
    }

    [Fact]
    public void An_instinctive_unit_in_charge_distance_may_charge_instead_and_its_charge_takes_the_closest_enemy()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Instinctive")], Gun("Gun", "48\"", 1), Melee("Claws", 1));
        var (near, far) = (E("Near"), E("Far"));
        var state = State([a], [near, far], x1: -2, x2: 2).Under(RuleSets.Gf360);
        Assert.Equal(2, GfMelee.ChargeTargets(state, Core, a.Id).Count);                                         // both within 12"
        var (actions, note) = Choices(state, a.Id);
        Assert.Equal([GfNames.Hold, GfNames.Advance, GfNames.Charge], actions);                                   // N2 owner ruling: shoot OR charge
        Assert.Equal(GrimdarkFutureRules.InstinctiveNote, note);
        var sim = Sim.Gf36(state, Enumerable.Repeat(4, 20).ToArray());
        sim.Append([new ActionRecorded(a.Id, GfNames.Charge)]);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        Assert.Equal([near.Id], ((PieceSelection)sim.AskFor(Samples.Team1).Legal).Eligible);                        // N3

        var plain = Unit("P", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var control = Sim.Gf36(State([plain], [E("N"), E("F")], x1: -2, x2: 2), 4);
        control.Start(GfNames.ChargeSeq, new SequenceContext(plain.Id, [], [], GfNames.Charge));
        Assert.Equal(2, ((PieceSelection)control.AskFor(Samples.Team1).Legal).Eligible.Count);
    }

    [Fact]
    public void An_instinctive_unit_only_able_to_charge_must_charge()
    {
        var a = Brawler(P("Instinctive"));
        var state = State([a], [E("E")], x1: -4, x2: 4).Under(RuleSets.Gf360);
        Assert.Equal([GfNames.Charge], Choices(state, a.Id).Actions);                                             // N2
    }

    [Fact]
    public void An_instinctive_unit_able_to_neither_has_every_action_and_a_shaken_one_stays_idle()
    {
        var a = Brawler(P("Instinctive"));
        var state = State([a], [E("E")], x1: -20, x2: 20).Under(RuleSets.Gf360);
        var (actions, note) = Choices(state, a.Id);
        Assert.Equal([GfNames.Hold, GfNames.Advance, GfNames.Rush], actions);                                     // N5 (no Charge: none in reach)
        Assert.NotEqual(GrimdarkFutureRules.InstinctiveNote, note);
        Assert.Null(GrimdarkFutureRules.InstinctiveAttack(state, Core, state.Projection.Units[a.Id]));

        var shooter = Shooter(P("Instinctive"));
        var shaken = State([shooter], [E("E")]).Under(RuleSets.Gf360);
        shaken.Applier.Apply(new GameEvent(shaken.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, shooter.Id), GfNames.Shaken, true), null));
        Assert.Equal([Scheduler.ActivationOption.Idle], Choices(shaken, shooter.Id).Actions);                    // N6
    }

    static int HitBonus(GameState s, Guid attacker, Guid target, Weapon w, AttackKind kind, string action) =>
        LiveProperties.Settle(GfAttack.AttackInteraction(s, Core, null, attacker, target, [w.Id], kind, action))
            .Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void The_plus_one_to_hit_is_for_that_attack_its_shot_or_its_charge_and_not_a_strike_back()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Instinctive")], Gun("Gun", "48\"", 1), Melee("Claws", 1));
        var e = E("E");
        var s = State([a], [e], x1: -2, x2: 2).Under(RuleSets.Gf360);
        var (gun, claws) = (a.Models[0].Weapons[0], a.Models[0].Weapons[1]);
        Assert.Equal(0, HitBonus(s, a.Id, e.Id, gun, AttackKind.Shooting, GfNames.Hold));                          // no activation mark: no bonus
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.InstinctiveCondition, true), null));
        Assert.Equal(1, HitBonus(s, a.Id, e.Id, gun, AttackKind.Shooting, GfNames.Advance));                       // N4: the shot
        Assert.Equal(1, HitBonus(s, a.Id, e.Id, claws, AttackKind.Melee, GfNames.Charge));                         // N4: the charge
        Assert.Equal(0, HitBonus(s, a.Id, e.Id, claws, AttackKind.Melee, GfNames.Hold));                           // a strike back is no Instinctive attack
    }

    [Fact]
    public void The_activation_marks_the_unit_and_its_end_clears_the_mark()
    {
        var a = Shooter(P("Instinctive"));
        var e = E("E");
        var sim = Sim.Gf36(State([a], [e]), Enumerable.Repeat(4, 20).ToArray());
        sim.Append([new ActionRecorded(a.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Contains(GfNames.InstinctiveCondition, sim.Unit(a.Id).Conditions);
        var cleared = GfHooks.OnTurnEnded(sim.State, Core, new Scheduler.TurnContext(Samples.Team1, new Scheduler.Activation(a.Id, GfNames.Hold, true, []), 0, 0));
        Assert.Contains(new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.InstinctiveCondition, false), cleared);   // "for that attack"
    }

    /// <summary>N3 "the closest valid target": the nearest enemy unit stands behind a wall that blocks sight, so it is no valid target to shoot; the
    /// farther one in sight is the closest valid target.</summary>
    [Fact]
    public void The_closest_valid_target_is_the_nearest_enemy_the_unit_can_see()
    {
        var a = Shooter(P("Instinctive"));
        var (near, far) = (E("Near"), E("Far"));
        var wall = new TerrainPlacement(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("wall"), new Tabletop.Geometry.Point3(30, 28, 0), new Tabletop.Geometry.Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Solid, Tabletop.Geometry.Footprint.Rect(8, 2), new Tabletop.Geometry.Point2(0, 0),
                new Tabletop.Geometry.Yaw(0), 0, 6, TerrainDeclarations.BlocksLineOfSight, null))]);
        var sim = Sim.Gf36(State([a], [near, far], terrain: [wall]), Enumerable.Repeat(4, 20).ToArray());
        Place(sim, (a.Models[0].Id, 30, 24), (near.Models[0].Id, 30, 32), (far.Models[0].Id, 44, 24));
        sim.Append([new ActionRecorded(a.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        var declare = sim.AskFor(Samples.Team1);
        Assert.Equal("declare_targets", declare.Question);
        Assert.Equal([far.Id], ((Assignment)declare.Legal).Targets);
    }

    static void Place(Sim sim, params (Guid Model, double X, double Y)[] at) =>
        sim.Append([new ModelsMoved([.. at.Select(a => new ModelRelocation(a.Model, sim.Model(a.Model).Location,
            new OnTable(new Placement(new Tabletop.Geometry.Point3(a.X, a.Y, 0), new Tabletop.Geometry.Yaw(0)))))])]);
}
