using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class MoveRegionPolicyTests
{
    static readonly Guid Model = Guid.CreateVersion7(), Unit = Guid.CreateVersion7(), Part = Guid.CreateVersion7(), Team = Guid.CreateVersion7();

    static readonly Guid Other = Guid.CreateVersion7();

    /// <summary>The mover at `at`; with `blocker`, a model of another unit standing there.</summary>
    static (PolicyView View, Guid ModelId) Standing(Point3 at, TableSize? size = null, Point3? blocker = null)
    {
        var slot = new ModelSlot(Model, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [slot]);
        var other = new Unit(Guid.CreateVersion7(), 2, "o", new Dictionary<string, string>(), [], [new ModelSlot(Other, 1, "o", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, [])]);
        var table = new TableState(Guid.CreateVersion7(), null, size ?? new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", blocker is null ? [unit] : [unit, other]))]);
        var applier = new EventApplier(board, info);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([new ModelRelocation(Model, new OnShelf(), new OnTable(new Placement(at, new Yaw(0)))),
            .. blocker is { } b ? [new ModelRelocation(Other, new OnShelf(), new OnTable(new Placement(b, new Yaw(0))))] : Array.Empty<ModelRelocation>()]), null));
        return (new PolicyView(applier.Projection, board), Model);
    }

    [Fact]
    public void A_destination_is_within_the_allowance_and_on_the_table()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 6, [], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To);
            var check = Movement.Check(view.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 6, [], [], null, view.Table);
            Assert.True(check.WithinAllowance, $"seed {seed} moved {check.Distance}");
            Assert.False(check.LeavesTable);
            Assert.Equal(new OnTable(new Placement(view.PlacedOf(model)!.Value.Placement.Position, new Yaw(0))).GetType(), answer.Moves[0].From.GetType());
        }
    }

    [Fact]
    public void A_regroup_answers_every_model_of_the_region_and_the_ones_it_leaves_where_they_stand()
    {
        // p.7: a unit out of coherency regroups. The regroup's paths are found with the whole region off the table, so the answer names every
        // model: the server goes round only the models an answer leaves out (GfMoves.Contact).
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var slots = ids.Select((id, i) => new ModelSlot(id, i + 1, $"m{i + 1}", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, [])).ToList();
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], slots);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit]))]);
        var applier = new EventApplier(board, info);
        Point3[] at = [new(30, 24, 0), new(31.5, 24, 0), new(36, 24, 0)];
        applier.Apply(new GameEvent(1, null, new ModelsMoved([.. ids.Select((id, i) => new ModelRelocation(id, new OnShelf(), new OnTable(new Placement(at[i], new Yaw(0)))))]), null));
        var view = new PolicyView(applier.Projection, board);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion(ids, 6, [], [], null, null, Separation: 1), false, false, null, Guid.CreateVersion7());
        var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(1)).Answer(ask, view));
        Assert.Equal(ids.ToHashSet(), answer.Moves.Select(x => x.ModelId).ToHashSet());
        Assert.Contains(answer.Moves, x => x.From == x.To);
    }

    [Fact]
    public void A_charge_move_ends_in_base_contact_with_the_target()
    {
        // p.9: "Charging models must move by up to 12” to get into base contact with an enemy model from the target unit".
        var (view, model) = Standing(new Point3(20, 24, 0), blocker: new Point3(30, 26, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Charge", [Team], new MoveRegion([model], 12, [], [], 6, ContactWith: [Other]), true, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 10; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To).Placement;
            var landed = new PlacedModel(view.BodyOf(model), to);
            Assert.InRange(Distance.EdgeToEdge(landed, view.PlacedOf(Other)!.Value), 0, 0.25);
        }
    }

    [Fact]
    public void A_minimum_move_with_little_room_for_it_is_always_met_on_the_table()
    {
        // p.13 Aircraft: a 30" minimum on a 24" table from near a corner, where only bearings close to the far diagonal have room (just over 30").
        var (view, model) = Standing(new Point3(2, 2, 0), new TableSize(24, 24));
        var start = view.PlacedOf(model)!.Value;
        Assert.InRange(Movement.FarthestOnTable(start, view.Table).Distance, 30, 31);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 36, [], [], null, null, 30), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 40; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To).Placement.Position.Horizontal;
            var check = Movement.Check(start, to, 36, [], [], null, view.Table);
            Assert.True(check.Distance >= 30 - 1e-6, $"seed {seed} moved {check.Distance}");
            Assert.False(check.LeavesTable, $"seed {seed} left the table");
        }
    }

    [Fact]
    public void A_minimum_the_table_has_no_room_for_is_met_as_far_as_the_table_allows_in_the_direction_taken()
    {
        // Owner ruling: on a table without 30" of room an Aircraft moves as far as it can in the direction taken (GfMoves.Minimum, Movement.RoomAlong).
        var (view, model) = Standing(new Point3(12, 12, 0), new TableSize(24, 24));
        var start = view.PlacedOf(model)!.Value;
        Assert.True(Movement.FarthestOnTable(start, view.Table).Distance < 30);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 36, [], [], null, null, 30), false, false, null, Guid.CreateVersion7());
        var bearings = new HashSet<int>();
        for (var seed = 0ul; seed < 40; seed++)
        {
            var to = Assert.IsType<OnTable>(Assert.Single(Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view)).Moves).To).Placement.Position.Horizontal;
            var check = Movement.Check(start, to, 36, [], [], null, view.Table);
            Assert.False(check.LeavesTable, $"seed {seed} left the table");
            Assert.True(check.Distance >= Movement.RoomAlong(start, to, view.Table) - 1e-6, $"seed {seed} moved {check.Distance}");
            bearings.Add((int)Math.Round(Math.Atan2(to.Y - 12, to.X - 12) * 180 / Math.PI));
        }
        Assert.True(bearings.Count > 4, "every answer went the one way");   // not only toward the farthest corner
    }

    [Fact]
    public void A_minimum_is_met_clear_of_other_bases_when_no_sample_finds_a_landing()
    {
        // With no random sample to draw, the fallback still meets the 30" (p.13) clear of every base: a model stands on the point at 30" toward the
        // farthest corner, so standing still (refused under strict) or landing on it would stall the game.
        var (view, model) = Standing(new Point3(2, 2, 0), new TableSize(36, 36), blocker: new Point3(2 + 30 / Math.Sqrt(2), 2 + 30 / Math.Sqrt(2), 0));
        var start = view.PlacedOf(model)!.Value;
        var other = view.PlacedOf(Other)!.Value;
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 36, [], [], null, null, 30), false, false, null, Guid.CreateVersion7());
        var to = Assert.IsType<OnTable>(Assert.Single(Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(1), new PolicyOptions(MoveSamples: 0)).Answer(ask, view)).Moves).To).Placement;
        Assert.False(Overlap.Bases(start with { Placement = to }, other), "landed on the other base");
        Assert.True(start.Placement.Position.Horizontal.DistanceTo(to.Position.Horizontal) >= 30 - 1e-6, "short of the minimum");
    }

    [Fact]
    public void A_model_standing_in_an_impassable_volume_moves_out_of_it()
    {
        // Only a hand move puts a model there; it may head out of the volume, never along it, as the server judges it (Movement.Check).
        var (view, model) = Standing(new Point3(14, 24, 0));
        var wall = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [wall]);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 12, [wall.Id], [], null, null), false, false, null, Guid.CreateVersion7());
        var start = walled.PlacedOf(model)!.Value;
        for (var seed = 0ul; seed < 25; seed++)
        {
            var to = Assert.IsType<OnTable>(Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled)).Moves[0].To).Placement.Position.Horizontal;
            Assert.NotEqual(start.Placement.Position.Horizontal, to);
            Assert.True(Movement.Check(start, to, 12, [wall], [], null, walled.Table) is { CrossesImpassable: false, WithinAllowance: true }, $"seed {seed}");
        }
    }

    [Fact]
    public void An_impassable_volume_is_not_crossed()
    {
        var (view, model) = Standing(new Point3(10, 24, 0));
        var wall = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [wall]);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 12, [wall.Id], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled));
            var to = Assert.IsType<OnTable>(answer.Moves[0].To);
            Assert.False(Movement.Check(walled.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 12, [wall], [], null, walled.Table).CrossesImpassable);
        }
    }

    [Fact]
    public void A_move_whose_Ask_lists_no_impassable_volume_still_never_ends_in_one()
    {
        // A Flying unit's Ask lists no impassable volume to sweep, but the server refuses a landing in one (p.11, p.7).
        var (view, model) = Standing(new Point3(10, 24, 0));
        var block = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(8, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(17, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [block]);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 12, [], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled));
            if (answer.Moves[0].To is not OnTable to) continue;
            var at = walled.PlacedOf(model)!.Value;
            var landed = at with { Placement = to.Placement };
            Assert.False(Movement.Check(landed, to.Placement.Position.Horizontal, 0, [block], [], null, null).CrossesImpassable, $"seed {seed}");
        }
    }

    [Fact]
    public void Embarking_puts_every_model_inside_an_enterable_container()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var container = Guid.CreateVersion7();
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 6, [], [], null, new MoveContainment([container], null, null)), false, false, null, Guid.CreateVersion7());
        var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(4), new PolicyOptions(EmbarkProbability: 1)).Answer(ask, view));
        Assert.Equal(new Inside(container), answer.Moves[0].To);
    }

    [Fact]
    public void The_difficult_cap_does_not_shorten_the_reach_in_open_ground()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 12, [], [], 6), false, false, null, Guid.CreateVersion7());
        var farthest = 0.0;
        for (var seed = 0ul; seed < 40; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To);
            var d = Movement.Check(view.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 12, [], [], null, view.Table).Distance;
            Assert.True(d <= 12 + 1e-9);
            farthest = Math.Max(farthest, d);
        }
        Assert.True(farthest > 6, $"farthest {farthest}");
    }

    [Fact]
    public void No_sampled_destination_stands_still_or_passes_and_never_throws()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var region = new MoveRegion([model], 6, [], [], null, null);
        var none = new PolicyOptions(MoveSamples: 0);
        var forced = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], region, false, false, null, Guid.CreateVersion7());
        var moves = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(1), none).Answer(forced, view)).Moves;
        Assert.Equal(moves[0].From, moves[0].To);   // OnTable at the same placement: standing still
        var optional = forced with { AllowPass = true };
        Assert.IsType<Passed>(new AnswerPolicy(new Chooser(1), none).Answer(optional, view));
    }

    [Fact]
    public void A_model_inside_a_container_with_nowhere_to_stand_is_left_out_of_the_answer()
    {
        var (view, ask, _, _) = Inside();
        var none = new PolicyOptions(MoveSamples: 0);
        Assert.Empty(Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(1), none).Answer(ask, view)).Moves);   // a wreck's survivors: no pass
        Assert.IsType<Passed>(new AnswerPolicy(new Chooser(1), none).Answer(ask with { AllowPass = true }, view));
    }

    [Fact]
    public void A_disembark_never_lands_in_impassable_terrain()
    {
        var (view, ask, rider, _) = Inside();
        var block = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(6, 20), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(34, 20, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [block]);
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled));
            if (answer.Moves.SingleOrDefault()?.To is not OnTable to) continue;
            var landed = new PlacedModel(walled.BodyOf(rider), to.Placement);
            Assert.False(Movement.Check(landed, to.Placement.Position.Horizontal, 0, [block], [], null, null).CrossesImpassable, $"seed {seed}");
        }
    }

    /// <summary>A rider inside a wreck standing at (30, 20), and the survivors' Ask (no pass).</summary>
    static (PolicyView View, Ask Ask, Guid Rider, Point3 At) Inside()
    {
        var wreck = Guid.CreateVersion7(); var rider = Guid.CreateVersion7();
        ModelSlot Slot(Guid id) => new(id, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        // The transport is a unit of its own: the rider's unit is judged for coherency on its own models (p.7).
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [Slot(rider)]);
        var hull = new Unit(Guid.CreateVersion7(), 2, "w", new Dictionary<string, string>(), [], [Slot(wreck)]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit, hull]))]);
        var applier = new EventApplier(board, info);
        var at = new Point3(30, 20, 0);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([
            new ModelRelocation(wreck, new OnShelf(), new OnTable(new Placement(at, new Yaw(0)))),
            new ModelRelocation(rider, new OnShelf(), new Inside(wreck))]), null));
        var ask = new Ask(Guid.CreateVersion7(), "disembark", "Disembark", [Team],
            new MoveRegion([rider], 6, [], [], null, new MoveContainment([], wreck, 6)), false, false, null, Guid.CreateVersion7());
        return (new PolicyView(applier.Projection, board), ask, rider, at);
    }

    [Fact]
    public void Disembarking_places_the_models_on_the_table_within_the_exit_distance_of_the_container()
    {
        var wreck = Guid.CreateVersion7(); var rider = Guid.CreateVersion7();
        ModelSlot Slot(Guid id) => new(id, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        // The transport is a unit of its own: the rider's unit is judged for coherency on its own models (p.7).
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [Slot(rider)]);
        var hull = new Unit(Guid.CreateVersion7(), 2, "w", new Dictionary<string, string>(), [], [Slot(wreck)]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit, hull]))]);
        var applier = new EventApplier(board, info);
        var at = new Point3(30, 20, 0);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([
            new ModelRelocation(wreck, new OnShelf(), new OnTable(new Placement(at, new Yaw(0)))),
            new ModelRelocation(rider, new OnShelf(), new Inside(wreck))]), null));
        var view = new PolicyView(applier.Projection, board);
        var ask = new Ask(Guid.CreateVersion7(), "disembark", "Disembark", [Team],
            new MoveRegion([rider], 6, [], [], null, new MoveContainment([], wreck, 6)), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var move = Assert.Single(answer.Moves);
            Assert.Equal(new Inside(wreck), move.From);
            var to = Assert.IsType<OnTable>(move.To);
            Assert.True(to.Placement.Position.Horizontal.DistanceTo(at.Horizontal) <= 6 + 1e-9, $"seed {seed}");
        }
    }
}
