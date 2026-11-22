using System.Text;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>
/// Writes viewer/tests/fixtures/movePaths.json: Godot's MovePaths over recorded logs, cut after each move step and at the next activation's start
/// (the kept segments, each moved model's route, footprints and dangerous volumes, the marks and gate lines), and its footprint outlines and colours.
/// viewer/tests/movePaths.agreement.test.ts computes the same through the browser's port and the WebAssembly Geometry.
/// Written only with TABLETOP_WRITE_FIXTURES=1 (--filter MovePathsViewerFixture); a plain run fails when the checked-in file differs.
/// </summary>
public class MovePathsViewerFixtureTests
{
    static readonly string[] Logs = ["skirmish", "terrain"];
    const int CutsPerLog = 6;

    static string FixturePath => Path.Combine(CallCases.RepoRoot(), "viewer", "tests", "fixtures", "movePaths.json");

    static JsonArray Points(IEnumerable<Point2> points) => new(points.Select(p => (JsonNode?)new JsonArray(p.X, p.Y)).ToArray());

    static JsonArray PathsOf(IEnumerable<ModelPath> paths) => new(paths.Select(x => (JsonNode?)new JsonObject
    {
        ["modelId"] = x.ModelId.ToString(), ["route"] = Points(x.Route),
        ["footprints"] = new JsonArray(x.Footprints.Select(f => (JsonNode?)new JsonObject { ["at"] = new JsonArray(f.At.X, f.At.Y), ["dangerous"] = f.Dangerous }).ToArray()),
        ["dangerous"] = new JsonArray(x.Dangerous.Select(d => (JsonNode?)d.ToString()).ToArray()),
    }).ToArray());

    static JsonObject MarksOf(IEnumerable<ModelPath> paths) => new(MovePaths.Marks(paths).Select(kv => KeyValuePair.Create(kv.Key.ToString(), (JsonNode?)kv.Value)));

