using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Lifecycle;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Lifecycle;

/// <summary>The whole-list shapes' checks over hand-built logs: Reinforcement's one round, the copies' bodies, every unit coming on, the game being a game.</summary>
public class LifecycleWholeTests
{
    // Red: unit 1 (two models) holds Reinforcement and unit 2 is its copy; unit 3 an Ambusher. Blue: one unit.
    static readonly CompiledScenario C = ScenarioCompiler.Compile(new ScenarioBuilder("whole", 1)
        .Army("Red", 3, 2, reserves: [new ScenarioReserve(2, "Reinforcement", 1)]).Army("Blue", 1, 1).Build());
    static readonly ArmySnapshot Red = C.Board.Armies[0].Snapshot;
    static readonly Unit Original = Red.Units[0], Copy = Red.Units[1], Ambusher = Red.Units[2], Blue = C.Board.Armies[1].Snapshot.Units[0];
    static readonly Guid RedTeam = C.Info.Teams[0].Id, BlueTeam = C.Info.Teams[1].Id;

    static GameEvent E(long seq, EventPayload p) => new(seq, null, p, null);
    static RoundStarted Round(int n) => new(n, [RedTeam, BlueTeam], []);
    static ModelRelocation On(ModelSlot m, int x = 10) => new(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0))));
    static ModelRelocation Off(ModelSlot m) => new(m.Id, new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0))), new OnShelf());
    static ModelsMoved Place(Unit u) => new(u.Models.Select((m, i) => On(m, 10 + 2 * i)).ToList());
    static ModelsMoved Kill(Unit u) => new(u.Models.Select(Off).ToList());

    static Ask Pick(string question, Guid team, Guid unit) =>
        new(Guid.CreateVersion7(), question, question, [team], new PieceSelection([unit], 1, 1), AllowPass: question != AskQuestions.Deploy, Sealed: false, WindowKind: null, FrameId: null);

    /// <summary>The Ask, its close answered with the unit, and the unit's models placed.</summary>
    static IEnumerable<EventPayload> Deploy(string question, Guid team, Unit unit)
    {
        var ask = Pick(question, team, unit.Id);
        return [new AskOpened(ask), new AskClosed(ask.Id, [new TeamAnswer(team, new SelectedPieces([unit.Id]))]), Place(unit)];
    }

    static LogQueries Log(params EventPayload[] payloads)
    {
        var events = payloads.Select((p, i) => E(i + 1, p)).ToList();
        var applier = new EventApplier(C.Board, C.Info);
        foreach (var e in events) applier.Apply(e);
        return new LogQueries(events, applier.Projection);
    }

    /// <summary>Deployment of Red's unit 1 and Blue's unit, then rounds 1..4 with `inRound` events added after each round's start.</summary>
    static LogQueries Game(Func<int, IEnumerable<EventPayload>> inRound) => Log([
        .. Deploy(AskQuestions.Deploy, RedTeam, Original), .. Deploy(AskQuestions.Deploy, BlueTeam, Blue),
        .. Enumerable.Range(1, 4).SelectMany(r => inRound(r).Prepend(Round(r))),
    ]);

    [Fact]
    public void A_copy_offered_in_the_round_after_its_original_was_destroyed_holds()
    {
        var log = Game(r => r switch
        {
            2 => [Kill(Original)],
            3 => Deploy(AskQuestions.DeployReinforcement, RedTeam, Copy),
            _ => [],
        });
        Assert.Empty(WholeGame.ReinforcementTiming(log, C.Board.Armies));
    }

    [Fact]
    public void A_copy_offered_a_round_late_or_again_fails()
    {
        var late = Game(r => r switch { 1 => [Kill(Original)], 3 => [new AskOpened(Pick(AskQuestions.DeployReinforcement, RedTeam, Copy.Id))], _ => [] });
        Assert.Contains("destroyed in round 1, had its copy offered in round(s) [3], not round 2 alone", Assert.Single(WholeGame.ReinforcementTiming(late, C.Board.Armies)));

        var again = Game(r => r switch
        {
            1 => [Kill(Original)],
            2 or 3 => [new AskOpened(Pick(AskQuestions.DeployReinforcement, RedTeam, Copy.Id))],
            _ => [],
        });
        Assert.Contains("[2, 3]", Assert.Single(WholeGame.ReinforcementTiming(again, C.Board.Armies)));
    }

    [Fact]
    public void A_copy_offered_while_its_original_stands_fails_and_one_destroyed_in_round_four_is_never_offered()
    {
        var standing = Game(r => r == 2 ? [new AskOpened(Pick(AskQuestions.DeployReinforcement, RedTeam, Copy.Id))] : []);
        Assert.Contains("not destroyed, had its copy offered in round(s) [2]", Assert.Single(WholeGame.ReinforcementTiming(standing, C.Board.Armies)));
        Assert.Empty(WholeGame.ReinforcementTiming(Game(r => r == 4 ? [Kill(Original)] : []), C.Board.Armies));
    }

    [Fact]
    public void A_copy_on_its_originals_bodies_holds_and_one_on_another_base_fails()
    {
        Assert.Empty(WholeGame.CopiesBodied(C.Board.Armies));
        var reshaped = Red with { Units = [.. Red.Units.Select(u => u.Id == Copy.Id ? u with { Models = [u.Models[0] with { Base = Base.Circle(2) }, u.Models[1]] } : u)] };
        var armies = new[] { C.Board.Armies[0] with { Snapshot = reshaped }, C.Board.Armies[1] };
        Assert.Contains("copy slot 1", Assert.Single(WholeGame.CopiesBodied(armies)));
    }

    [Fact]
    public void Every_unit_but_a_copy_comes_on_by_its_own_deploy_ask()
    {
        var log = Game(r => r == 2 ? Deploy(AskQuestions.DeployAmbusher, RedTeam, Ambusher) : []);
        Assert.Empty(WholeGame.Deployed(log, C.Board.Armies, new HashSet<Guid> { Ambusher.Id }));

        var never = Game(_ => []);
        Assert.Contains("Red unit 3 never came on: 0 of 2", Assert.Single(WholeGame.Deployed(never, C.Board.Armies, new HashSet<Guid>()), f => f.Contains("never came on")));

        var early = Game(r => r == 1 ? Deploy(AskQuestions.DeployAmbusher, RedTeam, Ambusher) : []);
        Assert.Contains("(set aside) was picked by deploy_ambusher in round 1", Assert.Single(WholeGame.Deployed(early, C.Board.Armies, new HashSet<Guid> { Ambusher.Id })));
    }

    [Fact]
    public void The_record_counts_from_the_log_and_a_game_without_melee_or_morale_fails()
    {
        var log = Game(r => r == 2 ? [Kill(Original)] : []);
        var answered = new[] { new AnsweredAsk("activate", 0, Guid.NewGuid(), new Passed(), false), new AnsweredAsk("activate", 1, Guid.NewGuid(), new Passed(), false) };
        var record = WholeGame.Record(log, C.Board.Armies, answered);
        Assert.Equal((4, 2, 1, 1, 0), (record.Rounds, record.Casualties, record.UnitsDestroyed, record.ReinforcementCopies, record.ReinforcementsPlaced));
        Assert.Equal(2, record.AsksByQuestion["activate"]);
        Assert.Equal(["no shooting attack was made", "no melee attack was made", "no morale test was taken"], WholeGame.Played(log, record).Skip(1));
        Assert.Equal("the log holds no game_ended", WholeGame.Played(log, record)[0]);
    }

    [Fact]
    public void A_warned_event_is_a_failure()
    {
        Assert.Empty(WholeGame.Warnings([E(1, Round(1))]));
        Assert.Contains("move_overlap", Assert.Single(WholeGame.Warnings([E(1, Round(1)), new GameEvent(2, null, Kill(Original), "move_overlap")])));
    }

    [Fact]
    public void The_player_takes_a_charge_and_a_shot_when_offered_and_otherwise_leaves_the_ask_to_the_policy()
    {
        var activate = new Ask(Guid.NewGuid(), AskQuestions.Activate, "", [RedTeam],
            new Options([new AskOption("a", "Red unit 1: Advance", []), new AskOption("c", "Red unit 1: Charge", [])]), false, false, null, null);
        Assert.Equal(new Chosen("c"), LifecyclePlayer.Script.Match(activate, Log().Final));
        var noCharge = activate with { Legal = new Options([new AskOption("a", "Red unit 1: Advance", [])]) };
        Assert.Null(LifecyclePlayer.Script.Match(noCharge, Log().Final));
        var shoot = new Ask(Guid.NewGuid(), "shoot_or_not", "", [RedTeam], new Options([new AskOption("shoot", "Shoot", []), new AskOption("pass", "Don't shoot", [])]), false, false, null, null);
        Assert.Equal(new Chosen("shoot"), LifecyclePlayer.Script.Match(shoot, Log().Final));
    }

    [Fact]
    public void The_players_move_goal_is_the_nearest_enemy_model()
    {
        var log = Log(new ModelsMoved([On(Original.Models[0], 10), On(Ambusher.Models[0], 40), On(Blue.Models[0], 30)]));
        var view = new PolicyView(log.Final, C.Board, null, C.Info, C.Properties);
        Assert.Equal(new Point2(30, 10), LifecyclePlayer.NearestEnemy(Original.Models[0].Id, view));
        Assert.Equal(new Point2(40, 10), LifecyclePlayer.NearestEnemy(Blue.Models[0].Id, view));
        Assert.Null(LifecyclePlayer.NearestEnemy(Original.Models[1].Id, view));
    }
}
