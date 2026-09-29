using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class InterpreterTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);

    static Sequence Outer() => new("outer", [
        new LambdaStep("first", ctx => new Complete([], new Dictionary<string, int> { ["x"] = 1 })),
        new LambdaStep("ask", ctx => ctx.History.LastClosed() is { Single: Chosen c } ? new Complete([new ScoreSet(Samples.Team1, c.OptionId == "a" ? 10 : 20)], new Dictionary<string, int> { ["x"] = 2 })
            : new OpenAsks([ctx.Ask("pick", "Pick", ctx.ActorTeam!.Value, new Options([new AskOption("a", "A", []), new AskOption("b", "B", [])]))])),
        new LambdaStep("push", ctx => ctx.History.Children.Any(ch => ch.Finished) ? new Complete([]) : new Push("inner", ctx.Frame.Context)),
        new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [], new RollConvention(6, null), null, new HashSet<int> { 6 }), 2); return new Complete([]); })]);
    static Sequence Inner() => new("inner", [new LambdaStep("only", ctx => new Complete([], new Dictionary<string, int> { ["parent_x"] = ctx.Frame.Parent!.Value("x") }))]);

    /// <summary>Appends the engine's payloads as events and advances until it rests. Answers are applied as the dispatcher would log them.</summary>
    static List<EventPayload> Run(GameState state, SequenceInterpreter interpreter)
    {
        var all = new List<EventPayload>();
        for (var guard = 0; guard < 100; guard++)
        {
            var events = interpreter.Advance(state);
            if (events.Count == 0) return all;
            Append(state, events); all.AddRange(events);
        }
        throw new InvalidOperationException("did not rest");
    }
    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
    static void Answer(GameState state, Ask ask, AskAnswer answer) => Append(state, [new AskAnswered(ask.Id, ask.Teams[0], answer, false), new AskClosed(ask.Id, [new TeamAnswer(ask.Teams[0], answer)])]);

    [Fact]
    public void A_sequence_completes_asks_pushes_reads_values_across_frames_and_finishes()
    {
        var state = State([A], [B]);
        var interpreter = new SequenceInterpreter(new StubRules(Outer(), Inner()), TestProperties.Core, new ScriptedDice(6, 3, 5));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var first = Run(state, interpreter);
        Assert.Equal(["step_completed", "ask_opened"], first.Select(EventKinds.Of));
        var ask = ((AskOpened)first[1]).Ask;
        Assert.Equal((Samples.Team1, "pick", state.Projection.Stack[0].Id), (ask.Teams[0], ask.Question, ask.FrameId));
        Assert.Equal(1, FrameReader.Read(state)[0].Position);
        Assert.Empty(interpreter.Advance(state));                                                 // waiting on the Ask

        Answer(state, ask, new Chosen("a"));
        var rest = Run(state, interpreter);
        // R-F9: the "push" step consumes one Advance emitting only sequence_started (no step_completed of its own), then a second
        // Advance after its child finishes emits the step_completed that ends "push" — one more step_completed than a naive count
        // of the sequence's four step names would suggest.
        Assert.Equal(["step_completed", "sequence_started", "step_completed", "sequence_finished", "step_completed", "step_completed", "dice_rerolled", "sequence_finished"], rest.Select(EventKinds.Of));
        var askStep = (StepCompleted)rest[0];
        Assert.Equal((new Chosen("a"), 10), (askStep.Answer, ((ScoreSet)askStep.Changes[0]).Score));
        Assert.Equal(2, ((StepCompleted)rest[2]).Values!["parent_x"]);                              // the child reads the parent's folded values: "ask" overwrote x
        var roll = ((StepCompleted)rest[5]).Rolls.Single();
        Assert.Equal([6, 3], roll.Dice.Select(d => d.Raw));
        Assert.Equal(5, ((DiceRerolled)rest[6]).Replaced.Single().New.Raw);                        // the forced reroll drew the third scripted die
        Assert.Empty(state.Projection.Stack);
        Assert.Equal(3, DiceCursor.DrawnSoFar(state));
    }

    [Fact]
    public void Waiting_between_steps_gates_every_boundary_with_a_confirm_ask_to_the_actor()
    {
        var state = State([A], [B], waits: true);
        var interpreter = new SequenceInterpreter(new StubRules(Outer(), Inner()), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var first = Run(state, interpreter);
        Assert.Equal(["step_completed", "ask_opened"], first.Select(EventKinds.Of));
        var confirm = ((AskOpened)first[1]).Ask;
        Assert.Equal((AskQuestions.Confirm, Samples.Team1), (confirm.Question, confirm.Teams[0]));
        Assert.IsType<Confirmation>(confirm.Legal);
        Answer(state, confirm, new Confirmed());
        var next = Run(state, interpreter);
        Assert.Equal("pick", ((AskOpened)next.Single()).Ask.Question);                            // the confirm is spent; the step's own Ask follows
    }

    /// <summary>
    /// The gate on a frame with no actor and no parent does not read WaitsBetweenSteps, which is off here, and it carries every team.
    /// The frame is still on the stack while it is open, which is what makes the roll-off's dice editable.
    /// </summary>
    [Fact]
    public void An_actorless_root_frames_boundary_gates_every_team_whatever_the_wait_setting()
    {
        var state = State([A], [B]);
        var interpreter = new SequenceInterpreter(new StubRules(new Sequence("two", [new LambdaStep("a", _ => new Complete([])), new LambdaStep("b", _ => new Complete([]))])), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "two", new SequenceContext(null, [], [], null)));
        var first = Run(state, interpreter);
        Assert.Equal(["step_completed", "ask_opened"], first.Select(EventKinds.Of));
        var confirm = ((AskOpened)first[1]).Ask;
        Assert.Equal((AskQuestions.Confirm, false), (confirm.Question, confirm.Sealed));
        Assert.Equal([Samples.Team1, Samples.Team2], confirm.Teams);
        Assert.Single(state.Projection.Stack);                                                    // the frame is live, so its rolls are still addressable
        Answer(state, confirm, new Confirmed());                                                  // unsealed: one team's answer closes it
        Assert.Equal(["step_completed", "sequence_finished"], Run(state, interpreter).Select(EventKinds.Of));
    }

    [Fact]
    public void Abandoning_pops_the_frames_and_an_unknown_sequence_throws()
    {
        var state = State([A], [B], waits: true);
        var interpreter = new SequenceInterpreter(new StubRules(new Sequence("one", [new LambdaStep("a", _ => new Complete([]))])), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "one", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "sequence_finished"], Run(state, interpreter).Select(EventKinds.Of));   // position 0 is never gated
        Append(state, interpreter.Start(state, "one", new SequenceContext(A.Id, [], [], null)));
        Append(state, [new SequenceAbandoned(state.Projection.Stack[0].Id)]);
        Assert.Empty(interpreter.Advance(state));
        Assert.Throws<UnknownSequenceException>(() => interpreter.Start(state, "nope", new SequenceContext(null, [], [], null)));
    }

    /// <summary>
    /// R-F2: Complete(Repeat: true) must hold the frame at the same step for another run, with a fresh history, recoverable from
    /// the effective log alone (StepRepeat.Key on the wire). The step decides whether to repeat from the count of its own prior
    /// step_completed rows for its own name — exactly what a step is left with across a suspension.
    /// </summary>
    [Fact]
    public void A_step_completing_with_Repeat_reruns_at_the_same_position_until_it_stops()
    {
        var state = State([A], [B]);
        var seq = new Sequence("repeats", [
            new LambdaStep("first", ctx => new Complete([], Repeat: ctx.Frame.Frame.Steps.Count(s => s.Step == "first") == 0)),
            new LambdaStep("second", ctx => new Complete([]))]);
        var interpreter = new SequenceInterpreter(new StubRules(seq), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "repeats", new SequenceContext(A.Id, [], [], null)));

        var all = new List<EventPayload>();
        var e1 = interpreter.Advance(state); Append(state, e1); all.AddRange(e1);
        Assert.Equal(0, FrameReader.Read(state)[0].Position);                                     // still on "first": the repeat held position

        var e2 = interpreter.Advance(state); Append(state, e2); all.AddRange(e2);
        Assert.Equal(1, FrameReader.Read(state)[0].Position);                                     // the non-repeat completion advances past it

        all.AddRange(Run(state, interpreter));
        Assert.Equal(["step_completed", "step_completed", "step_completed", "sequence_finished"], all.Select(EventKinds.Of));
        Assert.Equal(["first", "first", "second"], all.OfType<StepCompleted>().Select(s => s.Step));
    }

    static Sequence Repeating() => new("repeating", [
        new LambdaStep("a", ctx => new Complete([], Repeat: ctx.Frame.Frame.Steps.Count(s => s.Step == "a") < 3)),
        new LambdaStep("b", ctx => new Complete([], Repeat: ctx.Frame.Frame.Steps.Count(s => s.Step == "b") < 2)),
        new LambdaStep("c", ctx => new Complete([]))]);

    [Fact]
    public void Successive_repeating_steps_hold_and_advance_position_from_their_own_history()
    {
        var state = State([A], [B]);
        var interpreter = new SequenceInterpreter(new StubRules(Repeating()), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "repeating", new SequenceContext(A.Id, [], [], null)));

        var positions = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            Append(state, interpreter.Advance(state));
            var frame = FrameReader.Read(state)[0];
            positions.Add(frame.Position);
            Assert.DoesNotContain(StepRepeat.Key, frame.Values.Keys);
        }
        Assert.Equal([0, 0, 0, 1, 1, 1, 2], positions);

        var last = interpreter.Advance(state);
        Assert.Equal(["step_completed", "sequence_finished"], last.Select(EventKinds.Of));
        Assert.Equal("c", ((StepCompleted)last[0]).Step);
    }

    [Fact]
    public void Undoing_the_last_repeat_completion_gives_back_the_earlier_position()
    {
        var state = State([A], [B]);
        var interpreter = new SequenceInterpreter(new StubRules(Repeating()), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "repeating", new SequenceContext(A.Id, [], [], null)));
        for (var i = 0; i < 3; i++) Append(state, interpreter.Advance(state));                    // a, a, a: all repeats, position 0
        var beforeLast = state.Projection.LastSeq;
        Append(state, interpreter.Advance(state));                                                // a completes for real
        Assert.Equal(1, FrameReader.Read(state)[0].Position);

        Append(state, [new Undone(beforeLast)]);
        Assert.Equal(0, FrameReader.Read(state)[0].Position);
        Append(state, interpreter.Advance(state));                                                // the undone completion is redone from the log alone
        Assert.Equal(1, FrameReader.Read(state)[0].Position);
    }
}
