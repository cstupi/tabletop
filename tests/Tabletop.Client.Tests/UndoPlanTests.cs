using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class UndoPlanTests
{
    static readonly Guid P2 = Guid.Parse("0198b7ac-0031-7000-8000-000000000031");
    static readonly GameInfo Game = Samples.Game with { Participants = [.. Samples.Game.Participants, new Participant(P2, Guid.CreateVersion7(), "Dana", Samples.Team2, null)] };

    /// <summary>Each entry: who acted (null for the engine) and what.</summary>
    static (UndoPlan? Plan, IReadOnlyList<GameEvent> Log, Names Names) Plan(params (Guid? Actor, EventPayload Payload)[] history)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(history.Select((h, i) => new GameEvent(i + 1, h.Actor, h.Payload, null)));
        var n = new Names(Game, a.Projection);
        return (UndoPlan.Of(a.Log, n), a.Log, n);
    }

    static (Guid?, EventPayload) Engine(EventPayload p) => (null, p);
    static (Guid?, EventPayload) Me(EventPayload p) => (Samples.P1, p);
    static (Guid?, EventPayload) Them(EventPayload p) => (P2, p);
    static readonly EventPayload Round1 = new RoundStarted(1, [Samples.Team1, Samples.Team2], []);
    static EventPayload Move => new ModelsMoved([Samples.Deploy]);
    static EventPayload Score => new ScoreSet(Samples.Team2, 1);

    [Fact]
    public void Nothing_undoable_is_null()
    {
        Assert.Null(Plan().Plan);
        Assert.Null(Plan(Engine(Round1), Engine(new PhaseStarted("deployment"))).Plan);
    }

    [Fact]
    public void The_last_action_is_undone_whoever_took_it()
    {
        var (plan, log, n) = Plan(Engine(Round1), Me(Move), Them(Score));
        Assert.NotNull(plan);
        Assert.Equal(2L, plan.ToSeq);
        Assert.Equal(3L, plan.Action.Seq);
        Assert.Equal(P2, plan.Action.Actor);
        Assert.Equal("Undo: " + LogText.Describe(log[2], n), plan.Tooltip);
    }

    [Fact]
    public void A_first_action_undoes_to_zero()
    {
        var plan = Plan(Me(Move), Engine(new PhaseStarted("x"))).Plan!;
        Assert.Equal(0L, plan.ToSeq);
        Assert.Equal(1L, plan.Action.Seq);
    }

    [Fact]
    public void Engine_follow_ups_after_an_action_are_inside_the_range()
    {
        var plan = Plan(Engine(Round1), Me(Move), Engine(new PhaseStarted("x")), Engine(new TurnStarted(Samples.Team2))).Plan!;
        Assert.Equal(1L, plan.ToSeq);
        Assert.Equal(2L, plan.Action.Seq);
    }

    /// <summary>Undo, then undo again: the marker is never the action, the undone action is skipped, and the target is a live seq.</summary>
    [Fact]
    public void Each_press_steps_back_one_more_action()
    {
        var plan = Plan(Engine(Round1), Me(Move), Engine(new PhaseStarted("x")), Them(Score), Me(new Undone(3))).Plan!;
        Assert.Equal(1L, plan.ToSeq);
        Assert.Equal(2L, plan.Action.Seq);
        var again = Plan(Engine(Round1), Me(Move), Engine(new PhaseStarted("x")), Them(Score), Me(new Undone(3)), Me(new Undone(1))).Plan;
        Assert.Null(again);
    }

    /// <summary>The owner, 2026-11-14: "undo is NOT letting me undo my advance". A pass on a window (before-attacking, a reaction) is not the
    /// action Undo steps back to: undoing only the pass reopens the window it passed.
    /// Undo takes the action before it, the pass going with it.</summary>
    [Fact]
    public void A_pass_on_a_window_is_not_an_action_and_goes_with_the_one_before()
    {
        var window = Samples.Ask with { Id = Guid.CreateVersion7(), Question = "window", WindowKind = "before-attacking", AllowPass = true };
        var plan = Plan(Engine(Round1), Me(Move), Engine(new AskOpened(window)), Me(new AskAnswered(window.Id, Samples.Team1, new Passed(), false)),
            Me(new AskClosed(window.Id, [new TeamAnswer(Samples.Team1, new Passed())])), Engine(new PhaseStarted("x"))).Plan!;
        Assert.Equal((1L, 2L), (plan.ToSeq, plan.Action.Seq));                       // the move, not the pass after it
        var chosen = new Chosen("cast");                                             // a window answered with a choice is an action
        var cast = Plan(Engine(Round1), Me(Move), Engine(new AskOpened(window)), Me(new AskAnswered(window.Id, Samples.Team1, chosen, false))).Plan!;
        Assert.Equal((3L, 4L), (cast.ToSeq, cast.Action.Seq));
        var ask = Samples.Ask with { Id = Guid.CreateVersion7(), AllowPass = true };  // a pass on an Ask that is no window is an action
        var pass = Plan(Engine(Round1), Me(Move), Engine(new AskOpened(ask)), Me(new AskAnswered(ask.Id, Samples.Team1, new Passed(), false))).Plan!;
        Assert.Equal((3L, 4L), (pass.ToSeq, pass.Action.Seq));
    }

    /// <summary>The server stamps an Ask's close with the participant whose answer closed it; the answer is the action, so the Ask reopens unanswered.</summary>
    [Fact]
    public void An_answer_and_the_close_it_caused_are_one_action()
    {
        var answer = new Chosen("rush");
        var plan = Plan(Engine(Round1), Engine(new AskOpened(Samples.Ask)), Me(new AskAnswered(Samples.AskId, Samples.Team1, answer, false)),
            Me(new AskClosed(Samples.AskId, [new TeamAnswer(Samples.Team1, answer)])), Engine(new PhaseStarted("x"))).Plan!;
        Assert.Equal(2L, plan.ToSeq);
        Assert.Equal(3L, plan.Action.Seq);
    }

    [Fact]
    public void A_range_with_no_rolls_loses_none()
    {
        Assert.Empty(Plan(Engine(Round1), Me(Move), Engine(new PhaseStarted("x"))).Plan!.LostRolls);
    }

    [Fact]
    public void A_roll_in_the_range_is_listed_in_the_logs_collapsed_words()
    {
        var (plan, log, n) = Plan(Engine(Round1), Me(Move),
            Engine(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null))),
            Engine(new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], [])));
        var roll = CombatLog.Build(log, n).OfType<RollEntry>().Single();
        Assert.Equal([roll.Collapsed(CombatLog.Pip)], plan!.LostRolls);
    }

    [Fact]
    public void A_reroll_or_a_dice_edit_in_the_range_counts()
    {
        EventPayload[] before =
        [
            Round1, new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
        ];
        var reroll = new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]);
        var (plan, log, n) = Plan([.. before.Select(Engine), Me(reroll)]);
        var roll = CombatLog.Build(log, n).OfType<RollEntry>().Single();
        Assert.Equal(3L, plan!.ToSeq);
        Assert.Equal([roll.Collapsed(CombatLog.Pip)], plan.LostRolls);           // the line already ends with its live amendments
        Assert.EndsWith(" · rerolled ⚁ → ⚄", plan.LostRolls[0]);

        var edit = new DiceEdited(Samples.Frame, Samples.Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]);
        var (edited, elog, en) = Plan([.. before.Select(Engine), Me(edit)]);
        Assert.Equal([CombatLog.Build(elog, en).OfType<RollEntry>().Single().Collapsed(CombatLog.Pip)], edited!.LostRolls);
        Assert.EndsWith(" · set by hand ⚅ → ⚀", edited.LostRolls[0]);
    }

    [Fact]
    public void A_roll_and_its_reroll_both_in_the_range_are_listed_once()
    {
        var reroll = new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]);
        var (plan, log, n) = Plan(Engine(Round1), Me(Move),
            Engine(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null))),
            Engine(new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], [])), Engine(reroll));
        Assert.Equal(1L, plan!.ToSeq);
        Assert.Equal([CombatLog.Build(log, n).OfType<RollEntry>().Single().Collapsed(CombatLog.Pip)], plan.LostRolls);
    }

    [Fact]
    public void An_undone_roll_before_the_target_is_not_lost_again()
    {
        var plan = Plan(Engine(Round1), Me(Move),
            Engine(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null))),
            Engine(new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], [])),
            Me(new Undone(2)), Them(Score)).Plan!;
        Assert.Equal(2L, plan.ToSeq);
        Assert.Empty(plan.LostRolls);
    }
}
