using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One of the replay bar's step buttons: the unit it steps by, which way, the word under its icon and its tooltip.</summary>
public sealed record ReplayStepButton(ReplayUnit Unit, bool Back, string Label, string Icon, string Tip);

/// <summary>A round boundary on the scrubber: the fraction of the log at which the round starts, and its number.</summary>
public sealed record ReplayTick(double At, int Round);

/// <summary>
/// What the replay bar draws (UX redesign part 2 → Replay bar): labelled step buttons, back by round, turn, action and step on the left of
/// Play and forward by step, action, turn and round on its right; a tick per round on the scrubber; the caption, the position's block header.
/// </summary>
public static class ReplayBarView
{
    public static IReadOnlyList<ReplayStepButton> Back { get; } =
    [
        new(ReplayUnit.Round, true, "Round", "skip-back", "Back to the start of this round, or the round before"),
        new(ReplayUnit.Turn, true, "Turn", "skip-back", "Back to the start of this turn, or the turn before"),
        new(ReplayUnit.Action, true, "Action", "skip-back", "Back one action"),
        new(ReplayUnit.Step, true, "Step", "step-back", "Back one step: a player's input and what the rules did with it"),
    ];

    public static IReadOnlyList<ReplayStepButton> Forward { get; } =
    [
        new(ReplayUnit.Step, false, "Step", "step", "Forward one step: a player's input and what the rules did with it"),
        new(ReplayUnit.Action, false, "Action", "skip", "Forward one action"),
        new(ReplayUnit.Turn, false, "Turn", "skip", "Forward to the next turn"),
        new(ReplayUnit.Round, false, "Round", "skip", "Forward to the next round"),
    ];

    /// <summary>Each round_started in the log as a tick just after it (ReplayTimeline's Round stop), by the round it starts.</summary>
    public static IReadOnlyList<ReplayTick> Ticks(IReadOnlyList<GameEvent> log)
    {
        var ticks = new List<ReplayTick>();
        for (var i = 0; i < log.Count; i++)
            if (log[i].Payload is RoundStarted { Round: > 0 } r) ticks.Add(new ReplayTick((double)(i + 1) / log.Count, r.Round));
        return ticks;
    }

    /// <summary>The latest live action's header (CombatLog), `R2 · ` read as `Round 2 · `; else the round, or "Before round 1".</summary>
    public static string Caption(IReadOnlyList<LogEntry> entries, int round)
    {
        if (entries.OfType<ActivationBlock>().LastOrDefault(b => !b.Undone) is { } block)
            return block.Header is ['R', .. var rest] && rest.IndexOf(" · ", StringComparison.Ordinal) is > 0 and var at && int.TryParse(rest[..at], out var n)
                ? $"Round {n}{rest[at..]}" : block.Header;
        return round > 0 ? $"Round {round}" : "Before round 1";
    }
}
