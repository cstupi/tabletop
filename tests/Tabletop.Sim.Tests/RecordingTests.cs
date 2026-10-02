using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class RecordingTests
{
    static CompiledScenario Scenario() => ScenarioCompiler.Compile(ScenarioCatalog.Get("terrain"));

    [Fact]
    public void The_board_carries_the_table_the_terrain_the_zones_and_a_definition_per_model()
    {
        var projector = new FrameProjector(Scenario());
        var board = projector.Board;
        Assert.Equal(72, board.Width);
        Assert.Equal(48, board.Length);
        Assert.Equal(2, board.Terrain.Count);
        Assert.Contains("blocks_line_of_sight", board.Terrain[0].Volumes[0].Declarations);
        Assert.Equal(18, board.Models.Count);
        Assert.Equal(2, board.Models.Select(m => m.TeamId).Distinct().Count());
        Assert.All(board.Models, m => Assert.True(m.BaseA > 0));

        // The zones carry resolved rings, not just a shape id: resolving `frontline/1` is C# and the
        // twelve-inch inset is absolute, so a browser cannot recover it from the id and the table size.
        Assert.Equal(2, board.Zones.Count);
        Assert.Equal("frontline/1", board.Zones[0].Shape);
        Assert.Equal(1, board.Zones[0].TeamOrdinal);
        var ring = Assert.Single(board.Zones[0].Polygons);
        Assert.Equal(4, ring.Count);
        Assert.All(ring, p => Assert.InRange(p.X, 0, board.Width));
        Assert.All(ring, p => Assert.InRange(p.Y, 0, board.Length));
        Assert.Equal(12, ring.Max(p => p.Y) - ring.Min(p => p.Y));
    }

    [Fact]
    public void A_frame_carries_every_model_the_open_ask_and_the_log_line()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var unit = c.Board.Armies[0].Snapshot.Units[0];
        var moved = new ModelsMoved([new ModelRelocation(unit.Models[0].Id, new OnShelf(),
            new OnTable(new Tabletop.Geometry.Placement(new Tabletop.Geometry.Point3(6, 6, 0), new Tabletop.Geometry.Yaw(0))))]);
        var frame = projector.Add(new GameEvent(1, null, moved, null));

        Assert.Equal(0, frame.Index);
        Assert.Equal(1, frame.Seq);
        Assert.Equal("models_moved", frame.Kind);
        Assert.Equal(18, frame.Models.Count);
        var placed = frame.Models.Single(m => m.Id == unit.Models[0].Id);
        Assert.Equal("table", placed.Where);
        Assert.Equal(6, placed.X);
        Assert.Empty(frame.OpenAsks);
        Assert.Empty(frame.Passed);
        Assert.Equal(0, frame.Scores.Values.Sum());
        Assert.Contains("moved", frame.Line);
    }

    [Fact]
    public void A_frame_carries_the_score_and_who_has_passed()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new RoundStarted(1, c.TeamIds, []), null));
        projector.Add(new GameEvent(2, null, new TurnStarted(c.TeamIds[0]), null));
        var scored = projector.Add(new GameEvent(3, null, new ScoreSet(c.TeamIds[0], 3), null));
        Assert.Equal(3, scored.Scores[c.TeamIds[0]]);
        Assert.Empty(scored.Passed);

        var passed = projector.Add(new GameEvent(4, null, new TurnEnded(c.TeamIds[0], true, []), null));
        Assert.Equal(c.TeamIds[0], Assert.Single(passed.Passed));
        Assert.Equal(3, passed.Scores[c.TeamIds[0]]);   // whole state, not a delta
    }

    [Fact]
    public void A_marker_placed_during_play_carries_its_own_definition()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var marker = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Tabletop.Geometry.Point3(24, 12, 0),
            new Tabletop.Geometry.Yaw(0), 3, "Objective A", VisualRef.OfBuiltin("objective-ring"));
        var frame = projector.Add(new GameEvent(1, null, new MarkerPlaced(marker), null));

        // The board block knows only the setup markers, so the frame is what tells a viewer how to draw this one.
        Assert.DoesNotContain(marker.Id, projector.Board.Markers.Select(m => m.Id));
        var state = Assert.Single(frame.Markers);
        Assert.Equal(marker.Id, state.Id);
        Assert.Equal("objective", state.Kind);
        Assert.Equal(3, state.Diameter);
        Assert.Equal("Objective A", state.Label);
        Assert.Equal(24, state.X);
    }

    [Fact]
    public void An_open_ask_appears_on_every_frame_until_it_closes()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [c.TeamIds[0]],
            new Options([new AskOption("x", "X", [])]), true, false, null, null);
        Assert.Single(projector.Add(new GameEvent(1, null, new AskOpened(ask), null)).OpenAsks);
        var second = projector.Add(new GameEvent(2, null, new PhaseStarted("rounds"), null));
        Assert.Equal(ask.Id, Assert.Single(second.OpenAsks).Id);
        Assert.Equal("options", second.OpenAsks[0].LegalKind);
        Assert.Empty(projector.Add(new GameEvent(3, null, new AskClosed(ask.Id, []), null)).OpenAsks);
    }

    [Fact]
    public void The_dice_of_a_step_are_recorded_raw_modified_and_judged()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var frameId = Guid.CreateVersion7();
        projector.Add(new GameEvent(1, null, new SequenceStarted(frameId, "attack", new SequenceContext(null, [], [], null)), null));
        var roll = new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier("Good Shot", 1)],
            [new Die(3, 4, true), new Die(1, 2, false)]);
        var frame = projector.Add(new GameEvent(2, null,
            new StepCompleted(frameId, "hit-roll", null, [roll], []), null));
        var recorded = Assert.Single(frame.Rolls);
        Assert.Equal([3, 1], recorded.Raw);
        Assert.Equal([4, 2], recorded.Modified);
        Assert.Equal([true, false], recorded.Success);
        Assert.Equal(1, recorded.Net);
        Assert.Equal(4, recorded.Threshold);
    }

    [Fact]
    public void A_step_with_two_pools_records_both_and_the_line_names_each_threshold()
    {
        // A ruleset splits one roll wherever a step's dice do not share a threshold: OPR's roll-to-hit takes one pool per Quality
        // value the attacking models bring, since p.4 tests each attack against "the model's Quality value" and a joined hero
        // brings its own. The recording carries both, and the line has to say which is which.
        var projector = new FrameProjector(Scenario());
        var frameId = Guid.CreateVersion7();
        projector.Add(new GameEvent(1, null, new SequenceStarted(frameId, "attack", new SequenceContext(null, [], [], null)), null));
        var captain = new RollRecord(Guid.CreateVersion7(), "hit", 3, [], [new Die(4, 4, true)]);
        var squad = new RollRecord(Guid.CreateVersion7(), "hit", 5, [], [new Die(4, 4, false)]);
        var frame = projector.Add(new GameEvent(2, null,
            new StepCompleted(frameId, "roll-to-hit", null, [captain, squad], []), null));
        Assert.Equal([3, 5], frame.Rolls.Select(r => r.Threshold));
        Assert.Equal("roll-to-hit: hit 3+ 4!; hit 5+ 4", frame.Line);
    }

    [Fact]
    public async Task A_recording_round_trips_through_json()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new PhaseStarted("deployment"), null));
        projector.Add(new GameEvent(2, null, new RoundStarted(1, c.TeamIds, []), null));
        var meta = new RecordingMeta("terrain", 1, "in-process", "grimdark-future", c.Properties.ContentHash,
            "alternating-activation", "strict", 8, [], "round_limit", null, null, 1, 2, DateTimeOffset.UnixEpoch, 12.5);
        var recording = projector.Build(meta);

        var path = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}.json");
        try
        {
            await RecordingJson.WriteAsync(recording, path, CancellationToken.None);
            var read = await RecordingJson.ReadAsync(path, CancellationToken.None);
            Assert.Equal(RecordingJson.SchemaVersion, read.Version);
            Assert.Equal(RecordingJson.Serialize(recording), RecordingJson.Serialize(read));
            Assert.Equal(2, read.Frames.Count);
            Assert.Equal("terrain", read.Meta.Scenario);
            Assert.Contains("\"schemaVersion\"", await File.ReadAllTextAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_json_is_camel_case_and_omits_nulls()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new PhaseStarted("deployment"), null));
        var json = RecordingJson.Serialize(projector.Build(new RecordingMeta("terrain", 1, "in-process",
            "grimdark-future", "sha256:x", "alternating-activation", "strict", null, [], "stuck", "no ask, no events",
            null, 0, 1, DateTimeOffset.UnixEpoch, 1)));
        Assert.Contains("\"frames\"", json);
        Assert.DoesNotContain("\"Frames\"", json);
        Assert.Contains("\"polygons\"", json);            // the resolved zone rings, in the board block
        Assert.Contains("\"scores\"", json);              // empty, but present: a frame is whole state
        Assert.Contains("\"passed\"", json);
        Assert.DoesNotContain("roundCap", json);          // null, so omitted
        Assert.DoesNotContain("activeTeamId", json);      // no active team yet, so omitted
    }

    [Fact]
    public void An_undone_event_is_tolerated_on_a_projector_that_saw_nothing_before()
    {
        var projector = new FrameProjector(Scenario());
        var frame = projector.Add(new GameEvent(9, null, new Undone(5), null));
        Assert.Equal("undone", frame.Kind);
        Assert.Equal(18, frame.Models.Count);
    }
}
