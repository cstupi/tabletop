using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// A 3.6.0 game driven through the scheduler (the session's loop): the sample table (72x48, frontline zones: team 1 y 36–48, team 2 y 0–12, a hill
/// about (12, 12), the objective at (36, 24)), Core36, a queue of dice. `real` false runs FakeRules.Gf() (Grimdark Future's scheduling with empty
/// action sequences); true runs GrimdarkFutureRules.V360.
/// </summary>
internal sealed class ClausesDeploy36
{
    public Sim Sim { get; }
    public QueueDice Source { get; } = new();
    public Queue<int> Dice => Source.Dice;
    public IReadOnlyList<Guid> Teams { get; }

    public ClausesDeploy36(IReadOnlyList<Army> armies, Enforcement enforcement = Enforcement.Strict, bool real = false, IReadOnlyList<Marker>? markers = null)
    {
        IGameRules rules = real ? GrimdarkFutureRules.V360 : FakeRules.Gf();
        var game = TestGames.Build(armies, enforcement: enforcement, rules: rules, markers: markers);
        var state = new GameState(game.State.Info with { GameRules = RuleSets.Gf360 }, game.State.Board, game.State.Applier, rules);
        Sim = new Sim(state, rules, TestProperties.Core36, Source, new AlternatingActivationScheduler(TestProperties.Core36));
        Teams = game.Teams;
    }

    public GameState State => Sim.State;
    public Projection P => State.Projection;
    public IReadOnlyList<Guid> Units(int team) => TeamUnits.Of(State, Teams[team]).Select(u => u.Id).ToList();
    public Ask Open => P.OpenAsks.Values.Select(a => a.Ask).Single();
    public int TeamOf(Ask ask) => Teams.ToList().IndexOf(ask.Teams.Single());
    public void Run() => Sim.Run();

    public Tabletop.GameServer.Dispatch.DispatchOutcome Try(Command command, int team) =>
        Sim.Dispatch(command, State.Info.Participants.First(p => p.TeamId == Teams[team]).Id, Teams[team]);

    public void Dispatch(Command command, int team)
    {
        if (Try(command, team) is Tabletop.GameServer.Dispatch.Refused r) throw new Xunit.Sdk.XunitException($"refused: {r.Reason} {r.Detail}");
        Sim.Run();
    }

    public void Answer(int team, AskAnswer answer) => Sim.Answer(Teams[team], answer);

    /// <summary>Every model of the unit placed in a row from (x, y), 2" between centres.</summary>
    public MoveModels At(Guid unit, double x, double y) => new(P.Units[unit].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, P.Models[m.Id].Location, new OnTable(new Placement(new Point3(x + i * 2, y, 0), new Yaw(0))))).ToList());

    public void SetAside(Guid unit, int team) => Dispatch(new SetCondition(TestGames.UnitSubject(unit), GfNames.SetAside, true), team);

    /// <summary>Picks the unit at the open deploy Ask (its team's) and places it at (x, y).</summary>
    public void Place(Guid unit, double x, double y)
    {
        var team = TeamOf(Open);
        Answer(team, new SelectedPieces([unit]));
        Dispatch(At(unit, x, y), team);
    }

    /// <summary>Activates the unit (Hold) at the open activate Ask.</summary>
    public void Activate(Guid unit) => Answer(TeamOf(Open), new Chosen(TestGames.Activate(unit)));
}

