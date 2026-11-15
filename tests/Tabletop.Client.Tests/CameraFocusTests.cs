using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class CameraFocusTests
{
    static readonly TableSize Size = new(72, 48);
    static void Near(Point3 e, Point3 a) { Assert.Equal(e.X, a.X, 9); Assert.Equal(e.Y, a.Y, 9); Assert.Equal(e.Z, a.Z, 9); }
    static void Near(Point2 e, Point2 a) => Near(new Point3(e.X, e.Y, 0), new Point3(a.X, a.Y, 0));

    /// <summary>Playing both sides: the camera goes to the acting team's units, or its deployment zone while they are all on the shelf.</summary>
    [Fact]
    public void A_teams_side_is_its_units_on_the_table_else_the_middle_of_its_zone()
    {
        var game = Tabletop.Protocol.Tests.Samples.Game; var applier = new EventApplier(Tabletop.Protocol.Tests.Samples.Board, game);
        Near(new(36, 42, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team1)!.Value);   // frontline/1: the far long edge
        Near(new(36, 6, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team2)!.Value);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([Tabletop.Protocol.Tests.Samples.Deploy]), null));
        Near(new(12, 12, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team1)!.Value);
    }

    /// <summary>The zone's middle under the legs the server sent: side-battle/1 is (0,0), (48,0), (0,36) under 3.6.0 p.21, (0,0), (54,0), (0,30) by default.</summary>
    [Fact]
    public void A_teams_side_in_side_battle_follows_the_welcomes_legs()
    {
        var game = Tabletop.Protocol.Tests.Samples.Game; var board = Tabletop.Protocol.Tests.Samples.Board;
        board = board with { Table = board.Table with { Zones = board.Table.Zones.Select(z => z with { Shape = $"side-battle/{z.TeamOrdinal}" }).ToList() } };
        var p = new EventApplier(board, game).Projection;
        Near(new(18, 10, 0), CameraFocus.TeamSide(p, game, Tabletop.Protocol.Tests.Samples.Team1)!.Value);
        Near(new(16, 12, 0), CameraFocus.TeamSide(p, game, Tabletop.Protocol.Tests.Samples.Team1, new ZoneLegs(2.0 / 3, 0.75))!.Value);
    }

    [Fact]
    public void Pan_moves_along_and_across_the_heading_at_a_speed_set_by_distance_and_stops_12_inches_past_the_table()
    {
        var north = new Point2(0, 1);
        Near(new(36, 32), CameraFocus.Pan(new(36, 24), north, 1, 0, 10, 1, Size));        // 10" away: 8"/s
        Near(new(44, 24), CameraFocus.Pan(new(36, 24), north, 0, 1, 10, 1, Size));        // D: across, to the right
        Near(new(84, 60), CameraFocus.Pan(new(80, 58), north, 1, 1, 100, 1, Size));
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void Steer_is_silent_while_typing_and_q_e_turn_the_held_unit()
    {
        var held = new HashSet<Intent> { Intent.CameraForward, Intent.CameraLeft, Intent.TurnLeft };
        Assert.Equal((1, -1, -1), CameraFocus.Steer(held, holding: false, typing: false));
        Assert.Equal((1, -1, 0), CameraFocus.Steer(held, holding: true, typing: false));
        Assert.Equal((0, 0, 0), CameraFocus.Steer(held, holding: false, typing: true));
    }

    [Fact]
    public void Held_q_e_turn_what_is_in_hand_at_a_steady_rate()
    {
        Assert.Equal(CameraFocus.TurnDegreesPerSecond * 0.5, CameraFocus.HeldTurn(new HashSet<Intent> { Intent.TurnLeft }, holding: true, typing: false, fine: false, 0.5), 9);
        Assert.Equal(-CameraFocus.FineTurnDegreesPerSecond * 0.5, CameraFocus.HeldTurn(new HashSet<Intent> { Intent.TurnRight }, holding: true, typing: false, fine: true, 0.5), 9);
        Assert.Equal(0, CameraFocus.HeldTurn(new HashSet<Intent> { Intent.TurnLeft, Intent.TurnRight }, holding: true, typing: false, fine: false, 0.5));
        Assert.Equal(0, CameraFocus.HeldTurn(new HashSet<Intent> { Intent.TurnLeft }, holding: false, typing: false, fine: false, 0.5));
        Assert.Equal(0, CameraFocus.HeldTurn(new HashSet<Intent> { Intent.TurnLeft }, holding: true, typing: true, fine: false, 0.5));
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public void Follow_waits_for_a_quiet_camera_and_an_empty_hand()
    {
        Assert.True(CameraFocus.Follows(true, 2.0, false)); Assert.False(CameraFocus.Follows(true, 1.9, false));
        Assert.False(CameraFocus.Follows(true, 10, true)); Assert.False(CameraFocus.Follows(false, 10, false));
    }

    [Fact]
    public void A_glide_eases_over_three_tenths_of_a_second_to_the_actor_of_the_last_action_frame()
    {
        Point3 a = new(0, 0, 0), b = new(10, 20, 2);
        Near(new(5, 10, 1), CameraFocus.Glide(a, b, 0.15)); Near(b, CameraFocus.Glide(a, b, 0.3)); Near(b, CameraFocus.Glide(a, b, 5));
        var p = TableUx.After(TableUx.Deploy);
        SequenceStarted Start(Guid unit, string? action) => new(Guid.CreateVersion7(), "s", new SequenceContext(unit, [], [], action));
        Near(new(12, 10, 0), CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Squad.Id, "Advance"), Start(TableUx.Orcs.Id, null)), p)!.Value);
        Assert.Null(CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Reserve.Id, "Advance")), p));   // nothing on the table
    }
}

