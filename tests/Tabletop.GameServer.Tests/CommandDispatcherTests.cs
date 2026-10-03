using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

public class CommandDispatcherTests
{
    static readonly Actor Clay = new(Samples.P1, Samples.Team1), Opponent = new(Guid.CreateVersion7(), Samples.Team2), Watcher = new(Guid.CreateVersion7(), null);
    static GameState State(Enforcement e = Enforcement.Warn, params EventPayload[] history)
    {
        var info = Samples.Game with { Enforcement = e };
        var board = Samples.Board;   // a property that builds a fresh instance per access: take one and use it twice
        var applier = new EventApplier(board, info);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(info, board, applier, GrimdarkFutureRules.Instance);
    }
    static CommandDispatcher Dispatcher() => new(new InertEngine());
    static readonly string ActivateId = Samples.Unit.ToString("N") + "/Advance";
    static readonly Ask Activate = new(Samples.AskId, AskQuestions.Activate, "Activate", [Samples.Team1], new Options([new AskOption(ActivateId, "Advance", [Samples.Unit])]), true, false, null, null);
    /// <summary>Two model ids out of Samples, so the spend map is concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static readonly Ask SimultaneousSpend = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame);

    public static IEnumerable<object[]> Overrides() =>
        new Command[] { new MoveModels([Samples.Deploy]), new SetWounds(Samples.Model, 2), new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true),
            new RecordAction(Samples.Unit, "Advance"), new ClearActionRecord(Samples.Unit), new AddGrant(Samples.Unit, Samples.Grant), new RemoveGrant(Samples.Unit, Samples.Grant.Id),
            new SetSpellTokens(Samples.Model, 3), new SetWoundsThisActivation(Samples.Unit, 1), new SetScore(Samples.Team1, 5), new SetActiveTeam(Samples.Team2), new SetRound(2),
            new PlaceMarker(Samples.Token) }
        .SelectMany(c => new[] { Enforcement.Silent, Enforcement.Warn, Enforcement.Strict }.Select(e => new object[] { c, e }));

    [Theory, MemberData(nameof(Overrides))]
    public void Menu_edits_and_hand_moves_are_applied_under_every_setting_with_no_warning(Command command, Enforcement enforcement)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement), command, Watcher);
        var applied = Assert.IsType<Applied>(outcome);
        Assert.Single(applied.Events); Assert.Null(applied.Warning);
        var expectedKind = command switch { MoveModels => typeof(ModelsMoved), SetActiveTeam => typeof(TurnStarted), SetRound => typeof(RoundStarted), PlaceMarker => typeof(MarkerPlaced), _ => null };
        if (expectedKind is not null) Assert.IsType(expectedKind, applied.Events[0]);
        else Assert.IsAssignableFrom<StateChange>(applied.Events[0]);
    }

    [Fact]
    public void A_reference_to_nothing_is_refused_as_unknown_regardless_of_setting()
    {
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(Enforcement.Silent), new SetWounds(Guid.CreateVersion7(), 1), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(), new MoveMarker(Guid.CreateVersion7(), new(0, 0, 0), new(0)), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(), new AbandonSequence(Guid.CreateVersion7()), Clay)).Reason);
    }

    [Fact]
    public void Undo_is_accepted_for_zero_or_a_live_seq_only()
    {
        var state = State(Enforcement.Strict, new ScoreSet(Samples.Team1, 1), new ScoreSet(Samples.Team1, 2), new Undone(1));
        Assert.IsType<Undone>(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new Undo(1), Watcher)).Events[0]);
        Assert.IsType<Applied>(Dispatcher().Dispatch(state, new Undo(0), Watcher));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new Undo(2), Watcher)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new Undo(9), Watcher)).Reason);
    }

    [Theory]
    [InlineData(Enforcement.Strict, false, false)] [InlineData(Enforcement.Warn, true, true)] [InlineData(Enforcement.Silent, true, false)]
    public void An_out_of_set_answer_follows_the_enforcement_setting(Enforcement enforcement, bool applied, bool warned)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Chosen("zzz")), Clay);
        if (!applied) { Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(outcome).Reason); return; }
        var ok = Assert.IsType<Applied>(outcome);
        Assert.Equal(warned, ok.Warning is not null);
        Assert.True(Assert.IsType<AskAnswered>(ok.Events[0]).OutsideLegalSet);
        Assert.IsType<AskClosed>(ok.Events[1]);
    }

    static readonly Ask Unreadable = Activate with { Legal = new Options([new AskOption("not-an-option", "Unreadable", [Samples.Unit])]) };
    static readonly Ask UnreadableFramed = Unreadable with { FrameId = Samples.Frame };

    [Theory]
    [InlineData(Enforcement.Strict, false, false)] [InlineData(Enforcement.Warn, true, true)] [InlineData(Enforcement.Silent, true, false)]
    public void An_in_set_turn_answer_the_ruleset_cannot_read_follows_the_enforcement_setting(Enforcement enforcement, bool applied, bool warned)
    {
        var state = State(enforcement, new AskOpened(Unreadable));
        var outcome = Dispatcher().Dispatch(state, new AnswerAsk(Unreadable.Id, new Chosen("not-an-option")), Clay);
        if (!applied)
        {
            Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(outcome).Reason);
            Assert.Contains(Unreadable.Id, state.Projection.OpenAsks.Keys);
            return;
        }
        var ok = Assert.IsType<Applied>(outcome);
        Assert.Equal(warned, ok.Warning is not null);
        Assert.True(Assert.IsType<AskAnswered>(ok.Events[0]).OutsideLegalSet);
        Assert.IsType<AskClosed>(ok.Events[1]);
    }

    [Fact]
    public void A_pass_is_never_probed_and_a_non_turn_ask_is_not_probed()
    {
        Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(Unreadable)), new AnswerAsk(Unreadable.Id, new Passed()), Clay));
        var framed = Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(UnreadableFramed)), new AnswerAsk(UnreadableFramed.Id, new Chosen("not-an-option")), Clay));
        Assert.False(Assert.IsType<AskAnswered>(framed.Events[0]).OutsideLegalSet);
    }

    [Fact]
    public void An_in_set_answer_answers_and_closes_an_unsealed_ask()
    {
        var ok = Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Chosen(ActivateId)), Clay));
        Assert.Equal([typeof(AskAnswered), typeof(AskClosed)], ok.Events.Select(e => e.GetType()));
        Assert.Equal(Samples.Team1, ((AskClosed)ok.Events[1]).Answers.Single().TeamId);
        Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Passed()), Clay));
    }

    [Fact]
    public void Only_an_addressed_team_may_answer_and_an_unknown_ask_is_unknown()
    {
        var state = State(Enforcement.Silent, new AskOpened(Activate));
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Activate.Id, new Chosen(ActivateId)), Opponent)).Reason);
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Activate.Id, new Chosen(ActivateId)), Watcher)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Guid.CreateVersion7(), new Chosen(ActivateId)), Clay)).Reason);
    }

    [Fact]
    public void A_sealed_ask_collects_every_team_before_it_closes()
    {
        var state = State(Enforcement.Strict, new AskOpened(SimultaneousSpend));
        var first = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam1), Clay));
        Assert.Single(first.Events); Assert.IsType<AskAnswered>(first.Events[0]);
        state.Applier.Apply(new GameEvent(2, Clay.ParticipantId, first.Events[0], null));
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2), Clay)).Reason);
        var second = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2), Opponent));
        var closed = Assert.IsType<AskClosed>(second.Events[1]);
        Assert.Equal([Samples.Team1, Samples.Team2], closed.Answers.Select(a => a.TeamId));
        Assert.Equal([2, 1], closed.Answers.Select(a => ((TokensSpent)a.Answer).Counts.Values.Single()));
    }

    [Fact]
    public void Abandoning_closes_the_popped_frames_asks_first()
    {
        var inner = Guid.CreateVersion7();
        var innerAsk = SimultaneousSpend with { Id = Guid.CreateVersion7(), FrameId = inner };
        var state = State(Enforcement.Strict,
            new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null)),
            new SequenceStarted(inner, "resolve-hits", new SequenceContext(Samples.Unit, [], [], null)),
            new AskOpened(innerAsk), new AskAnswered(innerAsk.Id, Samples.Team1, SpentByTeam1, false));
        var ok = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AbandonSequence(Samples.Frame), Watcher));
        Assert.Equal([typeof(AskClosed), typeof(SequenceAbandoned)], ok.Events.Select(e => e.GetType()));
        Assert.Single(((AskClosed)ok.Events[0]).Answers);
    }

    [Fact]
    public void Legal_commands_are_enumerated_from_the_open_asks()
    {
        var state = State(Enforcement.Warn, new AskOpened(Activate));
        var legal = Dispatcher().EnumerateLegalCommands(state, Samples.Team1);
        Assert.Equal(2, legal.Count);
        Assert.Contains(legal, c => c is AnswerAsk { Answer: Chosen chosen } && chosen.OptionId == ActivateId);
        Assert.Contains(legal, c => c is AnswerAsk { Answer: Passed });
        Assert.Empty(Dispatcher().EnumerateLegalCommands(state, Samples.Team2));
        Assert.Empty(Dispatcher().EnumerateLegalCommands(state, null));
    }

    [Fact]
    public void Engine_commands_go_to_the_engine_and_the_inert_engine_refuses_them()
    {
        Assert.Equal(CommandClass.Engine, CommandDispatcher.Classify(new RerollDice(Samples.Frame, Samples.Roll, [])));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(Enforcement.Silent), new RerollDice(Samples.Frame, Samples.Roll, []), Clay)).Reason);
    }

    [Fact]
    public void Control_is_overridden_from_the_menu_for_a_setup_or_a_placed_marker_under_every_setting()
    {
        foreach (var e in new[] { Enforcement.Silent, Enforcement.Warn, Enforcement.Strict })
        {
            var state = State(e, new MarkerPlaced(Samples.Token));
            var setup = state.Board.Table.Markers.First(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
            var given = Assert.IsType<ObjectiveControlSet>(Assert.Single(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new SetObjectiveControl(setup, Samples.Team2), Watcher)).Events));
            Assert.Equal((setup, (Guid?)Samples.Team2), (given.MarkerId, given.TeamId));
            var neutral = Assert.IsType<ObjectiveControlSet>(Assert.Single(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new SetObjectiveControl(Samples.Token.Id, null), Watcher)).Events));
            Assert.Null(neutral.TeamId);
        }
        Assert.Equal(CommandClass.Override, CommandDispatcher.Classify(new SetObjectiveControl(Guid.CreateVersion7(), null)));
    }

    [Fact]
    public void Control_of_no_marker_or_for_no_team_is_refused_as_unknown()
    {
        var state = State();
        var setup = state.Board.Table.Markers.First().Id;
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new SetObjectiveControl(Guid.CreateVersion7(), Samples.Team1), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new SetObjectiveControl(setup, Guid.CreateVersion7()), Clay)).Reason);
    }
}
