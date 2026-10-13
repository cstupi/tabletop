using System.Diagnostics;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

public enum CommandClass { Override, Undo, Abandon, Answer, Engine }

public abstract record DispatchOutcome;
public sealed record Applied(IReadOnlyList<EventPayload> Events, string? Warning) : DispatchOutcome;
public sealed record Refused(RefusalReason Reason, string Detail) : DispatchOutcome;

/// <summary>Turns a command into the events it produces, or a refusal. Never appends and never broadcasts; the session does both.</summary>
public sealed class CommandDispatcher(IRulesEngine engine)
{
    public static CommandClass Classify(Command command) => command switch
    {
        Undo => CommandClass.Undo,
        AbandonSequence => CommandClass.Abandon,
        AnswerAsk => CommandClass.Answer,
        MoveModels or PlaceMarker or MoveMarker or RemoveMarker or SetWounds or SetCondition or RecordAction or ClearActionRecord
            or AddGrant or RemoveGrant or SetSpellTokens or SetWoundsThisActivation or SetScore or SetActiveTeam or SetRound or SetObjectiveControl => CommandClass.Override,
        _ => CommandClass.Engine,
    };

    public DispatchOutcome Dispatch(GameState state, Command command, Actor actor) => Classify(command) switch
    {
        CommandClass.Override => Override(state, command, actor),
        CommandClass.Undo => Replay.IsValidUndoTarget(state.Applier, ((Undo)command).ToSeq)
            ? new Applied([new Undone(((Undo)command).ToSeq)], null) : new Refused(RefusalReason.Unknown, $"seq {((Undo)command).ToSeq} is not an undo point"),
        CommandClass.Abandon => Abandon(state, ((AbandonSequence)command).FrameId),
        CommandClass.Answer => Answer(state, (AnswerAsk)command, actor),
        _ => Engine(state, command, actor),
    };

    /// <summary>
    /// The menu's one event. A set_round carries the round-start changes the scheduler would write, so a round started by hand is a whole round
    /// (enforcement spec → A menu round start). A hand move is judged and decided under the enforcement setting (in-game UX spec → Hand moves
    /// under enforcement); every other menu edit is applied under every setting.
    /// </summary>
    private DispatchOutcome Override(GameState state, Command command, Actor actor)
    {
        if (MenuTranslation.Translate(state, command, out var missing) is not { } payload)
            return new Refused(RefusalReason.Unknown, missing ?? "unknown command");
        if (payload is RoundStarted r) payload = r with { Changes = engine.RoundStartChanges(state, r.Round) };
        var verdict = command is MoveModels move ? EnforcementPolicy.Decide(state.Info.Enforcement, HandMoves.Judge(state, move, actor)) : new Accept(null);
        switch (verdict)
        {
            case Refuse refuse: return new Refused(refuse.Reason, refuse.Detail);
            case EndGame: throw new UnreachableException("a hand move's breach is always StrictRefuses");
        }
        return new Applied([payload], ((Accept)verdict).Warning);
    }

    private static DispatchOutcome Abandon(GameState state, Guid frameId)
    {
        var stack = state.Projection.Stack;
        var index = stack.ToList().FindIndex(f => f.Id == frameId);
        if (index < 0) return new Refused(RefusalReason.Unknown, $"no frame {frameId} on the stack");
        var popped = stack.Skip(index).Select(f => f.Id).ToHashSet();
        var events = new List<EventPayload>();
        foreach (var open in state.Projection.OpenAsks.Values.Where(a => a.Ask.FrameId is { } f && popped.Contains(f)))
            events.Add(new AskClosed(open.Ask.Id, open.Answers.Where(kv => kv.Value is not null).Select(kv => new TeamAnswer(kv.Key, kv.Value!)).ToList()));
        events.Add(new SequenceAbandoned(frameId));
        return new Applied(events, null);
    }

