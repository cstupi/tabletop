namespace Tabletop.Protocol.Tests;

public class EventApplierTests
{
    static EventApplier Fresh() => new(Samples.Board, Samples.Game);
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);
    static readonly Guid Ask = Samples.AskId, Frame = Samples.Frame, Roll = Samples.Roll;

    [Fact]
    public void Deployment_moves_a_model_from_shelf_to_table()
    {
        var a = Fresh();
        a.Apply(Ev(1, new ModelsMoved([Samples.Deploy])));
        Assert.Equal(Samples.Deploy.To, a.Projection.Models[Samples.Model].Location);
        Assert.Equal(1, a.Projection.LastSeq);
    }

    [Fact]
    public void Stores_copy_what_events_say()
    {
        var a = Fresh();
        a.ApplyAll([
            Ev(1, new WoundsSet(Samples.Model, 2)), Ev(2, new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true)),
            Ev(3, new ActionRecorded(Samples.Unit, "Advance")), Ev(4, new GrantAdded(Samples.Unit, Samples.Grant)),
            Ev(5, new SpellTokensSet(Samples.Model, 3)), Ev(6, new WoundsThisActivationSet(Samples.Unit, 2)), Ev(7, new ScoreSet(Samples.Team1, 4)),
            Ev(8, new MarkerPlaced(Samples.Token)), Ev(9, new MarkerMoved(Samples.Token.Id, new(5, 5, 0), new(90))),
        ]);
        var u = a.Projection.Units[Samples.Unit]; var m = a.Projection.Models[Samples.Model];
        Assert.Equal((2, 3), (m.Wounds, m.SpellTokens));
        Assert.Contains("shaken", u.Conditions); Assert.Contains("Advance", u.Actions);
        Assert.Single(u.Grants); Assert.Equal(2, u.WoundsThisActivation); Assert.Equal(4, a.Projection.Scores[Samples.Team1]);
        Assert.Equal(5, a.Projection.Markers[Samples.Token.Id].Position.X);
        a.ApplyAll([Ev(10, new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", false)), Ev(11, new ActionRecordCleared(Samples.Unit)),
            Ev(12, new GrantRemoved(Samples.Unit, Samples.Grant.Id)), Ev(13, new MarkerRemoved(Samples.Token.Id))]);
        Assert.Empty(u.Conditions); Assert.Empty(u.Actions); Assert.Empty(u.Grants); Assert.Empty(a.Projection.Markers);
    }

    [Fact]
    public void Scheduler_position_follows_the_four_events_and_the_pass_stands()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new RoundStarted(1, [Samples.Team1, Samples.Team2], [new ActionRecorded(Samples.Unit, "x")])),
            Ev(2, new PhaseStarted("rounds")), Ev(3, new TurnStarted(Samples.Team1)), Ev(4, new TurnEnded(Samples.Team1, true, [new ActionRecordCleared(Samples.Unit)]))]);
        var s = a.Projection.Scheduler;
        Assert.Equal((1, "rounds"), (s.Round, s.Phase));
        Assert.Equal([Samples.Team1, Samples.Team2], s.Order);
        Assert.Null(s.ActiveTeamId); Assert.Contains(Samples.Team1, s.Passed);
        Assert.Empty(a.Projection.Units[Samples.Unit].Actions);
        a.Apply(Ev(5, new RoundStarted(2, [Samples.Team2, Samples.Team1], [])));
        Assert.Empty(a.Projection.Scheduler.Passed);
    }

    [Fact]
    public void Frames_push_record_steps_and_pop_with_everything_above()
    {
        var a = Fresh(); var inner = Guid.CreateVersion7();
        a.ApplyAll([Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))),
            Ev(2, new ModifierAdded(Frame, "hit", new RollModifier("Clay", 1))),
            Ev(3, new StepCompleted(Frame, "roll-to-hit", null, [Samples.Hit], [new WoundsSet(Samples.Model, 1)]) { Values = new Dictionary<string, int> { ["hits"] = 1 } }),
            Ev(4, new SequenceStarted(inner, "resolve-hits", new SequenceContext(Samples.Unit, [], [], null)))]);
        Assert.Equal(2, a.Projection.Stack.Count);
        Assert.Single(a.Projection.Stack[0].Steps); Assert.Empty(a.Projection.Stack[0].PendingModifiers);
        Assert.Equal(1, a.Projection.Stack[0].Steps[0].Values!["hits"]);                       // copied, never derived
        Assert.Equal(1, a.Projection.Models[Samples.Model].Wounds);
        a.Apply(Ev(5, new DiceRerolled(Frame, Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))])));
        Assert.Equal(5, a.Projection.Stack[0].Steps[0].Rolls[0].Dice[1].Raw);
        a.Apply(Ev(6, new SequenceAbandoned(Frame)));
        Assert.Empty(a.Projection.Stack);
    }

    [Fact]
    public void Asks_open_collect_answers_and_close()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new AskOpened(Samples.Ask)), Ev(2, new AskAnswered(Ask, Samples.Team1, null, false))]);
        Assert.True(a.Projection.OpenAsks[Ask].Answers.ContainsKey(Samples.Team1));
        a.Apply(Ev(3, new AskClosed(Ask, [new TeamAnswer(Samples.Team1, new Passed())])));
        Assert.Empty(a.Projection.OpenAsks);
    }

    [Fact]
    public void Undo_drops_the_range_and_the_log_stays_append_only()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new ScoreSet(Samples.Team1, 2)), Ev(3, new ScoreSet(Samples.Team1, 3)),
            Ev(4, new Undone(1)), Ev(5, new ScoreSet(Samples.Team2, 9))]);
        Assert.Equal((1, 9), (a.Projection.Scores[Samples.Team1], a.Projection.Scores[Samples.Team2]));
        Assert.Equal(5, a.Log.Count);
        Assert.Equal([1L, 5L], EventApplier.EffectiveLog(a.Log).Select(e => e.Seq));
        Assert.Equal(1, a.ProjectTo(1).Scores[Samples.Team1]);
        Assert.Equal(0, a.ProjectTo(0).Scores[Samples.Team1]);
        a.Apply(Ev(6, new Undone(0)));
        Assert.Equal(0, a.Projection.Scores[Samples.Team2]);
    }

    [Fact]
    public void Moving_a_marker_placed_during_setup_is_a_desync()
    {
        var board = Samples.Board;                                                  // its markers are the draft's, not the log's
        var a = new EventApplier(board, Samples.Game);
        var setupMarker = board.Table.Markers[0].Id;
        var ex = Assert.Throws<ProjectionDesyncException>(
            () => a.Apply(Ev(1, new MarkerMoved(setupMarker, new(2, 2, 0), new(90)))));
        Assert.Equal("marker_moved", ex.Kind);
        Assert.Equal(setupMarker, ex.UnknownId);
    }

    [Fact]
    public void Editing_the_dice_of_a_popped_frame_is_a_desync()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new SequenceStarted(Frame, "roll-off", new SequenceContext(null, [], [], null))),
            Ev(2, new StepCompleted(Frame, "roll", null, [Samples.Hit], [])),
            Ev(3, new SequenceFinished(Frame))]);
        var ex = Assert.Throws<ProjectionDesyncException>(
            () => a.Apply(Ev(4, new DiceEdited(Frame, Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]))));
        Assert.Equal("dice_edited", ex.Kind);
        Assert.Equal(Frame, ex.UnknownId);
    }
}