/// <summary>The recorded 3.6 charge into spears (viewer/tests/fixtures/log/spears.*): two charges, each with Impact, a melee, wounds to
/// allocate, strike back, a morale test and consolidation, the Asks passing between the teams. Played one event a batch, as the follow
/// sees them, by a client holding both teams (Seats.Acting over both).</summary>
public class CameraFollowMeleeTests
{
    sealed record Move(long Seq, string Kind, Point3 At);
    sealed record Played(List<Move> Moves, List<(long Seq, Guid? Acting)> Acting, Dictionary<long, Point3> MeleeMiddle, IReadOnlyList<GameEvent> Log);

    static Played Play()
    {
        var dir = Path.Combine(Root(), "viewer", "tests", "fixtures", "log");
        var game = ((Welcome)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, "spears.welcome.json")))).Game;
        var board = ((BoardMessage)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, "spears.board.json")))).Board;
        var events = ((EventsMessage)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, "spears.events.json")))).Events;
        var mine = game.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList();
        var applier = new EventApplier(board, game); Guid? acting = null;
        var played = new Played([], [], [], applier.Log);
        foreach (var e in events)
        {
            applier.Apply(e); var p = applier.Projection; GameEvent[] batch = [e];
            if (e.Payload is SequenceStarted { Sequence: "melee", Context: { ActorUnitId: { } actor } c })
            {
                var at = p.Models.Values.Where(m => m.UnitId == actor || c.TargetUnitIds.Contains(m.UnitId)).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
                played.MeleeMiddle[e.Seq] = new(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z));
            }
            if (CameraFocus.FollowTarget(batch, p) is { } to) played.Moves.Add(new(e.Seq, "follow", to));
            var now = Seats.Acting(p, mine, applier.Log);
            if (CameraFocus.SideOnSwitch(p, game, acting, now) is { } side) played.Moves.Add(new(e.Seq, "side", side));
            played.Acting.Add((e.Seq, now)); acting = now;
        }
        return played;
    }

    static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) return dir.FullName;
        throw new InvalidOperationException("Tabletop.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Each melee: from its frame's start to the end of the charge, the camera moves once, at the start, to the middle of the
    /// charging and charged units. The acting team changes several times in that span (the camera used to go to that team's whole army on
    /// each change) and the morale test is an action frame of the defender (the camera used to glide to it).</summary>
    [Fact]
    public void A_melee_is_framed_once_at_its_start_and_the_camera_stays_through_its_asks_and_morale()
    {
        var run = Play();
        Assert.Equal(2, run.MeleeMiddle.Count);
        foreach (var (start, middle) in run.MeleeMiddle)
        {
            var charge = run.Log.Last(e => e.Seq < start && e.Payload is SequenceStarted { Sequence: "charge" });
            var end = run.Log.First(e => e.Payload is SequenceFinished f && f.FrameId == ((SequenceStarted)charge.Payload).FrameId).Seq;
            Assert.True(run.Acting.Where(a => a.Seq > start && a.Seq < end).Select(a => a.Acting).Distinct().Count() > 1);
            Assert.Contains(run.Log, e => e.Seq > start && e.Seq < end && e.Payload is SequenceStarted { Sequence: "morale-test", Context.Action: not null });

            var moves = run.Moves.Where(m => m.Seq >= start && m.Seq <= end).ToList();
            var only = Assert.Single(moves);
            Assert.Equal((start, "follow"), (only.Seq, only.Kind));
            Assert.Equal(middle, only.At);
        }
    }

    /// <summary>Between activations the camera still follows: each activation's own frame glides to its actor, and a change of acting
    /// team with no frame open goes to that team's side.</summary>
    [Fact]
    public void Activations_and_turns_still_move_the_camera()
    {
        var run = Play();
        foreach (var e in run.Log.Where(e => e.Payload is SequenceStarted { Sequence: "charge" or "hold" }))
            Assert.Contains(run.Moves, m => m.Seq == e.Seq && m.Kind == "follow");
        Assert.Contains(run.Moves, m => m.Kind == "side");
    }
}
