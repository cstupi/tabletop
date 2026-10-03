using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static ConditionDocument At(string point) => new(ConditionKind.AtPoint, null, Comparison.Eq, null, [point]);
    static EffectDocument Move(int inches, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.Move, null, null, null, null, null, null, null, inches, false, null, null, null, EffectDuration.Interaction, when, []);

    [Fact]
    public void An_at_point_row_is_returned_only_at_its_point_and_never_by_an_interaction_query()
    {
        var hop = new EntryDocument("Hop", CatalogKind.Model, false, Aggregation.All, null, null, "Hop.", null, [Move(3, At(GfNames.AfterShooting))]);
        var props = TestProperties.WithSpells(hop);
        var a = Unit("A", "4+", "4+", [], 1, [P("Hop")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Live(string? point) => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], point));
        Assert.Single(Live(GfNames.AfterShooting).AtPoint(Side.Attacker, GfNames.AfterShooting));
        Assert.Empty(Live(GfNames.AfterMelee).AtPoint(Side.Attacker, GfNames.AfterMelee));
        Assert.Empty(Live(null).Effects(Side.Attacker, EffectKind.Move));
    }

    [Fact]
    public void A_round_scoped_usage_counts_this_rules_sequences_since_the_round_started()
    {
        var once = new EntryDocument("Once", CatalogKind.Model, false, Aggregation.All, null, null, "Once per round.", null,
            [Move(3, At(GfNames.AfterShooting), new ConditionDocument(ConditionKind.UsageCount, null, Comparison.Lt, 1, ["round"]))]);
        var props = TestProperties.WithSpells(once);
        var a = Unit("A", "4+", "4+", [], 1, [P("Once")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Now() => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], GfNames.AfterShooting));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new SequenceStarted(Guid.CreateVersion7(), GfNames.RepositionSeq, new SequenceContext(a.Id, [], [], "Once")), null));
        Assert.Empty(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new RoundStarted(2, [], []), null));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
    }

    static EffectDocument Row(EffectKind kind, string? entry = null) =>
        new(EffectSubject.Self, 10, kind, null, null, entry, null, null, null, null, 1, false, null, null, null, EffectDuration.Once, [], []);

    [Fact]
    public void A_granted_rules_own_pass_one_rows_run_so_a_granted_boost_suppresses_its_base()
    {
        var grantBoost = new EntryDocument("Boost It", CatalogKind.Spell, false, null, 1, null, "Boost it.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18), [Row(EffectKind.GrantProperty, "Boosted")]);
        var boosted = new EntryDocument("Boosted", CatalogKind.Model, false, Aggregation.All, null, null, "Boosted.", null, [Row(EffectKind.SuppressProperty, "Base")]);
        var baseRule = new EntryDocument("Base", CatalogKind.Model, false, Aggregation.All, null, null, "Base.", null, []);
        var props = TestProperties.WithSpells(grantBoost, boosted, baseRule);
        var a = Unit("A", "4+", "4+", [], 1, [P("Base")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Live() => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.True(Live().Has(Side.Attacker, "Base"));
        var row = props.Entry("Boost It")!.Effects.Single();
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(a.Id, new Grant(Guid.CreateVersion7(), row.Id, "Boost It", a.Id, 1)), null));
        var live = Live();
        Assert.True(live.Has(Side.Attacker, "Boosted"));
        Assert.False(live.Has(Side.Attacker, "Base"));
    }

    [Fact]
    public void Pass_one_stops_when_a_rule_grants_itself()
    {
        var loop = new EntryDocument("Loop", CatalogKind.Model, false, Aggregation.All, null, null, "Loop.", null, [Row(EffectKind.GrantProperty, "Loop")]);
        var props = TestProperties.WithSpells(loop);
        var a = Unit("A", "4+", "4+", [], 1, [P("Loop")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.True(live.Has(Side.Attacker, "Loop"));
    }

    static void StartMelee(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit defender)
    {
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "strike_back" }) sim.Answer(Samples.Team2, new Chosen("pass"));
        if (sim.AskForOrNull(Samples.Team1) is { Question: "move" }) sim.Answer(Samples.Team1, new Passed());   // the charger's fall-back (consolidate)
    }

    static Sim MeleeSim(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender, PropertySet? props = null, IReadOnlyList<Tabletop.Content.TerrainPlacement>? terrain = null)
    {
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3, terrain: terrain), GrimdarkFutureRules.Instance, props ?? Core, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        StartMelee(sim, charger, defender);
        return sim;
    }

    static void MoveBy(Sim sim, Guid team, Tabletop.Content.Unit unit, double dx)
    {
        var moves = unit.Models.Select(m => { var at = ((OnTable)sim.Model(m.Id).Location).Placement; return new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } })); }).ToList();
        sim.Answer(team, new Destinations(moves));
    }

    [Fact]
    public void Hit_and_run_fighter_offers_its_team_a_three_inch_move_after_the_melee_once_a_round()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [P("Hit & Run Fighter")], Melee("Spear", 1));
        var sim = MeleeSim(charger, defender);
        var react = Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal($"Hit & Run Fighter|{defender.Id:N}", react.Choices.Single().Id);
        sim.Answer(Samples.Team2, new Chosen(react.Choices.Single().Id));
        Assert.Equal(3, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).Allowance);
        MoveBy(sim, Samples.Team2, defender, 3);
        Assert.Empty(sim.State.Projection.Stack);
        StartMelee(sim, charger, defender);
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                   // once per round
    }

    [Fact]
    public void Hit_and_run_shooter_moves_after_shooting_and_harassing_once_across_both_points()
    {
        foreach (var rule in new[] { "Hit & Run Shooter", "Harassing" })
        {
            var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
            var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
            var react = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
            sim.Answer(Samples.Team1, new Chosen(react.Choices.Single().Id));
            MoveBy(sim, Samples.Team1, a, -2);
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
            Assert.Null(sim.AskForOrNull(Samples.Team1));
        }
    }

    [Fact]
    public void Harassing_once_a_round_holds_across_after_melee_too()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Harassing")], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var sim = MeleeSim(charger, defender);
        sim.Answer(Samples.Team1, new Chosen($"Harassing|{charger.Id:N}"));
        MoveBy(sim, Samples.Team1, charger, -3);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
    }

    [Fact]
    public void Harassing_boost_moves_six_inches_and_counts_its_own_uses()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Harassing"), P("Harassing Boost")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        var react = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal($"Harassing Boost|{a.Id:N}", react.Choices.Single().Id);
        sim.Answer(Samples.Team1, new Chosen(react.Choices.Single().Id));
        Assert.Equal(6, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        MoveBy(sim, Samples.Team1, a, -5);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Null(sim.AskForOrNull(Samples.Team1));                          // the base is suppressed, so the Boost's one use is the round's move
    }

    [Fact]
    public void A_shaken_unit_is_offered_no_move()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [P("Hit & Run Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, defender.Id), GfNames.Shaken, true)]);
        StartMelee(sim, charger, defender);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
    }

    [Fact]
    public void A_team_that_has_passed_its_turn_is_still_offered_its_after_melee_move()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [P("Hit & Run Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        sim.State.Applier.Apply(new GameEvent(sim.State.Projection.LastSeq + 1, null, new TurnEnded(Samples.Team2, true, []), null));
        Assert.Contains(Samples.Team2, sim.State.Projection.Scheduler.Passed);
        StartMelee(sim, charger, defender);
        Assert.Equal($"Hit & Run Fighter|{defender.Id:N}", Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Single().Id);
    }

    static readonly EntryDocument Animate = new("Animate Spirit", CatalogKind.Spell, false, null, 1, null, "Grants Hit & Run Fighter.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18), [Row(EffectKind.GrantProperty, "Hit & Run Fighter")]);
    static readonly PropertySet GrantProps = TestProperties.WithSpells(Animate);

    static void Grant(Sim sim, Guid unit) =>
        sim.Append([new GrantAdded(unit, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), GrantProps.Entry("Animate Spirit")!.Effects[0].Id, "Animate Spirit", unit, 1))]);

    [Fact]
    public void A_granted_move_through_dangerous_terrain_still_takes_its_test_and_the_grant_is_spent_by_the_move()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var fire = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(3, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Dangerous, null))]);
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3, terrain: [fire]), GrimdarkFutureRules.Instance, GrantProps, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        Grant(sim, defender.Id);
        StartMelee(sim, charger, defender);
        sim.Answer(Samples.Team2, new Chosen($"Hit & Run Fighter|{defender.Id:N}"));
        var before = sim.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain);
        MoveBy(sim, Samples.Team2, defender, 3);
        Assert.Equal(before + 1, sim.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain));
        Assert.Empty(sim.Unit(defender.Id).Grants);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_declined_grant_is_not_spent_and_is_offered_at_the_next_after_melee_point()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, GrantProps, new ScriptedDice(Enumerable.Repeat(2, 60).ToArray()));
        Grant(sim, defender.Id);
        StartMelee(sim, charger, defender);
        sim.Answer(Samples.Team2, new Passed());                                  // declined at the window
        Assert.Single(sim.Unit(defender.Id).Grants);
        StartMelee(sim, charger, defender);
        Assert.Equal($"Hit & Run Fighter|{defender.Id:N}", Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Single().Id);
        sim.Answer(Samples.Team2, new Chosen($"Hit & Run Fighter|{defender.Id:N}"));
        sim.Answer(Samples.Team2, new Passed());                                  // declined at the move
        Assert.Single(sim.Unit(defender.Id).Grants);
    }
}
