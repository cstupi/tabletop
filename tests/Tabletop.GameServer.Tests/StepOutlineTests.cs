using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>SequenceStarted carries the sequence's labelled steps (in-game UX spec → Step outline).</summary>
public class StepOutlineTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);

    static Sequence Outer() => new("outer", [
        new LambdaStep("window", _ => new Complete([])),
        new LambdaStep("move", _ => new Complete([])).Labelled("Move"),
        new LambdaStep("push", ctx => ctx.History.Children.Any(ch => ch.Finished) ? new Complete([]) : new Push("inner", ctx.Frame.Context)).Labelled("Shoot")]);
    static Sequence Inner() => new("inner", [new LambdaStep("fire", _ => new Complete([])).Labelled("Fire"), new LambdaStep("after", _ => new Complete([]))]);
    static Sequence Bare() => new("bare", [new LambdaStep("only", _ => new Complete([]))]);

    static SequenceInterpreter Interpreter(IGameRules rules) => new(rules, TestProperties.Core, new ScriptedDice(4));
    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
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
    static IEnumerable<(string, string)> Pairs(IReadOnlyList<OutlineStep>? outline) => outline!.Select(o => (o.Step, o.Label));

    [Fact]
    public void A_started_sequence_carries_its_labelled_steps_in_order()
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(new StubRules(Outer(), Inner())).Start(state, "outer", new SequenceContext(A.Id, [], [], null))));
        Assert.Equal([("move", "Move"), ("push", "Shoot")], Pairs(started.Outline));
    }

    [Fact]
    public void A_sequence_with_no_label_carries_no_outline()
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(new StubRules(Bare())).Start(state, "bare", new SequenceContext(A.Id, [], [], null))));
        Assert.Null(started.Outline);
    }

    [Fact]
    public void A_pushed_sequence_carries_its_own_outline_and_every_outline_step_completes_under_its_name()
    {
        var state = State([A], [B]);
        var interpreter = Interpreter(new StubRules(Outer(), Inner()));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var outer = state.Projection.Stack.Single().Outline;
        var run = Run(state, interpreter);
        var inner = run.OfType<SequenceStarted>().Single();
        Assert.Equal([("fire", "Fire")], Pairs(inner.Outline));
        var completed = run.OfType<StepCompleted>().Select(s => s.Step).ToHashSet();
        Assert.All(outer.Concat(inner.Outline!), o => Assert.Contains(o.Step, completed));
    }

    [Fact]
    public void A_frame_started_before_outlines_existed_runs_to_the_end()
    {
        var state = State([A], [B]);
        var interpreter = Interpreter(new StubRules(Outer(), Inner()));
        Append(state, [new SequenceStarted(Guid.CreateVersion7(), "outer", new SequenceContext(A.Id, [], [], null))]);   // as an old log has it
        Assert.Empty(state.Projection.Stack.Single().Outline);
        var run = Run(state, interpreter);
        Assert.Equal(2, run.OfType<SequenceFinished>().Count());
        Assert.Empty(state.Projection.Stack);
    }

    [Fact]
    public void A_label_leaves_the_step_as_it_was()
    {
        var bare = new LambdaStep("move", _ => new Complete([]));
        var labelled = bare.Labelled("Move");
        Assert.Equal(("move", "Move"), (labelled.Name, labelled.Label));
        Assert.Null(((IStep)bare).Label);
    }

    public static IEnumerable<object[]> GrimdarkFutureOutlines() =>
    [
        [GfNames.HoldSeq, new[] { "shoot-or-not:Shoot" }],
        [GfNames.AdvanceSeq, new[] { "move:Move", "shoot-or-not:Shoot" }],
        [GfNames.RushSeq, new[] { "move:Move" }],
        [GfNames.ChargeSeq, new[] { "charge-target:Pick target", "move:Charge move", "defender-closes:Target moves", "fight:Melee" }],
        [GfNames.IdleSeq, Array.Empty<string>()],
        [GfNames.Shooting, new[] { "declare-targets:Pick targets", "fire-groups:Fire" }],
        [GfNames.Melee, new[] { "charger-attacks:Charger attacks", "strike-back:Strike back", "compare:Melee resolution", "consolidate:Consolidation" }],
    ];

    [Theory, MemberData(nameof(GrimdarkFutureOutlines))]
    public void Grimdark_Future_labels_the_action_and_attack_steps(string sequence, string[] expected)
    {
        var state = State([A], [B]);
        var started = Assert.IsType<SequenceStarted>(Assert.Single(Interpreter(GrimdarkFutureRules.Instance).Start(state, sequence, new SequenceContext(A.Id, [], [], null))));
        Assert.Equal(expected, (started.Outline ?? []).Select(o => $"{o.Step}:{o.Label}"));
    }
}
