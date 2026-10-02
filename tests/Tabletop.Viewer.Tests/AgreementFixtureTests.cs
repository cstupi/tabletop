using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Viewer.Tests;

public class AgreementFixtureTests
{
    public static TheoryData<string> Names() => new(FixtureWriter.FixtureNames);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_fixture_exists_round_trips_and_its_events_reproduce_its_frames(string name)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var scenario = ScenarioCatalog.Get(name) with { RoundCap = 2 };
            await FixtureWriter.WriteAsync(
                await FixtureWriter.RunAsync(scenario, CancellationToken.None),
                RepoPaths.Fixtures, CancellationToken.None);
        }

        var path = Path.Combine(RepoPaths.Fixtures, $"{name}.recording.json");
        Assert.True(File.Exists(path), $"{path} is missing; run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests");
        var recording = await RecordingJson.ReadAsync(path, CancellationToken.None);
        Assert.Equal(RecordingJson.SchemaVersion, recording.Version);
        Assert.NotEmpty(recording.Frames);
        Assert.NotEmpty(recording.Board.Models);
        Assert.Equal(RecordingJson.Serialize(recording),
            RecordingJson.Serialize(RecordingJson.Deserialize(RecordingJson.Serialize(recording))));

        // Every frame index is its position, and every frame names only board models — what read.ts enforces.
        var ids = recording.Board.Models.Select(m => m.Id).ToHashSet();
        for (var i = 0; i < recording.Frames.Count; i++)
        {
            Assert.Equal(i, recording.Frames[i].Index);
            Assert.All(recording.Frames[i].Models, m => Assert.Contains(m.Id, ids));
        }
    }

    [Fact]
    public void Both_fixtures_carry_the_four_files_the_viewer_reads()
    {
        foreach (var name in FixtureWriter.FixtureNames)
            foreach (var suffix in new[] { "recording", "welcome", "board", "events" })
                Assert.True(File.Exists(Path.Combine(RepoPaths.Fixtures, $"{name}.{suffix}.json")),
                    $"{name}.{suffix}.json is missing");
    }

    /// <summary>
    /// The C# half of one pair. The frame comparison proves the two appliers agree on a valid log; it
    /// cannot prove they agree on an invalid one, which is where equivalence is easiest to lose. C#
    /// throws <c>ProjectionDesyncException</c> and the TypeScript port
    /// throws <c>ProjectionDesyncError</c> on this same event — `viewer/tests/agreement.test.ts`,
    /// *the two appliers fault the same way*. One side faulting while the other returned undefined
    /// would not be equivalence, however many frames matched.
    /// </summary>
    [Fact]
    public void The_csharp_applier_faults_on_an_id_the_projection_does_not_hold()
    {
        var welcome = Assert.IsType<Welcome>(WireJson.Deserialize<SocketMessage>(
            File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "skirmish.welcome.json"))));
        var board = Assert.IsType<BoardMessage>(WireJson.Deserialize<SocketMessage>(
            File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "skirmish.board.json"))));
        var applier = new EventApplier(board.Board, welcome.Game);

        // A marker id nothing placed: the case ruling 2 names, a marker from setup rather than from an event.
        var unknown = Guid.CreateVersion7();
        Assert.Throws<ProjectionDesyncException>(
            () => applier.Apply(new GameEvent(1, null, new MarkerMoved(unknown, new Point3(5, 5, 0), new Yaw(90)), null)));

        // A store C# removes from rather than reads through does not fault, so the port must not either.
        applier.Apply(new GameEvent(2, null, new MarkerRemoved(unknown), null));
    }
}
