using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class ObjectiveGoalTests
{
    static readonly Guid Model = Guid.CreateVersion7(), Unit = Guid.CreateVersion7(), Part = Guid.CreateVersion7(), Team = Guid.CreateVersion7(),
        Enemy = Guid.CreateVersion7();

    static Marker Objective(double x, double y) =>
        new(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(x, y, 0), new Yaw(0), 2, null, VisualRef.OfBuiltin("objective-ring"));

    /// <summary>One model of team 1 standing at <paramref name="at"/>, the markers on the setup table, each controlled as given.</summary>
    static (PolicyView View, Guid ModelId) Standing(Point3 at, IReadOnlyList<Marker> markers, IReadOnlyDictionary<Guid, Guid>? control = null)
    {
        var slot = new ModelSlot(Model, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [slot]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], markers, []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1), new Team(Enemy, 2)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit]))]);
        var applier = new EventApplier(board, info);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([new ModelRelocation(Model, new OnShelf(), new OnTable(new Placement(at, new Yaw(0))))]), null));
        var seq = 2L;
        foreach (var (marker, team) in control ?? new Dictionary<Guid, Guid>())
            applier.Apply(new GameEvent(seq++, null, new ObjectiveControlSet(marker, team), null));
        return (new PolicyView(applier.Projection, board, info: info), Model);
    }

    static Ask Move(Guid model, double allowance = 6) =>
        new(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], allowance, [], [], null, null), false, false, null, Guid.CreateVersion7());

    static Point2 To(AskAnswer answer) => Assert.IsType<OnTable>(Assert.Single(Assert.IsType<Destinations>(answer).Moves).To).Placement.Position.Horizontal;

    [Fact]
    public void The_goal_is_the_nearest_objective_marker_the_models_team_does_not_control()
    {
        var near = Objective(36, 30); var far = Objective(36, 4);
        var (view, model) = Standing(new Point3(36, 24, 0), [near, far], new Dictionary<Guid, Guid> { [near.Id] = Team });
        Assert.Equal(far.Position.Horizontal, ObjectiveGoal.Nearest(model, view));

        var (contested, _) = Standing(new Point3(36, 24, 0), [near, far], new Dictionary<Guid, Guid> { [near.Id] = Enemy });
        Assert.Equal(near.Position.Horizontal, ObjectiveGoal.Nearest(model, contested));      // the enemy's marker is one to take
    }

    [Fact]
    public void No_goal_when_the_team_controls_every_objective_marker_or_there_is_none()
    {
        var only = Objective(36, 30);
        var (held, model) = Standing(new Point3(36, 24, 0), [only], new Dictionary<Guid, Guid> { [only.Id] = Team });
        Assert.Null(ObjectiveGoal.Nearest(model, held));
        var (bare, _) = Standing(new Point3(36, 24, 0), []);
        Assert.Null(ObjectiveGoal.Nearest(model, bare));
    }

    [Fact]
    public void A_goal_directed_move_stays_within_the_allowance_and_closes_on_the_marker()
    {
        var marker = Objective(36, 40);
        var (view, model) = Standing(new Point3(36, 24, 0), [marker]);
        for (var seed = 0ul; seed < 25; seed++)
        {
            var to = To(new AnswerPolicy(new Chooser(seed), goal: ObjectiveGoal.Nearest).Answer(Move(model), view));
            var check = Movement.Check(view.PlacedOf(model)!.Value, to, 6, [], [], null, view.Table);
            Assert.True(check.WithinAllowance, $"seed {seed} moved {check.Distance}");
            Assert.True(to.DistanceTo(marker.Position.Horizontal) < 14, $"seed {seed} ended {to.DistanceTo(marker.Position.Horizontal)} away");
        }
    }

    [Fact]
    public void A_goal_directed_move_is_deterministic_for_a_seed()
    {
        var (view, model) = Standing(new Point3(36, 24, 0), [Objective(60, 10)]);
        var a = To(new AnswerPolicy(new Chooser(9), goal: ObjectiveGoal.Nearest).Answer(Move(model), view));
        var b = To(new AnswerPolicy(new Chooser(9), goal: ObjectiveGoal.Nearest).Answer(Move(model), view));
        Assert.Equal(a, b);
    }

    [Fact]
    public void With_no_goal_to_seek_the_move_is_the_uniform_policys_own()
    {
        var (view, model) = Standing(new Point3(36, 24, 0), []);
        for (var seed = 0ul; seed < 10; seed++)
            Assert.Equal(To(new AnswerPolicy(new Chooser(seed)).Answer(Move(model), view)),
                To(new AnswerPolicy(new Chooser(seed), goal: ObjectiveGoal.Nearest).Answer(Move(model), view)));
    }
}