/// <summary>
/// Clauses, 3.6.0 p.13 Ambush: "May be set aside before deployment. [1] At the start of any round after the first, [2] may be deployed [3] anywhere over 9”
/// away from enemy units. [4] Players alternate in placing Ambush units, starting with the player that activates next. [5] Units that deploy via Ambush
/// can't seize or contest objectives on the round they deploy." [6] Set aside "before deployment" only.
/// Infiltrate: "[I1] Counts as having Ambush, but [I2] may be deployed anywhere over 3" away from enemy unit."
/// Rapid Ambush: "[R1] Counts as having Ambush, but [R2] may be deployed at the start of any round, including the first."
/// Surprise Attack (deployment half): "[S1] Counts as having Infiltrate."
/// Repel Ambushers: "[P1] Enemy units using Ambush [P2] must be set up over 12" away [P3] from this model's unit."
/// Ambush Beacon: "[B1] Friendly units using Ambush [B2] may ignore distance restrictions from enemies [B3] if they are deployed within 6" of this model."
/// Distances base to base (p.5). B's unit stands at (40, 4); a base is 1.26" across, so a centre 1.26" further than the gap.
/// </summary>
public class ClausesDeployAmbushTests
{
    /// <summary>A's unit 1 deployed at (40, 44), its unit 2 set aside with the rules given; B's unit 1 at (40, 4) (B's own rules); round two (or the
    /// round given) starts from the menu and A's unit 2 is picked at the Ambusher Ask.</summary>
    static (ClausesDeploy36 H, Guid Ambusher) Picked((int, string, int?)[] ambusher, (int, string, int?)[]? enemy = null, (int, string, int?)[]? own = null, double ownY = 44,
        Enforcement enforcement = Enforcement.Strict, int enemyModels = 1)
    {
        var a = TestArmies.Build("A", 2, 1, unitRules: [.. ambusher.Where(r => r.Item2 == Ambush)], modelRules: [.. ambusher.Where(r => r.Item2 != Ambush), .. own ?? []]);
        var h = new ClausesDeploy36([a, TestArmies.Build("B", 1, enemyModels, modelRules: enemy)], enforcement, real: true);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run();
        var au = h.Units(0); var bu = h.Units(1);
        h.SetAside(au[1], 0);
        h.Place(au[0], 40, ownY);
        h.Place(bu[0], 40, 4);
        h.Dispatch(new SetRound(2), 0);
        Assert.Equal(AskQuestions.DeployAmbusher, h.Open.Question);
        h.Answer(h.TeamOf(h.Open), new SelectedPieces([au[1]]));
        return (h, au[1]);
    }

    static string? Breach(ClausesDeploy36 h, Guid unit, double x, double y) => GrimdarkFutureRules.V360.PlacementBreach(h.State, h.At(unit, x, y).Moves)?.Rule;

