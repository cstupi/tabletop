using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>A step is ruleset C#. It reads game state and its frame and ends one of four ways. It holds nothing between runs.
/// Label: the player-facing name in the sequence's outline (SequenceStarted.Outline); null for an internal step, which the outline leaves out.</summary>
public interface IStep { string Name { get; } string? Label => null; StepOutcome Run(StepContext ctx); }

/// <summary>A step under a player-facing label. The same name and the same run: a label is a property of the step's place in a sequence, not of its class.</summary>
public sealed class LabelledStep(IStep step, string label) : IStep
{
    public string Name => step.Name;
    public string? Label => label;
    public StepOutcome Run(StepContext ctx) => step.Run(ctx);
}

public static class StepLabels
{
    public static IStep Labelled(this IStep step, string label) => new LabelledStep(step, label);
}

/// <summary>Starts a sequence on the stack: what the scheduler (Task 10) needs from the interpreter.</summary>
public interface ISequenceStarter { IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context); }

/// <summary>No two consecutive steps share a name: a repeated completion of one name is how a repeating step is told from the next step.</summary>
public sealed record Sequence(string Name, IReadOnlyList<IStep> Steps)
{
    public IReadOnlyList<IStep> Steps { get; } = Steps.Zip(Steps.Skip(1)).Any(p => p.First.Name == p.Second.Name)
        ? throw new ArgumentException($"{Name}: consecutive steps share a name") : Steps;
}

public abstract record StepOutcome;
/// <summary>step_completed; the frame advances. Values are the settled parameters later steps and child frames read. Repeat keeps the position: the same step runs again with a fresh history (a move step's next segment).</summary>
public sealed record Complete(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null, bool Repeat = false) : StepOutcome;
/// <summary>step_completed, then sequence_finished: the remaining steps never happen (a move that cannot reach ends the activation).</summary>
public sealed record Finish(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null) : StepOutcome;
/// <summary>The frame suspends until every Ask is closed. Several Asks: a window offered to several teams, one each.</summary>
public sealed record OpenAsks(IReadOnlyList<Ask> Asks) : StepOutcome;
/// <summary>A nested sequence starts; this step runs again when it finishes and sees it in History.Children.</summary>
public sealed record Push(string Sequence, SequenceContext Context) : StepOutcome;

/// <summary>
/// The reserved key SequenceInterpreter writes into a repeating step's StepCompleted.Values (R-F2): a step that returned
/// Complete(Repeat: true) is recoverable as a repeat from the effective log alone, because a step holds no C# locals across
/// a suspension. Prefixed with '$' so it can never collide with a ruleset's own snake_case keys, and so a client's log
/// panel can filter it out by this one name.
/// </summary>
public static class StepRepeat
{
    public const string Key = "$repeat";
}

public sealed record AskRecord(Ask Ask, IReadOnlyDictionary<Guid, AskAnswer>? Answers)
{
    public bool Closed => Answers is not null;
    public AskAnswer? Single => Answers is { Count: 1 } a ? a.Values.First() : null;
}
public sealed record ChildRecord(Guid FrameId, string Sequence, SequenceContext Context, bool Finished, bool Abandoned);

public sealed record StepHistory(IReadOnlyList<AskRecord> Asks, IReadOnlyList<ChildRecord> Children)
{
    public static readonly StepHistory Empty = new([], []);
    public bool Waiting => Asks.Any(a => !a.Closed);
    /// <summary>With no question, the step-boundary Confirm is never the answer: it belongs to the gate, not to the step.</summary>
    public AskRecord? LastClosed(string? question = null) =>
        (question is null ? ClosedExceptConfirm : Asks.Where(a => a.Closed && a.Ask.Question == question)).LastOrDefault();
    public int Confirmed => Asks.Count(a => a.Closed && a.Ask.Question == AskQuestions.Confirm);
    public IEnumerable<AskRecord> ClosedExceptConfirm => Asks.Where(a => a.Closed && a.Ask.Question != AskQuestions.Confirm);
}

public sealed record FrameView(Frame Frame, int Position, IReadOnlyDictionary<string, int> Values, StepHistory History, FrameView? Parent)
{
    public Guid Id => Frame.Id;
    public SequenceContext Context => Frame.Context;
    public int Value(string key, int fallback = 0) => Values.GetValueOrDefault(key, fallback);
}
