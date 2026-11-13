using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clauses, 3.6.0 army rules:
/// Ambush Re-Deployment: "[A1] Once per game, [A2] when a unit where all models have this rule [A3] ends its activation, [A4] you may [A5] immediately
/// remove it from the table [A6] (dropping any objectives it might hold within 1"), and [A7] deploy it as if it had Ambush [A8] at the beginning of the next round."
/// Re-Deployment: "[D1] After all other units are deployed (excluding units that were set aside), [D2] you may remove [D3] up to two [D4] friendly units from the
/// table and [D5] deploy them again. [D6] Players alternate in placing Re-Deployment units, starting with the player that activates next."
/// Vanguard / Fanatic: "[V1] After this model is deployed, [V2] it may be placed [V3] anywhere fully within 9" of its position, [V4] over 9" away from
/// non-Vanguard enemy units."
/// Surprise Attack (activation half): "[S2] The first time this unit is activated, [S3] pick one enemy unit [S4] within 6" [S5] in line of sight, and
/// [S6] roll X dice. For each 2+ it takes one hit [S7] with AP(1)."
/// Scout (3.6.0 p.15, every clause proven by DeploymentTests, GfScout36Tests, GfBatch3DeploymentTests): "[K1] May be set aside before deployment. [K2] After all
/// other units are deployed, [K3] may be deployed anywhere fully within 9” of their deployment zone, [K4] over 9” away from non-Scout enemy units. [K5] Players
/// alternate in placing Scout units, starting with the player that activates next."
/// </summary>
public class ClausesDeployRedeployTests
{
    static string Offer(Guid unit) => $"{GfAmbush.AmbushReDeployment}|{unit:N}";

    /// <summary>A's unit 1 (two models, each with Ambush Re-Deployment unless `heroOnly`: then model 1 alone) at (40, 44), B's at (40, 4); round one, A to activate.</summary>
    static ClausesDeploy36 RedeployGame(bool heroOnly = false)
    {
        var a = TestArmies.Build("A", 1, 2);
        a = a with { Units = [a.Units[0] with { Models = [.. a.Units[0].Models.Select((m, i) => !heroOnly || i == 0 ? m with { Properties = [P(GfAmbush.AmbushReDeployment)] } : m)] }] };
        var h = new ClausesDeploy36([a, TestArmies.Build("B", 1, 1)], real: true);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        h.Place(h.Units(0)[0], 40, 44); h.Place(h.Units(1)[0], 40, 4);
        Assert.Equal((1, AskQuestions.Activate, h.Teams[0]), (h.P.Scheduler.Round, h.Open.Question, h.Open.Teams.Single()));
        return h;
    }

    static IReadOnlyList<string> Offered(ClausesDeploy36 h, int team) =>
        h.P.OpenAsks.Values.Select(a => a.Ask).FirstOrDefault(a => a.Teams.Contains(h.Teams[team])) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices.Select(c => c.Id).ToList() : [];

    [Fact]
    public void Ambush_Re_Deployment_removes_the_unit_at_its_activations_end_and_it_arrives_as_an_Ambusher_next_round()
    {
        var h = RedeployGame();
        var a = h.Units(0)[0]; var b = h.Units(1)[0];
        h.Activate(a);
        Assert.Contains(Offer(a), Offered(h, 0));                                                            // [A2][A3] at the end of its activation
        h.Answer(0, new Chosen(Offer(a)));
        Assert.All(h.P.Units[a].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));   // [A5]
        h.Activate(b);
        h.Answer(0, new Confirmed());
        Assert.Equal((2, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.Open.Question));              // [A7][A8]
        Assert.Equal([a], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Answer(0, new SelectedPieces([a]));
        Assert.Equal(AmbushDistanceRule, h.State.Rules.PlacementBreach(h.State, h.At(a, 40, 12).Moves)?.Rule);   // [A7] as Ambush: over 9" from enemy units
        h.Dispatch(h.At(a, 40, 30), 0);
        Assert.All(h.P.Units[a].Definition.Models, m => Assert.IsType<OnTable>(h.P.Models[m.Id].Location));
        h.Activate(a);
        Assert.DoesNotContain(Offer(a), Offered(h, 0));                                                     // [A1] once per game
    }

    [Fact]
    public void Declining_Ambush_Re_Deployment_keeps_the_unit_and_offers_it_again_next_activation()
    {
        var h = RedeployGame();
        var a = h.Units(0)[0]; var b = h.Units(1)[0];
        h.Activate(a);
        h.Answer(0, new Passed());                                                                           // [A4] "may"
        Assert.All(h.P.Units[a].Definition.Models, m => Assert.IsType<OnTable>(h.P.Models[m.Id].Location));
        h.Activate(b);
        h.Answer(0, new Confirmed());
        h.Activate(a);
        Assert.Contains(Offer(a), Offered(h, 0));                                                            // [A1] a declined offer used nothing
    }

