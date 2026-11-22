using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>
/// The move path drawn and warned of before a commit and kept after it (spec 2026-11-21-move-path-and-dangerous-warning-design): each model's route,
/// its footprints and the dangerous volumes its swept base enters, by the same geometry the server tests with. Owner ruling: any part of the base counts.
/// </summary>
public class MovePathsTests
{
    /// <summary>The game "Test" (dev DB, seq 309): a 3.6 Rush of five Flayer Cavalry on 60 × 35 mm ovals, 16" past an 8"-wide toxic smog. Rider 1's
    /// centre line passes 4.72" from the smog's centre and its base edge clips about 0.35" in; the server tested dangerous terrain for rider 1 alone.</summary>
    internal static class FlayerRush
    {
        public static readonly TableSize Table = new(72, 48);
        public static readonly Base Oval = Base.Oval(2.362204724409449, 1.3779527559055118);

        static int _made;
        /// <summary>Fixed ids, so the viewer fixture written from this scene stays the same from run to run.</summary>
        static Guid Next() => new($"00000000-0000-4000-8000-{++_made:D12}");

        static WorldVolume Piece(double x, double y, double yaw, VolumeKind kind, bool circle, double a, double b, double top, TerrainDeclarations d) =>
            new(Next(),new Volume(kind, circle ? Footprint.Circle(a) : Footprint.Rect(a, b), new Point2(0, 0), new Yaw(0), 0, top, d, null),
                new TerrainTransform(new Point3(x, y, 0), new Yaw(yaw), 1));

        const TerrainDeclarations Unstable = TerrainDeclarations.DangerousOnRushCharge, Dangerous = TerrainDeclarations.Dangerous,
            Impassable = TerrainDeclarations.Impassable, Difficult = TerrainDeclarations.Difficult, Steep = TerrainDeclarations.Steep;

        public static readonly WorldVolume Smog = Piece(15.955780558267506, 16.17559109636377, 255, VolumeKind.Area, true, 4, 4, 6, Dangerous);
        public static readonly WorldVolume[] Volumes =
        [
            Piece(4.337871564709523, 24, 45, VolumeKind.Area, false, 6, 6, 4, Unstable),
            Piece(8.031871411964238, 3.1380079254219346, 180, VolumeKind.Area, true, 3, 3, 1, 0),
            Piece(12.843534211089617, 36.053910853366325, 270, VolumeKind.Solid, false, 6, 6, 8, Impassable),
            Smog,
            Piece(20.684918390905914, 43.361806465015654, 315, VolumeKind.Area, true, 3, 3, 1, 0),
            Piece(24.73499442671193, 24, 150, VolumeKind.Area, false, 6, 6, 0.5, Difficult),
            Piece(31.126120931993295, 32.40564565379436, 105, VolumeKind.Area, true, 4, 4, 6, Dangerous),
            Piece(31.390014894022613, 5.88636164641304, 225, VolumeKind.Solid, false, 6, 6, 8, Impassable),
            Piece(37.013229330863695, 23.216379165649414, 45, VolumeKind.Solid, false, 6, 4, 1.5, Steep),
            Piece(40.60998510597739, 42.11363835358696, 45, VolumeKind.Solid, false, 4, 4, 4, Impassable),
            Piece(41.041390054334826, 14.872346594740797, 285, VolumeKind.Area, true, 4, 4, 6, Dangerous),
            Piece(48.14221824339694, 24, 105, VolumeKind.Solid, false, 4, 4, 4, Impassable),
            Piece(51.315081609094086, 4.638193534984346, 135, VolumeKind.Solid, false, 6, 4, 1.5, Steep),
            Piece(56.044219441732494, 31.82440890363623, 75, VolumeKind.Area, false, 6, 6, 4, Unstable),
            Piece(59.15646578891038, 11.946089146633675, 90, VolumeKind.Solid, false, 6, 6, 8, Impassable),
            Piece(63.96812858803576, 44.86199207457807, 0, VolumeKind.Area, true, 3, 3, 1, 0),
            Piece(67.61812327598135, 24, 225, VolumeKind.Area, false, 6, 6, 0.1, Dangerous),
        ];

        static List<Guid> With(TerrainDeclarations d) => Volumes.Where(v => v.Volume.Declarations.HasFlag(d)).Select(v => v.Id).ToList();

