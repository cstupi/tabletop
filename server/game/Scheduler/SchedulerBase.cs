using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// The shared shape of both schedulers: the phases before round one (alternating one unit a turn), the round boundary (a player's
/// end_round answer, then the first-team policy, the roll-off, round_started with the hooks' changes, and the interactive boundary
/// hook), the rotation over teams, and one activation cycle per open turn. Shapes decide what a completed or passed cycle does from
/// round one on, and what they clear.
/// Implements the interpreter plan's Engine.IScheduler: called with no framed Ask open and an empty stack; starts sequences only
/// through the starter it is handed; holds the game's pinned set for the action rows and the three IGameRules hooks.
/// </summary>
public abstract class SchedulerBase(PropertySet properties) : IScheduler
{
    public const string RoundsPhase = "rounds";

    protected PropertySet Properties { get; } = properties;
    protected TurnCycle Cycle { get; } = new(properties);

    public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter)
    {
        var log = SchedulerLog.Of(state);
        var s = state.Projection.Scheduler;
        var teams = state.Info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList();          // frozen at the flip to playing: the same list orders every round of the game
        if (teams.Count == 0) return [];

        var stale = StaleAsks(state, log);
        if (stale.Count > 0) return stale;
        if (state.Projection.OpenAsks.Count > 0) return [];                                       // an unframed Ask of ours (or a boundary hook's) is live: wait for its answer
        if (s.ActiveTeamId is { } active) return InTurn(state, log, active, starter);
        if (log.RoundStartedAt == 0) return BeginRound(state, log, teams, state.Rules.PhasesBeforeRounds.Count > 0 ? 0 : 1, starter);

        if (s.Round >= 1 && log.SinceRound.All(e => e.Payload is not TurnStarted))
        {
            switch (state.Rules.RoundBoundaryStep(state, Properties, s.Round, s.Order))
            {
                case HookWaiting: return [];
                case HookEmit h: return h.Events;
                case HookStartSequences seqs: return seqs.Sequences.SelectMany(r => starter.Start(state, r.Sequence, r.Context)).ToList();
            }
        }

        if (NextTeam(state, log, s.Order) is { } next) return [new TurnStarted(next)];

        if (s.Round == 0)
        {
            var phases = state.Rules.PhasesBeforeRounds;
            var index = phases.ToList().IndexOf(s.Phase ?? "");
            return index + 1 < phases.Count ? [new PhaseStarted(phases[index + 1])] : BeginRound(state, log, teams, 1, starter);
        }
        // Round one on: the round is over, and it does not advance on its own — a player ends it.
        if (!RoundEnded(state, log, teams, out var gate)) return gate;
        return BeginRound(state, log, teams, s.Round + 1, starter);
    }

    /// <summary>Unframed, unsealed, no pass: the round boundary's Ask. Unsealed means the dispatcher closes it on the first answer, which is how any player on any team ends the round.</summary>
    public static Ask EndRoundAsk(IReadOnlyList<Guid> teams) =>
        new(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", teams, new Confirmation(), AllowPass: false, Sealed: false, WindowKind: null, FrameId: null);

    /// <summary>
    /// Has a player ended this round? True with an empty gate once the boundary Ask has closed with an answer; false with the Ask to
    /// open (or nothing to emit, when no team has a participant to ask). Read from the log since round_started, so undo-by-replay and
    /// a rehydrated server reach the same answer, and a round that already advanced is never re-asked.
    /// </summary>
    private bool RoundEnded(GameState state, SchedulerLog log, IReadOnlyList<Guid> teams, out IReadOnlyList<EventPayload> gate)
    {
        var opened = log.SinceRound.LastOrDefault(e => e.Payload is AskOpened o && o.Ask.Question == AskQuestions.EndRound);
        if (opened is { } o && log.SinceRound.Any(e => e.Seq > o.Seq && e.Payload is AskClosed c
            && c.AskId == ((AskOpened)o.Payload).Ask.Id && c.Answers.Count > 0))
        {
            gate = []; return true;                                                              // answered: BeginRound follows, with the hooks' changes on its round_started
        }
        var asked = teams.Where(t => state.Info.Participants.Any(p => p.TeamId == t)).ToList();   // a participant with no team is never addressed
        gate = asked.Count == 0 ? [] : [new AskOpened(EndRoundAsk(asked))];                        // nobody to ask: wait, as an empty legal set waits
        return false;
    }

    /// <summary>A scheduler Ask opened before the current turn or round began was superseded by a menu edit (set_active_team, set_round); it closes with no answers.</summary>
    private static IReadOnlyList<EventPayload> StaleAsks(GameState state, SchedulerLog log)
    {
        var cutoff = Math.Max(log.TurnStartedAt, log.RoundStartedAt);
        return log.Effective
            .Where(e => e.Seq < cutoff && e.Payload is AskOpened o && SchedulerLog.IsSchedulerAsk(o.Ask) && state.Projection.OpenAsks.ContainsKey(o.Ask.Id))
            .Select(e => (EventPayload)new AskClosed(((AskOpened)e.Payload).Ask.Id, [])).ToList();
    }

    /// <summary>The order from the first-team policy — a roll-off started through the interpreter and read back once finished (Task 3) — then round_started with the shape's and the ruleset's changes.</summary>
    private IReadOnlyList<EventPayload> BeginRound(GameState state, SchedulerLog log, IReadOnlyList<Guid> teams, int round, ISequenceStarter starter)
    {
        IReadOnlyList<Guid> order;
        switch (state.Rules.FirstTeam(state, round, teams))
        {
            case OrderedTeams o: order = o.Order.ToList(); break;                                  // a copy: a hook may hand back Projection.Scheduler.Order itself, which round_started's own apply clears first
            case RollOffNeeded r:
                switch (RollOff.Advance(state, log, r.Sequence, starter))
                {
                    case RollOff.Emit e: return e.Events;
                    case RollOff.Waiting: return [];
                    case RollOff.Resolved res: order = res.Order; break;
                    default: throw new NotSupportedException();
                }
                break;
            default: throw new NotSupportedException();
        }
        var changes = new List<StateChange>(ShapeRoundStartChanges(state, round));
        changes.AddRange(state.Rules.OnRoundStarted(state, Properties, round));
        var events = new List<EventPayload> { new RoundStarted(round, order, changes) };
        if (round == 0) events.Add(new PhaseStarted(state.Rules.PhasesBeforeRounds[0]));
        else if (round == 1) events.Add(new PhaseStarted(RoundsPhase));
        return events;
    }

    private IReadOnlyList<EventPayload> InTurn(GameState state, SchedulerLog log, Guid team, ISequenceStarter starter)
    {
        var beforeRounds = state.Projection.Scheduler.Round == 0;
        if (!beforeRounds && Cycle.Read(state, team, log) is NoAsk && BeforeFirstAsk(state, log, team) is { Count: > 0 } opening) return opening;
        switch (Cycle.Step(state, team, log, starter, beforeRounds ? null : ask => ShapeAsk(state, ask)))
        {
            case CycleEmit e: return e.Events;
            case CycleWaiting: return [];
            case CycleEmpty:
                var empty = new TurnContext(team, null, log.TurnStartedAt, log.TurnStartedAt);
                return beforeRounds ? EndTurn(state, empty, passed: false) : OnEmpty(state, log, empty);
            case CyclePassed p: return beforeRounds ? EndTurn(state, p.Turn, passed: true) : OnPassed(state, log, p.Turn);
            case CycleComplete c: return beforeRounds ? EndTurn(state, c.Turn, passed: false) : OnComplete(state, log, c.Turn);
            default: throw new NotSupportedException();
        }
    }

    /// <summary>Order[0] after a round or phase start; otherwise the team after the last turn_ended; finished teams skipped; null when every team is finished.</summary>
    protected Guid? NextTeam(GameState state, SchedulerLog log, IReadOnlyList<Guid> order)
    {
        if (order.Count == 0) return null;
        var last = log.LastTurnEndedTeam(log.SinceRotation);
        var start = last is null ? 0 : (TeamUnits.IndexOf(order, last.Value) + 1) % order.Count;
        for (var i = 0; i < order.Count; i++)
        {
            var team = order[(start + i) % order.Count];
            if (!IsFinished(state, log, team)) return team;
        }
        return null;
    }

    /// <summary>Passed this round, or nothing legal to do — read again every time (scheduler → Re-entering a round).</summary>
    protected virtual bool IsFinished(GameState state, SchedulerLog log, Guid teamId) =>
        state.Projection.Scheduler.Passed.Contains(teamId) || state.Rules.LegalTurn(state, Properties, teamId) is null;

    protected IReadOnlyList<EventPayload> EndTurn(GameState state, TurnContext turn, bool passed)
    {
        var changes = new List<StateChange>(ShapeTurnEndChanges(state, turn));
        changes.AddRange(state.Rules.OnTurnEnded(state, Properties, turn));
        return [new TurnEnded(turn.TeamId, passed, changes)];
    }

    /// <summary>ActionRecordCleared for every unit that has an action on record: what a shape clears at a round start or a turn end.</summary>
    protected static IReadOnlyList<StateChange> ClearRecords(IEnumerable<UnitInPlay> units) =>
        units.Where(u => u.Actions.Count > 0).Select(u => (StateChange)new ActionRecordCleared(u.Id)).ToList();

    protected virtual IReadOnlyList<StateChange> ShapeRoundStartChanges(GameState state, int round) => [];
    protected virtual IReadOnlyList<StateChange> ShapeTurnEndChanges(GameState state, TurnContext turn) => [];
    /// <summary>Events a shape emits when a turn (round one on) has opened and no Ask exists yet — the player-turn shape's first phase_started.</summary>
    protected virtual IReadOnlyList<EventPayload> BeforeFirstAsk(GameState state, SchedulerLog log, Guid teamId) => [];
    /// <summary>The shape's last word on a turn Ask from round one on.</summary>
    protected virtual TurnAsk ShapeAsk(GameState state, TurnAsk ask) => ask;
    /// <summary>A turn opened (by rotation or the menu) on a team with nothing legal: the turn ends unactivated.</summary>
    protected virtual IReadOnlyList<EventPayload> OnEmpty(GameState state, SchedulerLog log, TurnContext turn) => EndTurn(state, turn, passed: false);
    protected abstract IReadOnlyList<EventPayload> OnPassed(GameState state, SchedulerLog log, TurnContext turn);
    protected abstract IReadOnlyList<EventPayload> OnComplete(GameState state, SchedulerLog log, TurnContext turn);
}
