using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>
/// The roll-off is a ruleset sequence (FirstTeamDecision.RollOffNeeded names it; OPR's is GfRolls.RollOffSequence) run by the
/// interpreter like any other. The scheduler starts it with no actor and, once its frame has finished, reads the descending order
/// from its step_completed.Values place_1..place_n — team ordinals, best first (scheduler → Deployment; p.50). Replay applies
/// round_started.Order and never reads the values again.
/// It is confirm-gated like any other roll: the sequence is two steps, and the boundary before the second is an
/// every-team Confirm the interpreter opens whatever the wait setting says, so the frame stays open with its dice editable and the
/// order is read only once sequence_finished stands. Waiting is that whole interval. The values live on the step that carries
/// place_1 — the ranking step, which runs after the gate — so an edited die decides the order and no logged step is ever rewritten.
/// </summary>
public static class RollOff
{
    /// <summary>The one key convention between a ruleset's roll-off and this reader: place_1..place_n, team ordinals best first (interfaces needed 5). It also identifies the ranking step among the frame's completed steps.</summary>
    static string Place(int k) => $"place_{k}";

    public abstract record Progress;
    public sealed record Emit(IReadOnlyList<EventPayload> Events) : Progress;
    public sealed record Waiting : Progress;
    public sealed record Resolved(IReadOnlyList<Guid> Order) : Progress;

    /// <summary>Since the last round_started (the whole log before the first): no roll-off → start one; abandoned → start again; started and unfinished → wait, which is where a confirm gate holds it; finished → its order.</summary>
    public static Progress Advance(GameState state, SchedulerLog log, string sequence, ISequenceStarter starter)
    {
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var started = since.OfType<SequenceStarted>().LastOrDefault(s => s.Sequence == sequence);
        if (started is null || since.Any(p => p is SequenceAbandoned a && a.FrameId == started.FrameId))
            return new Emit(starter.Start(state, sequence, new SequenceContext(null, [], [], null)));
        if (!since.Any(p => p is SequenceFinished f && f.FrameId == started.FrameId)) return new Waiting();
        return new Resolved(Order(state, since.OfType<StepCompleted>().LastOrDefault(s => s.FrameId == started.FrameId && s.Values?.ContainsKey(Place(1)) is true)));
    }

    /// <summary>place_1..place_n as team ids; ordinals the values do not name (a hand-edited log; the team list itself cannot change after the flip to playing) follow in ordinal order.
    /// The step handed in is the frame's last completed step carrying place_1 — the ranking step, which the interpreter runs after the confirm gate, over whatever dice stand then.</summary>
    public static IReadOnlyList<Guid> Order(GameState state, StepCompleted? step)
    {
        var teams = state.Info.Teams.OrderBy(t => t.Ordinal).ToList();
        var placed = new List<Guid>();
        for (var k = 1; step?.Values is { } values && values.TryGetValue(Place(k), out var ordinal); k++)
            if (teams.FirstOrDefault(t => t.Ordinal == ordinal) is { } team && !placed.Contains(team.Id)) placed.Add(team.Id);
        return placed.Concat(teams.Select(t => t.Id).Where(t => !placed.Contains(t))).ToList();
    }
}