    public static JsonObject Fixture()
    {
        var cases = new JsonArray();
        foreach (var name in Logs)
        {
            var log = CallCases.Log(name);
            var player = log.Game.Participants.Where(x => x.TeamId is not null).Select(x => x.Id).Order().First();
            var moves = log.Events.Where(e => e.Payload is StepCompleted { Answer: Destinations d } && d.Moves.Any(m => m.From is OnTable && m.To is OnTable))
                .Select(e => e.Seq).Take(CutsPerLog).ToList();
            var cuts = moves.SelectMany(seq => new[] { seq, log.Events.FirstOrDefault(e => e.Seq > seq && e.Payload is ActionRecorded)?.Seq ?? seq }).Distinct().ToList();
            foreach (var seq in cuts)
            {
                var s = CallState.Playing(log, seq, player);
                var p = s.P!;
                var segments = MovePaths.Last(s.Events);
                var paths = MovePaths.KeptLater(segments, p, p.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList(), p.Board.Table.Size)();
                cases.Add(new JsonObject
                {
                    ["log"] = name, ["upTo"] = seq,
                    ["segments"] = new JsonArray(segments.Select(x => (JsonNode?)new JsonArray(x.Moves.Select(m => (JsonNode?)m.ModelId.ToString()).ToArray())).ToArray()),
                    ["paths"] = PathsOf(paths), ["marks"] = MarksOf(paths),
                    ["lines"] = new JsonArray(MovePaths.Lines(paths, id => p.Models[id].Slot.Name, v => MovePaths.VolumeName(p.Board.Table, v)).Select(l => (JsonNode?)l).ToArray()),
                });
            }
        }
        // The game "Test"'s Rush of five Flayer Cavalry past a toxic smog (MovePathsTests.FlayerRush): rider 1's base edge clips it.
        var flayer = MovePathsTests.FlayerRush.Volumes.Select(v => new Tabletop.Content.TerrainPlacement(new Guid("10" + v.Id.ToString()[2..]),
            Tabletop.Content.VisualRef.OfBuiltin(v == MovePathsTests.FlayerRush.Smog ? "toxic-smog" : "crater"), v.Transform.Position, v.Transform.Yaw, v.Transform.Scale,
            [new Tabletop.Content.TerrainVolume(v.Id, v.Volume)])).ToList();
        var rush = MovePathsTests.FlayerRush.Rush;
        var riders = rush.ModelIds.Select((id, i) => new ModelRelocation(id, new OnTable(MovePathsTests.FlayerRush.Starts[id].Placement),
            new OnTable(new Placement(new Point3(MovePathsTests.FlayerRush.Moves[i].To.X, MovePathsTests.FlayerRush.Moves[i].To.Y, 0), new Yaw(-50.78))))).ToList();
        var flayerPaths = MovePaths.Measured(new MoveSegment(rush, riders), id => MovePathsTests.FlayerRush.Starts[id].Body,
            flayer.SelectMany(t => t.WorldVolumes()).ToList(), MovePathsTests.FlayerRush.Table, []);
        var table = new Tabletop.Content.TableState(Guid.Empty, null, MovePathsTests.FlayerRush.Table, Tabletop.Content.VisualRef.OfBuiltin("desert"), flayer, [], []);
        string Rider(Guid id) => $"Flayer Cavalry {rush.ModelIds.ToList().IndexOf(id) + 1}";
        var flayerCase = new JsonObject
        {
            ["terrain"] = CallCases.J(flayer), ["size"] = CallCases.J(MovePathsTests.FlayerRush.Table), ["legal"] = CallCases.J<LegalSet>(rush),
            ["moves"] = CallCases.J(riders), ["body"] = CallCases.J(MovePathsTests.FlayerRush.Starts.Values.First().Body),
            ["names"] = new JsonObject(rush.ModelIds.Select(id => KeyValuePair.Create(id.ToString(), (JsonNode?)Rider(id)))),
            ["paths"] = PathsOf(flayerPaths), ["marks"] = MarksOf(flayerPaths),
            ["lines"] = new JsonArray(MovePaths.Lines(flayerPaths, Rider, v => MovePaths.VolumeName(table, v)).Select(l => (JsonNode?)l).ToArray()),
        };
        var outlines = new JsonArray();
        foreach (var (b, yaw) in new[] { (Base.Oval(2.362204724409449, 1.3779527559055118), 0.0), (Base.Circle(1.26), 30.0), (Base.Rect(2, 1), 90.0), (Base.Oval(3, 2), -50.78) })
            outlines.Add(new JsonObject
            {
                ["base"] = CallCases.J(b), ["at"] = new JsonArray(10.5, 7.25), ["yaw"] = yaw,
                ["outline"] = Points(MovePaths.Outline(b, new Point2(10.5, 7.25), new Yaw(yaw))),
            });
        var team = new Rgba(0.2f, 0.4f, 0.9f);
        JsonNode Colour(Rgba c) => new JsonArray(c.R, c.G, c.B, c.A);
        return new JsonObject
        {
            ["cases"] = cases, ["flayer"] = flayerCase, ["outlines"] = outlines,
            ["colours"] = new JsonObject
            {
                ["team"] = Colour(team), ["footprint"] = Colour(MovePaths.Colour(new PathFootprint(new Point2(0, 0), false), team)),
                ["dangerous"] = Colour(MovePaths.Colour(new PathFootprint(new Point2(0, 0), true), team)), ["line"] = Colour(MovePaths.Line(team)), ["lineAlpha"] = MovePaths.LineAlpha,
            },
        };
    }

    [Fact]
    public void Move_paths_fixture_is_current()
    {
        var text = Fixture().ToJsonString() + "\n";
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1") File.WriteAllText(FixturePath, text, new UTF8Encoding(false));
        Assert.True(File.Exists(FixturePath), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter MovePathsViewerFixture");
        Assert.Equal(File.ReadAllText(FixturePath), text);
    }
}
