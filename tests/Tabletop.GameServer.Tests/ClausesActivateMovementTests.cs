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
/// Clause by clause, 3.6.0 core rules on how a unit may move (pp.13–15).
///
/// Immobile: "Models with this special rule may only use Hold actions." M1 Hold only; mixed: one model holds the whole unit (owner ruling); Shaken: idle.
/// Cumbersome: "May only use Hold or Advance actions, but can't shoot when moving." K1 Hold or Advance; K2 no shot after moving (proven by
///   GfCumbersome36Tests); mixed: a joined Cumbersome Hero holds the whole unit to both.
/// Artillery: "When this model uses Hold actions and shoots at enemies over 12” away, it gets +1 to hit rolls. If most models in this unit have this rule,
///   as long as they haven't moved during this round, enemy units shooting at them from over 12” away get -1 to hit rolls." A1 this model, Hold, over 12"
///   (proven by GfArtillery36Tests for a whole unit; here only the holder's weapon); A2 most models; A3 not moved this round, cleared at a round's start;
///   A4 from over 12" (proven by GfArtillery36Tests); moving is allowed under 3.6 (proven by GfArtillery36Tests).
/// Flying: "May move through units and terrain, and ignores the effects of Difficult and Dangerous terrain." (each clause proven by GfMoves36Tests,
///   GfContactTests, GfMovesTerrainTests); mixed: a unit moves as its slowest model (owner ruling), so one model without it keeps the cap.
/// Strider: "May ignore the effects of Difficult terrain." (proven by GfMovesTests; still tests Dangerous: GfMoveDangersTests); mixed as Flying.
/// </summary>
public class ClausesActivateMovementTests
{
    static readonly PropertySet Core = TestProperties.Core36;
    static readonly string[] Rows = [GfNames.Hold, GfNames.Advance, GfNames.Rush, GfNames.Charge];

    static Unit Joined(Unit squad, params PropertyInstance[] heroRules) =>
        squad with { Models = [.. squad.Models.Take(squad.Models.Count - 1), squad.Models[^1] with { Properties = [P("Hero"), .. heroRules] }] };