    private static DispatchOutcome Answer(GameState state, AnswerAsk command, Actor actor)
    {
        if (!state.Projection.OpenAsks.TryGetValue(command.AskId, out var open)) return new Refused(RefusalReason.Unknown, $"no open ask {command.AskId}");
        var ask = open.Ask;
        if (actor.TeamId is not { } team || !ask.Teams.Contains(team)) return new Refused(RefusalReason.NotPermitted, "this ask is not addressed to your team");
        if (open.Answers.ContainsKey(team)) return new Refused(RefusalReason.NotPermitted, "your team has already answered");
        var inSet = LegalSetMembership.Contains(ask, command.Answer);
        // An answer to a turn Ask that the ruleset cannot read (an option id that does not parse, a unit the log no longer has)
        // is outside the legal set as much as an unknown option is, so it takes the same three positions under the enforcement dial.
        // Passing is a real answer and is never probed; a framed Ask, an Ambusher Ask and the boundary confirmation are not turn Asks.
        var readable = command.Answer is Passed || !SchedulerLog.IsTurnAsk(ask) || state.Rules.ReadAnswer(state, team, command.Answer) is not null;
        var detail = inSet ? $"answer to '{ask.Question}' names nothing this ruleset can read" : $"answer to '{ask.Question}' is outside the legal set";
        // An answer inside the set can still break a rule only the table answers (IGameRules.AnswerBreach): the ruleset names it, and strict refuses it.
        var breach = inSet && readable ? state.Rules.AnswerBreach(state, ask, command.Answer) : null;
        var legal = inSet && readable && breach is null;
        var verdict = breach is not null
            ? EnforcementPolicy.Decide(state.Info.Enforcement, new Breach(breach.Rule, $"answer to '{ask.Question}': {breach.Detail}", new StrictRefuses(RefusalReason.Illegal)))
            : EnforcementPolicy.Decide(state.Info.Enforcement, legal ? Legality.Legal : Legality.OutsideLegalSet, detail);
        switch (verdict)
        {
            case Refuse r: return new Refused(r.Reason, r.Detail);
            case EndGame: throw new UnreachableException("a legality breach is always StrictRefuses");
        }
        var events = new List<EventPayload> { new AskAnswered(ask.Id, team, command.Answer, !inSet || !readable, breach?.Rule) };
        var answered = new Dictionary<Guid, AskAnswer>(open.Answers.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!)) { [team] = command.Answer };
        if (!ask.Sealed || ask.Teams.All(answered.ContainsKey))
            events.Add(new AskClosed(ask.Id, ask.Teams.Where(answered.ContainsKey).Select(t => new TeamAnswer(t, answered[t])).ToList()));
        return new Applied(events, ((Accept)verdict).Warning);
    }

    private DispatchOutcome Engine(GameState state, Command command, Actor actor)
    {
        var verdict = EnforcementPolicy.Decide(state.Info.Enforcement, engine.Judge(state, command, actor), $"{command.GetType().Name} is illegal here");
        switch (verdict)
        {
            case Refuse r: return new Refused(r.Reason, r.Detail);
            case EndGame: throw new UnreachableException("a legality breach is always StrictRefuses");
        }
        return engine.Execute(state, command, actor) switch
        {
            Emit e => new Applied(e.Events, ((Accept)verdict).Warning),
            RefuseCommand rc => new Refused(rc.Reason, rc.Detail),
            var other => throw new NotSupportedException(other.GetType().Name),
        };
    }

    /// <summary>The discrete part of legality, for a team: every option of every open Ask addressed to it, plus the pass where allowed. Continuous legal sets (regions, allocations) are parameters the client renders and are not enumerated here.</summary>
    public IReadOnlyList<Command> EnumerateLegalCommands(GameState state, Guid? teamId)
    {
        if (teamId is not { } team) return [];
        var result = new List<Command>();
        foreach (var open in state.Projection.OpenAsks.Values.Where(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team)))
        {
            if (open.Ask.Legal is Options o) result.AddRange(o.Choices.Select(c => new AnswerAsk(open.Ask.Id, new Chosen(c.Id))));
            if (open.Ask.Legal is Confirmation) result.Add(new AnswerAsk(open.Ask.Id, new Confirmed()));
            if (open.Ask.AllowPass) result.Add(new AnswerAsk(open.Ask.Id, new Passed()));
        }
        return result;
    }
}
