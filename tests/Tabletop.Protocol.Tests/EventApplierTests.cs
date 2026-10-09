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
    public void A_round_started_whose_order_is_the_projections_own_list_keeps_that_order()
    {
        var a = Fresh();
        a.Apply(Ev(1, new RoundStarted(1, [Samples.Team1, Samples.Team2], [])));
        a.Apply(Ev(2, new RoundStarted(2, a.Projection.Scheduler.Order, [])));
        Assert.Equal([Samples.Team1, Samples.Team2], a.Projection.Scheduler.Order);
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
    public void Moving_a_setup_marker_puts_it_in_the_projection_at_the_new_spot_and_keeps_its_control()
    {
        var board = Samples.Board;
        var a = new EventApplier(board, Samples.Game);
        var setup = board.Table.Markers[0];
        a.ApplyAll([Ev(1, new ObjectiveControlSet(setup.Id, Samples.Team1)), Ev(2, new MarkerMoved(setup.Id, new(2, 2, 0), new(90))),
            Ev(3, new MarkerMoved(setup.Id, new(4, 4, 0), new(45)))]);
        Assert.Equal(setup with { Position = new(4, 4, 0), Yaw = new(45) }, a.Projection.Markers[setup.Id]);
        Assert.Equal(Samples.Team1, a.Projection.Control[setup.Id]);
        Assert.Equal(setup, a.Projection.Board.Table.Markers[0]);                     // the board row itself is untouched
        Assert.False(a.ProjectTo(1).Markers.ContainsKey(setup.Id));                    // replay from the board puts it back
    }

    [Fact]
    public void Moving_a_marker_nothing_holds_is_a_desync()
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        var unknown = Guid.CreateVersion7();
        var ex = Assert.Throws<ProjectionDesyncException>(() => a.Apply(Ev(1, new MarkerMoved(unknown, new(2, 2, 0), new(90)))));
        Assert.Equal("marker_moved", ex.Kind);
        Assert.Equal(unknown, ex.UnknownId);
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

    [Fact]
    public void A_change_riding_inside_a_carrier_reports_its_own_kind()
    {
        var a = Fresh(); var unknown = Guid.CreateVersion7();
        var ex = Assert.Throws<ProjectionDesyncException>(
            () => a.Apply(Ev(1, new RoundStarted(1, [Samples.Team1], [new WoundsSet(unknown, 1)]))));
        Assert.Equal("wounds_set", ex.Kind);
        Assert.Equal(unknown, ex.UnknownId);
    }

    [Fact]
    public void Popping_a_frame_that_is_not_on_the_stack_is_a_desync()
    {
        var a = Fresh();
        var ex = Assert.Throws<ProjectionDesyncException>(() => a.Apply(Ev(1, new SequenceFinished(Frame))));
        Assert.Equal("sequence_finished", ex.Kind);
        Assert.Equal(Frame, ex.UnknownId);
    }

    [Fact]
    public void Game_ended_sets_ended_and_a_round_start_clears_it()
    {
        var a = Fresh();
        var end = new GameEnded("4 rounds have been played", [Samples.Team1]);
        a.Apply(Ev(1, end));
        Assert.Equal(end, a.Projection.Ended);
        a.Apply(Ev(2, new RoundStarted(5, [Samples.Team1, Samples.Team2], [])));
        Assert.Null(a.Projection.Ended);
    }

    [Fact]
    public void Undoing_past_game_ended_clears_it()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new GameEnded("over", [])), Ev(3, new Undone(1))]);
        Assert.Null(a.Projection.Ended);
        Assert.Null(a.ProjectTo(1).Ended);
    }

    [Fact]
    public void Control_follows_its_changes_alone_or_inside_a_round_end_and_leaves_with_the_marker()
    {
        var a = Fresh();
        var objective = Samples.Board.Table.Markers.First(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
        a.Apply(Ev(1, new RoundEnded(1, [new ObjectiveControlSet(objective, Samples.Team1), new ObjectiveControlSet(Samples.Token.Id, Samples.Team2)])));
        Assert.Equal(Samples.Team1, a.Projection.Control[objective]);
        a.Apply(Ev(2, new ObjectiveControlSet(objective, null)));
        Assert.False(a.Projection.Control.ContainsKey(objective));
        a.Apply(Ev(3, new MarkerPlaced(Samples.Token)));
        a.Apply(Ev(4, new MarkerRemoved(Samples.Token.Id)));
        Assert.Empty(a.Projection.Control);
    }

    [Fact]
    public void SequenceStarted_without_outline_reads_as_empty()
    {
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))));
        Assert.Empty(a.Projection.Stack.Single().Outline);

        // A log row written before Outline existed: no "outline" key at all.
        var json = $"{{\"kind\":\"sequence_started\",\"frameId\":\"{Frame}\",\"sequence\":\"hold\",\"context\":{{\"actorUnitId\":\"{Samples.Unit}\",\"targetUnitIds\":[],\"weaponIds\":[]}}}}";
        var old = Assert.IsType<SequenceStarted>(WireJson.Deserialize<EventPayload>(json));
        Assert.Null(old.Outline);
        var b = Fresh();
        b.Apply(Ev(1, old));
        Assert.Empty(b.Projection.Stack.Single().Outline);
    }

    [Fact]
    public void StepCompleted_applied_round_trips_and_lands_on_the_completed_step()
    {
        var withApplied = new StepCompleted(Frame, "roll-to-hit", null, [Samples.Hit], [], ["Furious", "Rending"]);
        var back = Assert.IsType<StepCompleted>(WireJson.Deserialize<EventPayload>(WireJson.Serialize<EventPayload>(withApplied)));
        Assert.Equal(["Furious", "Rending"], back.Applied);
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))));
        a.Apply(Ev(2, back));
        Assert.Equal(["Furious", "Rending"], a.Projection.Stack.Single().Steps.Single().Applied);
    }

    [Fact]
    public void StepCompleted_without_applied_reads_as_null_and_an_empty_list_on_the_completed_step()
    {
        // A log row written before Applied existed: no "applied" key at all.
        var json = $"{{\"kind\":\"step_completed\",\"frameId\":\"{Frame}\",\"step\":\"roll\",\"rolls\":[],\"changes\":[]}}";
        var old = Assert.IsType<StepCompleted>(WireJson.Deserialize<EventPayload>(json));
        Assert.Null(old.Applied);
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))));
        a.Apply(Ev(2, old));
        Assert.Empty(a.Projection.Stack.Single().Steps.Single().Applied);
    }

    [Fact]
    public void A_frame_carries_its_sequences_outline_in_order()
    {
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance"),
            [new OutlineStep("move", "Move"), new OutlineStep("shoot", "Shoot")])));
        Assert.Equal(["move", "shoot"], a.Projection.Stack.Single().Outline.Select(o => o.Step));
        Assert.Equal(["Move", "Shoot"], a.Projection.Stack.Single().Outline.Select(o => o.Label));
    }
}