    static Destinations By(Sim sim, Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    static void Apply(GameState s, params EventPayload[] events)
    {
        foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null));
    }

    [Fact]
    public void Immobile_holds_and_a_shaken_immobile_unit_stays_idle()
    {
        var gun = Unit("Gun", "4+", "4+", [], 1, [P("Immobile")]);
        var s = State([gun], [Unit("E", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(RuleSets.Gf360);       // within charge distance
        Assert.Equal([GfNames.Hold], GrimdarkFutureRules.ActivationChoices(s, Core, s.Projection.Units[gun.Id], Rows).Actions);   // M1
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, gun.Id), GfNames.Shaken, true));
        Assert.Equal([Scheduler.ActivationOption.Idle], GrimdarkFutureRules.ActivationChoices(s, Core, s.Projection.Units[gun.Id], Rows).Actions);
    }

    [Fact]
    public void A_joined_cumbersome_hero_keeps_its_unit_to_hold_or_advance_and_from_shooting_after_moving()
    {
        var squad = Joined(Unit("Squad", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1)), P("Cumbersome"));
        var s = State([squad], [Unit("E", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(RuleSets.Gf360);
        Assert.Equal([GfNames.Hold, GfNames.Advance], GrimdarkFutureRules.ActivationChoices(s, Core, s.Projection.Units[squad.Id], Rows).Actions);
        var sim = Sim.Gf36(State([squad], [Unit("T", "4+", "4+", [], 1, [])], x1: -10, x2: 10), 1);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, By(sim, squad, 2));
        Assert.NotEqual("shoot_or_not", sim.AskForOrNull(Samples.Team1)?.Question);                              // K2 for the whole unit
        Assert.Equal(1, Assert.Single(sim.Steps("shoot-or-not")).Values!["cumbersome_no_shot"]);
    }

    static int HitMod(GameState s, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, string action) =>
        LiveProperties.Settle(GfAttack.AttackInteraction(s, Core, null, attacker, target, weapons, AttackKind.Shooting, action))
            .Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Artillerys_plus_one_is_the_holding_models_own_shot()
    {
        var crew = Unit("Crew", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1));
        crew = crew with { Models = [crew.Models[0] with { Properties = [P(GfNames.Artillery)], Weapons = [Gun("Cannon", "48\"", 1)] }, crew.Models[1]] };
        var target = Unit("Target", "4+", "4+", [], 1, []);
        var s = State([crew], [target]).Under(RuleSets.Gf360);                                                   // 24" apart
        var (cannon, rifle) = (crew.Models[0].Weapons[0].Id, crew.Models[1].Weapons[0].Id);
        Assert.Equal(1, HitMod(s, crew.Id, target.Id, [cannon], GfNames.Hold));                                  // A1 "this model"
        Assert.Equal(0, HitMod(s, crew.Id, target.Id, [rifle], GfNames.Hold));
        Assert.Equal(0, HitMod(s, crew.Id, target.Id, [cannon], GfNames.Advance));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, -1)]
    [InlineData(3, -1)]
    public void Artillerys_minus_one_needs_most_models_of_the_target_unit(int holders, int expected)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "48\"", 1));
        var guns = Unit("Guns", "4+", "4+", [], 3, []);
        guns = guns with { Models = [.. guns.Models.Select((m, i) => i < holders ? m with { Properties = [P(GfNames.Artillery)] } : m)] };
        var s = State([shooter], [guns]).Under(RuleSets.Gf360);
        Assert.Equal(expected, HitMod(s, shooter.Id, guns.Id, [shooter.Models[0].Weapons[0].Id], GfNames.Hold));   // A2
    }

    [Fact]
    public void Artillery_that_moved_this_round_loses_its_minus_one_until_the_next_round()
    {
        var guns = Unit("Guns", "4+", "4+", [P(GfNames.Artillery)], 1, []);
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "48\"", 1));
        var sim = Sim.Gf36(State([guns], [shooter], x1: -14, x2: 14), 4);
        var rifle = shooter.Models[0].Weapons[0].Id;
        Assert.Equal(-1, HitMod(sim.State, shooter.Id, guns.Id, [rifle], GfNames.Hold));
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(guns.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, By(sim, guns, -1));                                                             // moved away, still over 12"
        Assert.Equal(0, HitMod(sim.State, shooter.Id, guns.Id, [rifle], GfNames.Hold));                          // A3
        sim.Append([new RoundStarted(2, [Samples.Team1, Samples.Team2], GfHooks.OnRoundStarted(sim.State, Core))]);
        Assert.Equal(-1, HitMod(sim.State, shooter.Id, guns.Id, [rifle], GfNames.Hold));                         // "during this round"
    }

    static TerrainPlacement Strip(TerrainDeclarations kind) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(-6, -20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(6, 30), new Point2(0, 0), new Yaw(0), 0, 1, kind, null))]);

    /// <summary>A Rush of 11" through a Difficult strip (x -9..-3) no model can go round: whether the 6" cap binds the unit.</summary>
    static bool Capped(Unit unit)
    {
        var sim = Sim.Gf36(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Strip(TerrainDeclarations.Difficult)]), Enumerable.Repeat(4, 30).ToArray());
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        var ask = sim.AskFor(Samples.Team1);
        return GfMoves.DifficultCapBreach(sim.State, ask, Assert.IsType<MoveRegion>(ask.Legal), By(sim, unit, 11)) is not null;
    }

    [Theory]
    [InlineData("Strider")]
    [InlineData("Flying")]
    public void A_unit_all_of_whose_models_ignore_difficult_terrain_is_not_capped_and_one_model_without_it_caps_the_unit(string rule)
    {
        Assert.True(Capped(Unit("Plain", "4+", "4+", [], 2, [])));
        Assert.False(Capped(Unit("All", "4+", "4+", [], 2, [P(rule)])));
        Assert.True(Capped(Joined(Unit("Hero has it", "4+", "4+", [], 2, []), P(rule))));                          // owner ruling: the slowest model
        Assert.True(Capped(Joined(Unit("Hero lacks it", "4+", "4+", [], 2, [P(rule)]))));
    }
}
