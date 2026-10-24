using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Runs the top frame's current step and turns its outcome into payloads. Generic: knows no ruleset.</summary>
public sealed class SequenceInterpreter(IGameRules rules, PropertySet properties, IDiceSource dice) : ISequenceStarter
{
    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context) =>
        [new SequenceStarted(Guid.CreateVersion7(), sequence, context, Outline(rules.Sequence(sequence)))];   // Sequence throws UnknownSequenceException

    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        var frames = FrameReader.Read(state);
        if (frames.Count == 0) return [];
        var top = frames[^1];
        var sequence = rules.Sequence(top.Frame.Sequence);
        if (top.Position >= sequence.Steps.Count) return [new SequenceFinished(top.Id)];
        if (top.History.Waiting) return [];
        var actorTeam = top.Context.ActorUnitId is { } u ? Teams.Of(state, u) : null;
        // A boundary is before every step but the first, and before a repeat of the first in a frame with an actor: a repeat at position 0
        // (assign's segments, a placement's roll) settles what it rolled on the next run, so the wait setting stops there too.
        var boundary = top.Position > 0 || (top.Frame.Steps.Count > 0 && actorTeam is not null);
        if (boundary && top.History.Confirmed == 0 && GateTeams(state, top, actorTeam) is { } gate)
            return [new AskOpened(new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, $"Continue: {sequence.Steps[top.Position].Name}", gate, new Confirmation(), false, false, null, top.Id))];

        var step = sequence.Steps[top.Position];
        var ctx = new StepContext(state, properties, rules, top, new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state)));
        var events = new List<EventPayload>();
        switch (step.Run(ctx))
        {
            case Complete c: Completed(events, ctx, step, c.Changes, RepeatValues(c), finished: !c.Repeat && top.Position + 1 == sequence.Steps.Count); break;
            case Finish f: Completed(events, ctx, step, f.Changes, f.Values, finished: true); break;
            case OpenAsks { Asks.Count: 0 }: throw new InvalidOperationException($"{top.Frame.Sequence}/{step.Name} suspended with no Ask open");
            case OpenAsks a: events.AddRange(a.Asks.Select(ask => new AskOpened(ask))); break;
            case Push p: events.Add(new SequenceStarted(Guid.CreateVersion7(), p.Sequence, p.Context, Outline(rules.Sequence(p.Sequence)))); break;
        }
        return events;
    }

    /// <summary>
    /// R-F2: a repeat must be recoverable from the effective log alone, since a step holds no C# locals across a suspension and
    /// FrameReader rebuilds the frame from the log. Complete(Repeat: true) rides the reserved StepRepeat.Key in this
    /// step_completed's Values; FrameReader reads it back to hold the frame's position at this step for one more run.
    /// </summary>
    static IReadOnlyDictionary<string, int>? RepeatValues(Complete c)
    {
        if (!c.Repeat) return c.Values;
        var values = c.Values is null ? new Dictionary<string, int>() : new Dictionary<string, int>(c.Values);
        values[StepRepeat.Key] = 1;
        return values;
    }

    /// <summary>The sequence's labelled steps in order (in-game UX spec → Step outline), or null when it has none, so an unlabelled
    /// sequence's sequence_started is written as it was before outlines existed.</summary>
    static IReadOnlyList<OutlineStep>? Outline(Sequence sequence)
    {
        var outline = sequence.Steps.Where(s => s.Label is not null).Select(s => new OutlineStep(s.Name, s.Label!)).ToList();
        return outline.Count == 0 ? null : outline;
    }

    /// <summary>
    /// Who a step boundary is gated to, or null for no gate. Only when WaitsBetweenSteps is on, which players never set: the actor's team,
    /// or every team for a frame with no actor and no parent. The roll-off is the only such frame, and nothing else keeps it on the stack,
    /// so the gate is what lets SetDice (the rules suite's, which runs with the setting on) reach its dice. That Ask is unsealed, so the
    /// dispatcher closes it on the first answer and any team releases it. A nested actorless frame - the terrain-wounds deal-wounds -
    /// is not gated, because its own assign Ask already suspends it.
    /// </summary>
    static IReadOnlyList<Guid>? GateTeams(GameState state, FrameView top, Guid? actorTeam) =>
        !state.Info.WaitsBetweenSteps ? null
        : actorTeam is { } team ? [team]
        : top.Parent is null ? state.Info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList()
        : null;

    static void Completed(List<EventPayload> events, StepContext ctx, IStep step, IReadOnlyList<StateChange> changes, IReadOnlyDictionary<string, int>? values, bool finished)
    {
        var answer = ctx.History.ClosedExceptConfirm.LastOrDefault()?.Single;
        events.Add(new StepCompleted(ctx.Frame.Id, step.Name, answer, ctx.Rolls, changes, ctx.Applied.Names) { Values = values is { Count: > 0 } ? values : null });
        events.AddRange(ctx.ForcedRerolls);
        if (finished) events.Add(new SequenceFinished(ctx.Frame.Id));
    }
}