        public static readonly Guid[] Ids = [.. Enumerable.Range(0, 5).Select(_ => Next())];
        public static readonly (Point2 From, Point2 To)[] Moves =
        [
            (new(14.497011184692383, 9.693385124206543), new(28.048744327367796, 18.199296529206755)),
            (new(16.074962615966797, 9.693385124206543), new(29.9576309014231, 17.64772490045357)),
            (new(17.652915954589844, 9.693385124206543), new(31.882734934652806, 17.00859201086668)),
            (new(19.23086929321289, 9.693385124206543), new(33.763499793903854, 16.240039245426026)),
            (new(20.808820724487305, 9.693385124206543), new(34.76118264699538, 15.017517407540039)),
        ];
        public static readonly IReadOnlyDictionary<Guid, PlacedModel> Starts = Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id,
            x => new PlacedModel(new ModelBody(Oval, 4.43), new Placement(new Point3(Moves[x.i].From.X, Moves[x.i].From.Y, 0), new Yaw(0))));
        public static readonly IReadOnlyDictionary<Guid, Point2> To = Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => Moves[x.i].To);

        /// <summary>The Ask the server opened (RegionDangers under 3.6.0: smogs and the mine field dangerous, the ruins unstable over 6").</summary>
        public static readonly MoveRegion Rush = new(Ids, 16, With(Impassable), With(Difficult), 6, Separation: 1, SteepVolumeIds: With(Steep),
            DangerousVolumeIds: With(Dangerous), UnstableVolumeIds: With(Unstable), UnstableOver: 6);

        public static MoveRegions Regions() => new(Starts, Rush, Volumes, Table, []);
    }

    static string Name(Guid id) => $"Flayer Cavalry {Array.IndexOf(FlayerRush.Ids, id) + 1}";
    static string Piece(Guid id) => id == FlayerRush.Smog.Id ? "Toxic Smog" : "Other";

    [Fact]
    public void Rider_one_crossing_the_smogs_edge_is_dangerous_and_the_other_four_are_not()
    {
        var paths = MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, FlayerRush.To);
        Assert.Equal(FlayerRush.Ids, paths.Select(p => p.ModelId));
        Assert.Equal([FlayerRush.Smog.Id], paths[0].Dangerous);
        Assert.All(paths.Skip(1), p => Assert.Empty(p.Dangerous));
        Assert.Contains(paths[0].Footprints, f => f.Dangerous);
        Assert.All(paths.Skip(1), p => Assert.DoesNotContain(p.Footprints, f => f.Dangerous));
    }

    [Fact]
    public void Each_path_runs_from_where_the_model_stood_to_its_ghost()
    {
        var paths = MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, FlayerRush.To);
        for (var i = 0; i < paths.Count; i++)
        {
            Assert.Equal(FlayerRush.Moves[i].From, paths[i].Route[0]);
            Assert.Equal(FlayerRush.Moves[i].To, paths[i].Route[^1]);
            Assert.Equal(new Yaw(0), paths[i].Start.Placement.Yaw);
        }
    }

    [Fact]
    public void A_model_not_moved_has_no_path()
    {
        var to = FlayerRush.To.ToDictionary(kv => kv.Key, kv => kv.Value);
        to[FlayerRush.Ids[2]] = FlayerRush.Moves[2].From;
        var paths = MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, to);
        Assert.DoesNotContain(paths, p => p.ModelId == FlayerRush.Ids[2]);
    }

    [Fact]
    public void A_crossing_is_marked_on_its_model_and_named_in_the_gate_line()
    {
        var paths = MovePaths.Proposed(FlayerRush.Regions(), FlayerRush.Starts, FlayerRush.Rush, FlayerRush.Volumes, FlayerRush.To);
        Assert.Equal(new Dictionary<Guid, string> { [FlayerRush.Ids[0]] = "Dangerous terrain test" }, MovePaths.Marks(paths));
        Assert.Equal(["Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test"], MovePaths.Lines(paths, Name, Piece));
    }

    [Fact]
    public void A_dangerous_crossing_warns_and_never_holds_the_commit()
    {
        var line = "Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test";
        Assert.Equal(new MoveGate(true, line + "."), MoveRuler.Gate([MoveRuler.Read(16, 16)], Enforcement.Strict, null, [line]));
        Assert.Equal(new MoveGate(false, "1 model past its allowance · " + line + "."), MoveRuler.Gate([MoveRuler.Read(17, 16)], Enforcement.Strict, null, [line]));
    }

    [Fact]
    public void The_kept_path_is_the_one_the_server_measured_with_the_same_dangerous_rider()
    {
        var moves = FlayerRush.Ids.Select((id, i) => new ModelRelocation(id, new OnTable(FlayerRush.Starts[id].Placement),
            new OnTable(new Placement(new Point3(FlayerRush.Moves[i].To.X, FlayerRush.Moves[i].To.Y, 0), new Yaw(-50.78))))).ToList();
        var kept = MovePaths.Measured(new MoveSegment(FlayerRush.Rush, moves), id => FlayerRush.Starts[id].Body,
            FlayerRush.Volumes, FlayerRush.Table, []);
        Assert.Equal([FlayerRush.Smog.Id], kept[0].Dangerous);
        Assert.All(kept.Skip(1), p => Assert.Empty(p.Dangerous));
        Assert.Equal(FlayerRush.Moves[0].To, kept[0].Route[^1]);
    }

    [Fact]
    public void A_footprint_outline_is_the_base_at_the_starts_facing()
    {
        var oval = MovePaths.Outline(FlayerRush.Oval, new Point2(10, 10), new Yaw(0), 4);
        Assert.Equal([new Point2(10.688976, 10), new(10, 11.181102), new(9.311024, 10), new(10, 8.818898)],
            oval.Select(p => new Point2(Math.Round(p.X, 6), Math.Round(p.Y, 6))));
        var square = MovePaths.Outline(Base.Rect(2, 1), new Point2(0, 0), new Yaw(90), 4);
        Assert.Equal([new Point2(1, -0.5), new(1, 0.5), new(-1, 0.5), new(-1, -0.5)], square.Select(p => new Point2(Math.Round(p.X, 6) + 0, Math.Round(p.Y, 6) + 0)));
    }

    [Fact]
    public void Footprints_are_faint_in_the_team_colour_and_a_dangerous_one_in_the_dangerous_colour()
    {
        var team = new Rgba(0.2f, 0.4f, 0.9f);
        // Over the move's clear band, which is the team colour itself, the path is the team colour lifted halfway to white.
        var light = new Rgba(0.6f, 0.7f, 0.95f);
        Assert.Equal(light with { A = MovePaths.FootprintAlpha }, MovePaths.Colour(new PathFootprint(new Point2(0, 0), false), team));
        Assert.Equal(light with { A = MovePaths.LineAlpha }, MovePaths.Line(team));
        Assert.Equal(TerrainEffects.Of(TerrainDeclarations.Dangerous).Colour with { A = MovePaths.DangerousAlpha },
            MovePaths.Colour(new PathFootprint(new Point2(0, 0), true), team));
        Assert.True(MovePaths.FootprintAlpha < 1 && MovePaths.LineAlpha < 1);
    }

    static GameEvent E(long seq, EventPayload payload) => new(seq, null, payload, null);

    [Fact]
    public void The_last_move_is_kept_until_the_next_activation_starts()
    {
        var unit = Guid.CreateVersion7(); var frame = Guid.CreateVersion7(); var ask = Guid.CreateVersion7();
        var moves = new[] { new ModelRelocation(FlayerRush.Ids[0], new OnTable(FlayerRush.Starts[FlayerRush.Ids[0]].Placement),
            new OnTable(new Placement(new Point3(28, 18, 0), new Yaw(0)))) };
        List<GameEvent> log =
        [
            E(1, new ActionRecorded(unit, "Rush")),
            E(2, new AskOpened(new Ask(ask, "move", "Move", [], FlayerRush.Rush, false, false, null, frame))),
            E(3, new StepCompleted(frame, "move", new Destinations(moves), [], [])),
            E(4, new StepCompleted(frame, "shoot-or-not", null, [], [])),
        ];
        var segment = Assert.Single(MovePaths.Last(log));
        Assert.Equal(FlayerRush.Rush, segment.Legal);
        Assert.Equal(moves, segment.Moves);
        log.Add(E(5, new TurnStarted(Guid.CreateVersion7())));
        Assert.Single(MovePaths.Last(log));                                                        // the next team's turn opens: still kept
        log.Add(E(6, new ActionRecorded(Guid.CreateVersion7(), "Hold")));
        Assert.Empty(MovePaths.Last(log));                                                         // the next activation started
    }

    [Fact]
    public void Every_segment_of_the_last_activations_move_is_kept()
    {
        var unit = Guid.CreateVersion7(); var frame = Guid.CreateVersion7();
        ModelRelocation[] Leg(double y0, double y1) => [new(FlayerRush.Ids[0], new OnTable(new Placement(new Point3(10, y0, 0), new Yaw(0))),
            new OnTable(new Placement(new Point3(10, y1, 0), new Yaw(0))))];
        var first = FlayerRush.Rush with { Stoppable = true }; var second = FlayerRush.Rush with { Allowance = 8 };
        List<GameEvent> log =
        [
            E(1, new StepCompleted(frame, "move", new Destinations(Leg(0, 2)), [], [])),                  // an earlier activation's
            E(2, new ActionRecorded(unit, "Advance")),
            E(3, new AskOpened(new Ask(Guid.CreateVersion7(), "move", "Move", [], first, false, false, null, frame))),
            E(4, new StepCompleted(frame, "move", new Destinations(Leg(5, 9), true), [], [])),
            E(5, new AskOpened(new Ask(Guid.CreateVersion7(), "move", "Move", [], second, false, false, null, frame))),
            E(6, new StepCompleted(frame, "move", new Destinations(Leg(9, 12)), [], [])),
        ];
        var kept = MovePaths.Last(log);
        Assert.Equal([first, second], kept.Select(s => s.Legal));
    }
}
