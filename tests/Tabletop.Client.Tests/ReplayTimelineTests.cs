using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ReplayTimelineTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Guid Other = Guid.CreateVersion7();

    static GameEvent E(long seq, EventPayload p, Guid? actor = null) => new(seq, actor, p, null);
    static SequenceStarted Action(Guid frame) => new(frame, "activation", new SequenceContext(Samples.Unit, [], [], "Hold"));

    /// <summary>The engine starts round 1 and team 1's turn; P1 sets a score twice in one command, the engine answers with an action
    /// frame; Other sets a score; P1 undoes the second score; round 2.</summary>
    static List<GameEvent> Log()
    {
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), nested = Guid.CreateVersion7();
        return
        [
            E(1, new RoundStarted(1, [Samples.Team1], [])),                 // 0
            E(2, new TurnStarted(Samples.Team1)),                           // 1
            E(3, new ScoreSet(Samples.Team1, 1), Samples.P1),               // 2: step
            E(4, new ScoreSet(Samples.Team1, 2), Samples.P1),               // 3
            E(5, Action(a)),                                                // 4
            E(6, Action(nested)),                                           // 5: nested, not an action stop
            E(7, new SequenceFinished(a)),                                  // 6: the action ends, a stop after it
            E(8, new ScoreSet(Samples.Team1, 3), Other),                    // 7: step
            E(9, Action(b)),                                                // 8: never finishes
            E(10, new Undone(4), Samples.P1),                               // 9: step; back before the action
            E(11, new RoundStarted(2, [Samples.Team1], [])),                // 10
            E(12, new TurnStarted(Samples.Team1)),                          // 11
        ];
    }

    [Fact]
    public void Starts_at_the_board_and_seeks_forward_and_back_through_the_log_as_it_happened()
    {
        var log = Log();
        var t = new ReplayTimeline(Samples.Board, Game, log);
        Assert.Equal((0, 12, false), (t.Position, t.Count, t.AtEnd));
        Assert.Empty(t.Shown);
        Assert.Equal(0, t.Projection.Scores.GetValueOrDefault(Samples.Team1));

        var applied = new List<long>();
        var forward = t.Seek(8, e => applied.Add(e.Seq));
        Assert.Equal([1L, 2, 3, 4, 5, 6, 7, 8], forward!.Select(e => e.Seq));
        Assert.Equal(forward.Select(e => e.Seq), applied);
        Assert.Equal(3, t.Projection.Scores[Samples.Team1]);
        Assert.Equal(8, t.Shown.Count);

        Assert.Null(t.Seek(3));                                             // backward rebuilds
        Assert.Equal((3, 1), (t.Position, t.Projection.Scores[Samples.Team1]));

        t.Seek(10);                                                         // the undo shows as it happened
        Assert.Equal(2, t.Projection.Scores[Samples.Team1]);
        t.Seek(99);
        Assert.Equal((12, true, 2), (t.Position, t.AtEnd, t.Projection.Scheduler.Round));
        t.Seek(-5);
        Assert.Equal(0, t.Position);
    }

    [Fact]
    public void A_growing_log_extends_the_timeline_without_moving_the_position()
    {
        var log = Log().Take(3).ToList();
        var t = new ReplayTimeline(Samples.Board, Game, log);
        t.Seek(3);
        Assert.True(t.AtEnd);
        log.Add(E(4, new ScoreSet(Samples.Team1, 7), Samples.P1));
        Assert.Equal((3, 4, false), (t.Position, t.Count, t.AtEnd));
        Assert.Equal(4, t.Next(ReplayUnit.Step));
    }

    [Fact]
    public void Each_unit_stops_where_the_log_says()
    {
        var log = Log();
        Assert.Equal([0, 2, 7, 9, 12], ReplayTimeline.Stops(log, ReplayUnit.Step));
        Assert.Equal([0, 7, 12], ReplayTimeline.Stops(log, ReplayUnit.Action));
        Assert.Equal([0, 2, 12], ReplayTimeline.Stops(log, ReplayUnit.Turn));
        Assert.Equal([0, 1, 11, 12], ReplayTimeline.Stops(log, ReplayUnit.Round));
        Assert.Equal([0], ReplayTimeline.Stops([], ReplayUnit.Step));
    }

    [Fact]
    public void An_undo_back_before_an_open_action_lets_the_next_one_stop()
    {
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7();
        List<GameEvent> log = [E(1, new ScoreSet(Samples.Team1, 1)), E(2, Action(a)), E(3, new Undone(1), Samples.P1), E(4, Action(b)), E(5, new SequenceFinished(a)),
            E(6, new SequenceFinished(b)), E(7, new ScoreSet(Samples.Team1, 2))];
        Assert.Equal([0, 6, 7], ReplayTimeline.Stops(log, ReplayUnit.Action));
    }

    [Fact]
    public void Next_and_previous_move_between_stops_and_clamp_at_the_ends()
    {
        var t = new ReplayTimeline(Samples.Board, Game, Log());
        Assert.Equal(0, t.Previous(ReplayUnit.Step));
        Assert.Equal(2, t.Next(ReplayUnit.Step));
        t.Seek(7);
        Assert.Equal((2, 9), (t.Previous(ReplayUnit.Step), t.Next(ReplayUnit.Step)));
        Assert.Equal((0, 12), (t.Previous(ReplayUnit.Action), t.Next(ReplayUnit.Action)));
        Assert.Equal((1, 11), (t.Previous(ReplayUnit.Round), t.Next(ReplayUnit.Round)));
        t.Seek(12);
        Assert.Equal(12, t.Next(ReplayUnit.Round));
    }

    /// <summary>The owner's game 01a126db (CombatLogFixtureTests): its round_started, then one charge with the strike-back and morale.</summary>
    [Fact]
    public void A_played_logs_stops_fall_on_its_round_its_action_and_each_players_input()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game-01a126db-charge.json")));
        var log = doc.RootElement.EnumerateArray().Select(e => WireJson.Deserialize<GameEvent>(e.GetRawText())).ToList();
        var round = ReplayTimeline.Stops(log, ReplayUnit.Round);
        Assert.IsType<RoundStarted>(log[round[1] - 1].Payload);
        var actions = ReplayTimeline.Stops(log, ReplayUnit.Action);
        var charge = Assert.IsType<SequenceFinished>(log[actions[1] - 1].Payload).FrameId;   // the stop is just after the charge's frame ends
        Assert.Equal("Charge", log.Select(e => e.Payload).OfType<SequenceStarted>().Single(s => s.FrameId == charge).Context.Action);
        Assert.Equal(2, actions.Count);                                     // the board and the charge's end, which ends the log: the strike-back and morale are inside it
        var steps = ReplayTimeline.Stops(log, ReplayUnit.Step);
        Assert.All(steps.Skip(1).SkipLast(1), s => Assert.NotNull(log[s].Actor));
        Assert.True(steps.Count > 3, $"{steps.Count} steps");
    }

    [Fact]
    public void The_caption_names_the_round_and_the_position()
    {
        var t = new ReplayTimeline(Samples.Board, Game, Log());
        Assert.Equal("Before round 1 · 0 of 12", t.Caption);
        t.Seek(11);
        Assert.Equal("Round 2 · 11 of 12", t.Caption);
    }

    [Fact]
    public void The_clock_steps_once_per_interval_at_its_speed_and_not_while_paused()
    {
        var c = new ReplayClock();
        Assert.False(c.Tick(10));
        c.Play();
        Assert.False(c.Tick(1.0));
        Assert.True(c.Tick(0.5));
        Assert.False(c.Tick(0.1));
        c.Speed = 4;
        Assert.True(c.Tick(0.3));                                           // 1.5 s / 4
        c.Pause();
        Assert.False(c.Tick(5));
    }
}