    [Fact]
    public void Ambush_Re_Deployment_is_not_offered_when_only_a_joined_hero_holds_it()
    {
        var h = RedeployGame(heroOnly: true);
        h.Activate(h.Units(0)[0]);
        Assert.Empty(Offered(h, 0));                                                                         // [A2] "a unit where all models have this rule"
    }

    /// <summary>[A8] "deploy it as if it had Ambush at the beginning of the next round": a unit passed at that round's Ambusher Ask is not asked at a later one.</summary>
    [Fact]
    public void A_unit_removed_by_Ambush_Re_Deployment_is_deployed_at_the_next_round_only()
    {
        var h = RedeployGame();
        var a = h.Units(0)[0]; var b = h.Units(1)[0];
        h.Activate(a);
        h.Answer(0, new Chosen(Offer(a)));
        h.Activate(b);
        h.Answer(0, new Confirmed());
        Assert.Equal(AskQuestions.DeployAmbusher, h.Open.Question);
        h.Answer(0, new Passed());
        h.Activate(b);
        h.Answer(0, new Confirmed());
        Assert.Equal(3, h.P.Scheduler.Round);
        Assert.NotEqual(AskQuestions.DeployAmbusher, h.Open.Question);
    }

    /// <summary>[D6] A deployed last (A, B, A), yet A won the roll-off and activates first in round one, so A is asked first in the Scouts phase; then B.</summary>
    [Fact]
    public void Re_Deployment_starts_with_the_team_that_activates_next_and_alternates()
    {
        var h = new ClausesDeploy36([TestArmies.Build("A", 2, 1, modelRules: [(1, GfAmbush.ReDeployment, null)]), TestArmies.Build("B", 1, 1, modelRules: [(1, GfAmbush.ReDeployment, null)])]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Place(a[0], 30, 44); h.Place(b[0], 30, 4); h.Place(a[1], 50, 44);
        Assert.Equal((AskQuestions.DeployScouts, h.Teams[0]), (h.Open.Question, h.Open.Teams.Single()));     // [D1] after all others
        Assert.True(h.Open.AllowPass);                                                                       // [D2] "may"
        Assert.Equal(a.Order(), Assert.IsType<PieceSelection>(h.Open.Legal).Eligible.Order());               // [D4] friendly units only
        h.Answer(0, new SelectedPieces([a[0]]));
        h.Dispatch(h.At(a[0], 20, 40), 0);                                                                  // [D5]
        Assert.Equal((AskQuestions.DeployScouts, h.Teams[1]), (h.Open.Question, h.Open.Teams.Single()));     // [D6] alternate
    }

    /// <summary>[V1] the after-deploy window offers the move once the unit is placed; [V2] it may be passed and deployment goes on.</summary>
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Fanatic")]
    public void Vanguard_is_offered_after_the_unit_is_deployed_and_may_be_declined(string rule)
    {
        var h = new ClausesDeploy36([TestArmies.Build("A", 2, 1, modelRules: [(1, rule, null)]), TestArmies.Build("B", 1, 1)], real: true);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Place(a[0], 40, 44);
        var offer = Assert.Single(Offered(h, 0));
        Assert.StartsWith(rule + "|", offer);
        h.Answer(0, new Passed());
        Assert.Equal((AskQuestions.Deploy, h.Teams[1]), (h.Open.Question, h.Open.Teams.Single()));
        h.Place(b[0], 40, 4);
        h.Place(a[1], 20, 44);
        Assert.Empty(Offered(h, 0));                                                                         // a unit without the rule offers nothing
    }

    /// <summary>[V3] "fully within 9" of its position": the model moves at most 9".</summary>
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Fanatic")]
    public void Vanguard_places_the_model_within_9_inches_of_its_position(string rule)
    {
        var f = Unit("F", "4+", "4+", [], 1, [P(rule)]);
        var sim = Sim.Gf36(State([f], [Unit("E", "4+", "4+", [], 1, [])], x1: 10, x2: 60), 1);
        sim.Start(RepositionSeq, new SequenceContext(f.Id, [], [], rule));
        var ask = sim.AskFor(Samples.Team1);
        var m = f.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        Destinations By(double dx) => new([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }))]);
        Assert.Null(sim.State.Rules.AnswerBreach(sim.State, ask, By(8.9)));
        Assert.NotNull(sim.State.Rules.AnswerBreach(sim.State, ask, By(9.5)));
    }

    static IReadOnlyList<AskOption> Reactions(Sim sim) => sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices : [];

    static (Sim Sim, Tabletop.Content.Unit Unit, Tabletop.Content.Unit Enemy) Surprise(double enemyX, params int[] dice)
    {
        var sa = Unit("SA", "4+", "4+", [], 1, [P(GfAmbush.SurpriseAttack, 3)]);
        var enemy = Unit("E", "4+", "6+", [], 5, []);
        return (Sim.Gf36(State([sa], [enemy], x1: 20, x2: enemyX), dice), sa, enemy);
    }

    static void Activate(Sim sim, Guid unit)
    {
        sim.Append([new ActionRecorded(unit, Hold)]);
        sim.Start(HoldSeq, new SequenceContext(unit, [], [], Hold));
    }

    [Fact]
    public void Surprise_Attack_is_offered_at_the_first_activation_rolls_X_dice_and_hits_once_per_2_plus()
    {
        var (sim, sa, enemy) = Surprise(25, 2, 1, 6, 1, 1, 1, 1, 1);                                         // edge to edge 3.74"
        Activate(sim, sa.Id);
        var use = Assert.Single(Reactions(sim), o => o.Id.StartsWith(GfAmbush.SurpriseAttack + "|", StringComparison.Ordinal));   // [S2]
        sim.Answer(Samples.Team1, new Chosen(use.Id));
        if (sim.AskForOrNull(Samples.Team1) is { Legal: PieceSelection p }) { Assert.Equal([enemy.Id], p.Eligible); sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id])); }   // [S3]
        var roll = sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).First(r => r.Dice.Count == 3);
        Assert.Equal([2, 1, 6], roll.Dice.Select(d => d.Raw));                                               // [S6] X = 3 dice
        var hits = sim.Emitted.OfType<SequenceStarted>().Single(s => s.Sequence == RuleHitsSeq);
        Assert.Equal(GfRuleHits.Key(GfAmbush.SurpriseAttack, 2), hits.Context.Action);                       // two 2+: two hits
        Assert.Equal([enemy.Id], hits.Context.TargetUnitIds);
    }

    [Fact]
    public void Surprise_Attack_is_not_offered_at_the_units_second_activation()
    {
        var (sim, sa, _) = Surprise(25);
        Activate(sim, sa.Id);
        Assert.NotEmpty(Reactions(sim));
        sim.Answer(Samples.Team1, new Passed());
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { } ask; i++) sim.Answer(Samples.Team1, ask.AllowPass ? new Passed() : new Confirmed());
        Activate(sim, sa.Id);
        Assert.DoesNotContain(Reactions(sim), o => o.Id.StartsWith(GfAmbush.SurpriseAttack, StringComparison.Ordinal));   // [S2] "The first time"
    }

    [Fact]
    public void Surprise_Attack_is_not_offered_with_no_enemy_unit_within_6_inches()
    {
        var (sim, sa, _) = Surprise(27.5);                                                                   // edge to edge 6.24"
        Activate(sim, sa.Id);
        Assert.DoesNotContain(Reactions(sim), o => o.Id.StartsWith(GfAmbush.SurpriseAttack, StringComparison.Ordinal));   // [S4]
    }

    /// <summary>[S2] with p.10 (3.6.0): a Shaken unit spends its first activation idle and "can't … use any active special rules"; that was its first
    /// activation, so a later one offers nothing.</summary>
    [Fact]
    public void Surprise_Attack_is_not_offered_after_a_first_activation_spent_idle()
    {
        var (sim, sa, _) = Surprise(25);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, sa.Id), Shaken, true), new ActionRecorded(sa.Id, Tabletop.GameServer.Scheduler.ActivationOption.Idle)]);
        sim.Start(IdleSeq, new SequenceContext(sa.Id, [], [], Tabletop.GameServer.Scheduler.ActivationOption.Idle));
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { } ask; i++) sim.Answer(Samples.Team1, ask.AllowPass ? new Passed() : new Confirmed());
        Assert.Contains(Shaken, sim.Unit(sa.Id).Conditions);                                                 // cleared at the turn's end (GfHooks.OnTurnEnded), which Start does not reach
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, sa.Id), Shaken, false)]);
        Activate(sim, sa.Id);
        Assert.DoesNotContain(Reactions(sim), o => o.Id.StartsWith(GfAmbush.SurpriseAttack, StringComparison.Ordinal));
    }

    /// <summary>[S5] "in line of sight": the enemy within 6" behind a wall that blocks sight is no Surprise Attack target.</summary>
    [Fact]
    public void Surprise_Attack_needs_line_of_sight_to_its_target()
    {
        var sa = Unit("SA", "4+", "4+", [], 1, [P(GfAmbush.SurpriseAttack, 3)]);
        var enemy = Unit("E", "4+", "6+", [], 1, []);
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(30, 26, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(8, 1), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.BlocksLineOfSight, null))]);
        foreach (var blocked in new[] { false, true })
        {
            var sim = Sim.Gf36(State([sa], [enemy], terrain: blocked ? [wall] : []));
            sim.Append([new ModelsMoved([new ModelRelocation(sa.Models[0].Id, sim.Model(sa.Models[0].Id).Location, new OnTable(new Placement(new Point3(30, 24, 0), new Yaw(0)))),
                new ModelRelocation(enemy.Models[0].Id, sim.Model(enemy.Models[0].Id).Location, new OnTable(new Placement(new Point3(30, 28, 0), new Yaw(0))))])]);
            Activate(sim, sa.Id);
            Assert.Equal(!blocked, Reactions(sim).Any(o => o.Id.StartsWith(GfAmbush.SurpriseAttack, StringComparison.Ordinal)));
        }
    }
}