    [Fact]
    public void Ambush_is_not_asked_in_round_one_and_a_pass_in_round_two_is_asked_again_in_round_three()
    {
        var h = new ClausesDeploy36([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.SetAside(a[1], 0);
        h.Place(a[0], 40, 44); h.Place(b[0], 40, 4);
        Assert.Equal((1, AskQuestions.Activate), (h.P.Scheduler.Round, h.Open.Question));                   // [1] round one opens on an activation, no Ambusher Ask
        h.Activate(a[0]); h.Activate(b[0]);
        h.Answer(0, new Confirmed());                                                                        // end round one
        Assert.Equal((2, AskQuestions.DeployAmbusher, h.Teams[0]), (h.P.Scheduler.Round, h.Open.Question, h.Open.Teams.Single()));
        Assert.True(h.Open.AllowPass);                                                                       // [2] "may"
        h.Answer(0, new Passed());
        Assert.Equal(AskQuestions.Activate, h.Open.Question);
        Assert.Contains(GfNames.SetAside, h.P.Units[a[1]].Conditions);
        h.Activate(a[0]); h.Activate(b[0]);
        h.Answer(0, new Confirmed());
        Assert.Equal((3, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.Open.Question));              // [1] "any round after the first"
        Assert.Equal([a[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
    }

    /// <summary>[4] B finished activating first in round one, so B activates first in round two (p.7) and places the first Ambusher, though A won the roll-off.</summary>
    [Fact]
    public void Ambushers_start_with_the_team_that_activates_next_not_the_roll_off_winner()
    {
        var h = new ClausesDeploy36([TestArmies.Build("A", 3, 1, unitRules: [(3, Ambush, null)]), TestArmies.Build("B", 2, 1, unitRules: [(2, Ambush, null)])]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();                                                         // A wins the roll-off
        var a = h.Units(0); var b = h.Units(1);
        h.SetAside(a[2], 0); h.SetAside(b[1], 1);
        h.Place(a[0], 30, 44); h.Place(b[0], 30, 4); h.Place(a[1], 50, 44);
        h.Activate(a[0]); h.Activate(b[0]); h.Activate(a[1]);                                              // B's last activation ends before A's
        h.Answer(0, new Confirmed());
        Assert.Equal(h.Teams[1], h.P.Scheduler.Order[0]);
        var asked = new List<Guid>();
        while (h.P.OpenAsks.Count > 0 && h.Open.Question == AskQuestions.DeployAmbusher)
        {
            var team = h.TeamOf(h.Open);
            asked.Add(h.Teams[team]);
            var unit = Assert.IsType<PieceSelection>(h.Open.Legal).Eligible[0];
            h.Answer(team, new SelectedPieces([unit]));
            h.Dispatch(h.At(unit, team == 0 ? 60 : 10, team == 0 ? 44 : 30), team);
        }
        Assert.Equal([h.Teams[1], h.Teams[0]], asked);
    }

    [Fact]
    public void An_Ambusher_exactly_9_inches_from_an_enemy_is_refused_and_just_over_is_accepted()
    {
        var (h, ambusher) = Picked([(2, Ambush, null)]);
        Assert.Equal(AmbushDistanceRule, Breach(h, ambusher, 40, 4 + 1.26 + 9));                           // [3] "over 9”": a gap of 9" is not over it
        Assert.Null(Breach(h, ambusher, 40, 4 + 1.26 + 9.05));
        Assert.Null(Breach(h, ambusher, 70, 30));                                                            // [3] "anywhere": outside every zone
    }

    [Fact]
    public void Infiltrate_keeps_over_3_inches_exactly_3_refused()
    {
        var (h, ambusher) = Picked([(2, GfAmbush.Infiltrate, null)]);                                        // [I1] set aside and asked as an Ambusher
        Assert.Equal(AmbushDistanceRule, Breach(h, ambusher, 40, 4 + 1.26 + 3));                            // [I2]
        Assert.Null(Breach(h, ambusher, 40, 4 + 1.26 + 3.05));
    }

    [Fact]
    public void Surprise_Attack_counts_as_having_Infiltrate_and_deploys_over_3_inches()
    {
        var (h, ambusher) = Picked([(2, GfAmbush.SurpriseAttack, 2)]);                                       // [S1] set aside and asked as an Ambusher
        Assert.Equal(AmbushDistanceRule, Breach(h, ambusher, 40, 4 + 1.26 + 3));
        Assert.Null(Breach(h, ambusher, 40, 4 + 1.26 + 3.05));
    }

    /// <summary>[R2] the Ambusher Ask opens at the start of round one for a Rapid Ambush unit, and only for it: not for Ambush [1] nor Infiltrate [I1].</summary>
    [Fact]
    public void Rapid_Ambush_is_asked_at_the_start_of_round_one()
    {
        var h = new ClausesDeploy36([TestArmies.Build("A", 4, 1, unitRules: [(2, Ambush, null)], modelRules: [(3, GfAmbush.RapidAmbush, null), (4, GfAmbush.Infiltrate, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.SetAside(a[1], 0); h.SetAside(a[2], 0); h.SetAside(a[3], 0);                                       // [R1] counts as having Ambush: may be set aside
        h.Place(a[0], 40, 44); h.Place(b[0], 40, 4);
        Assert.Equal((1, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.Open.Question));
        Assert.Equal([a[2]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
    }

    [Fact]
    public void Repel_Ambushers_holds_an_Infiltrator_over_12_inches_measured_from_every_model_of_the_holders_unit()
    {
        // [P3] "this model's unit": only B's model 1 carries the rule; model 2 stands 20" to the side at (60, 4).
        var a = TestArmies.Build("A", 2, 1, modelRules: [(2, GfAmbush.Infiltrate, null)]);
        var bArmy = TestArmies.Build("B", 1, 2);
        bArmy = bArmy with { Units = [bArmy.Units[0] with { Models = [bArmy.Units[0].Models[0] with { Properties = [new PropertyInstance(GfAmbush.RepelAmbushers, null)] }, bArmy.Units[0].Models[1]] }] };
        var h = new ClausesDeploy36([a, bArmy], real: true);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var au = h.Units(0); var bu = h.Units(1);
        h.SetAside(au[1], 0);
        h.Place(au[0], 40, 44);
        h.Answer(1, new SelectedPieces([bu[0]]));
        var models = h.P.Units[bu[0]].Definition.Models;
        h.Dispatch(new MoveModels([new(models[0].Id, new OnShelf(), new OnTable(new Placement(new Point3(40, 4, 0), new Yaw(0)))),
            new(models[1].Id, new OnShelf(), new OnTable(new Placement(new Point3(60, 4, 0), new Yaw(0))))]), 1);
        h.Dispatch(new SetRound(2), 0);
        h.Answer(0, new SelectedPieces([au[1]]));
        Assert.Equal(AmbushDistanceRule, Breach(h, au[1], 60, 4 + 1.26 + 10));                              // [P2]/[P3]: 10" from the model without the rule; Infiltrate's 3" is no help
        Assert.Equal(AmbushDistanceRule, Breach(h, au[1], 60, 4 + 1.26 + 12));                              // [P2] "over 12"
        Assert.Null(Breach(h, au[1], 60, 4 + 1.26 + 12.05));
    }

    [Fact]
    public void Repel_Ambushers_does_not_hold_back_its_own_teams_Ambushers()
    {
        var (h, ambusher) = Picked([(2, Ambush, null)], own: [(1, GfAmbush.RepelAmbushers, null)], ownY: 40);   // [P1] "Enemy units": A's own unit at (40, 40) holds it
        Assert.Null(Breach(h, ambusher, 46, 40));                                                            // 4.74" from A's own Repel unit, 36" from B
    }

    [Fact]
    public void An_enemy_Ambush_Beacon_lifts_nothing_and_a_friendly_one_lifts_Repel_Ambushers_too()
    {
        // [B1] "Friendly units": B's beacon at (40, 4) does not let A's Ambusher in.
        var (h, ambusher) = Picked([(2, Ambush, null)], enemy: [(1, GfAmbush.AmbushBeacon, null)]);
        Assert.Equal(AmbushDistanceRule, Breach(h, ambusher, 40, 8));

        // [B2] "distance restrictions from enemies": B's unit holds Repel Ambushers; A's beacon stands at (40, 12) under warn (out of A's zone).
        var (w, lifted) = Picked([(2, Ambush, null)], enemy: [(1, GfAmbush.RepelAmbushers, null)], own: [(1, GfAmbush.AmbushBeacon, null)], ownY: 12, enforcement: Enforcement.Warn);
        Assert.Null(Breach(w, lifted, 44, 12));                                                              // 2.74" from the beacon, about 8" from B's Repel unit
        Assert.Equal(AmbushDistanceRule, Breach(w, lifted, 52, 4));                                          // [B3] 10.7" from the beacon, 10.7" from B
    }

    /// <summary>[B3] "within 6" of this model": a beacon that is not on the table (never deployed: set aside) lifts nothing.</summary>
    [Fact]
    public void An_Ambush_Beacon_still_off_the_table_lifts_nothing()
    {
        var a = TestArmies.Build("A", 3, 1, unitRules: [(2, Ambush, null), (3, Ambush, null)], modelRules: [(3, GfAmbush.AmbushBeacon, null)]);
        var h = new ClausesDeploy36([a, TestArmies.Build("B", 1, 1)], real: true);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var au = h.Units(0); var bu = h.Units(1);
        h.SetAside(au[1], 0); h.SetAside(au[2], 0);
        h.Place(au[0], 40, 44); h.Place(bu[0], 40, 4);
        h.Dispatch(new SetRound(2), 0);
        h.Answer(0, new SelectedPieces([au[1]]));
        Assert.Equal(AmbushDistanceRule, Breach(h, au[1], 40, 8));
    }

    /// <summary>
    /// [5] and Transport p.15 "units inside Transports are deployed at the same time as the Transport": a unit that rides an Ambushing Transport in deploys
    /// via Ambush with it, so it neither seizes nor contests in the round it arrives, even once it has disembarked onto the marker.
    /// </summary>
    [Fact]
    public void A_passenger_of_an_Ambushing_Transport_neither_seizes_nor_contests_in_the_round_it_arrives()
    {
        var bus = Armies.Unit("Bus", "4+", "4+", [Armies.P(Ambush)], 1, [Armies.P("Transport", 6), Armies.P("Tough", 6)]);
        var troops = Armies.Unit("Troops", "4+", "4+", [Armies.P(Ambush)], 1, []);
        var blue = Armies.Unit("Blue", "4+", "4+", [], 1, []);
        var s = Armies.State([bus, troops], [blue]).Under(RuleSets.Gf360);
        void Apply(params EventPayload[] events) { foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null)); }
        var objective = s.Board.Table.Markers[0];
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [Protocol.Tests.Samples.Team1], new PieceSelection([bus.Id], 1, 1), true, false, null, null);
        Apply(new ModelsMoved([new(troops.Models[0].Id, s.Projection.Models[troops.Models[0].Id].Location, new Inside(bus.Models[0].Id))]),
            new RoundStarted(2, [Protocol.Tests.Samples.Team1, Protocol.Tests.Samples.Team2], []), new AskOpened(ask),
            new AskClosed(ask.Id, [new TeamAnswer(Protocol.Tests.Samples.Team1, new SelectedPieces([bus.Id]))]),
            new ModelsMoved([new(bus.Models[0].Id, s.Projection.Models[bus.Models[0].Id].Location, new OnTable(new Placement(new Point3(36, 34, 0), new Yaw(0))))]),
            new ModelsMoved([new(troops.Models[0].Id, new Inside(bus.Models[0].Id), new OnTable(new Placement(new Point3(36, 26, 0), new Yaw(0))))]),
            new ModelsMoved([new(blue.Models[0].Id, s.Projection.Models[blue.Models[0].Id].Location, new OnTable(new Placement(new Point3(36, 22, 0), new Yaw(0))))]));
        Assert.Equal([new ObjectiveControlSet(objective.Id, Protocol.Tests.Samples.Team2)], GfObjectives.Seize(s, TestProperties.Core36));
    }


    /// <summary>Infiltrating Precision Strike: "[X1] Counts as having Infiltrate, and [X2] gets +1 to hit rolls in melee [X3] on the round in which it deploys via
    /// this rule." The charge's hit roll carries the +1 while the unit arrived this round, and not otherwise.</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Infiltrating_Precision_Strike_gets_plus_one_to_hit_in_melee_on_its_arrival_round(bool arrived, int bonus)
    {
        var charger = Armies.Unit("IPS", "4+", "4+", [], 1, [Armies.P(GfAmbush.InfiltratingPrecisionStrike)], Armies.Melee("Blade", 1));
        var defender = Armies.Unit("D", "4+", "6+", [], 3, [], Armies.Melee("Claw", 1));
        var sim = Sim.Gf36(Armies.State([charger], [defender], x1: 30, x2: 31.3), 1, 1, 1, 1, 1, 1, 1, 1);
        if (arrived) sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfAmbush.AmbushedThisRound, true)]);
        sim.Start(Melee, new SequenceContext(charger.Id, [defender.Id], [.. charger.Models[0].Weapons.Select(w => w.Id)], Charge));
        var hit = sim.Emitted.OfType<StepCompleted>().Where(s => s.Step == "roll-to-hit").SelectMany(s => s.Rolls).First();
        Assert.Equal(bonus, hit.Modifiers.Where(m => m.Source == GfAmbush.InfiltratingPrecisionStrike).Sum(m => m.Amount));
    }
}
