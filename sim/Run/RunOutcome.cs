using Tabletop.Protocol;

namespace Tabletop.Sim.Run;

/// <summary>Why the engine stopped making progress. Every one of these is a bug the simulator exists to find.</summary>
public enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }

/// <summary>
/// Five ends, as spec → What a full game means tabulates. Decided, RoundLimit and Stuck are the game
/// ending; Failed is an assertion breach — an unscripted Ask in a script-only run, a failed expectation or a
/// failed invariant — and Paused is the step-and-wait seam of Task 9.
/// </summary>
public abstract record RunOutcome
{
    public sealed record Decided(Guid WinningTeamId) : RunOutcome;
    public sealed record RoundLimit(int Rounds) : RunOutcome;
    public sealed record Stuck(StuckReason Reason, string Detail) : RunOutcome;
    public sealed record Failed(string Detail) : RunOutcome;
    /// <summary>StepAndWait: the loop stopped with this Ask open and answered nothing (Task 9).</summary>
    public sealed record Paused(Guid AskId, string Question) : RunOutcome;

    public string Kind => this switch
    {
        Decided => "decided",
        RoundLimit => "round_limit",
        Stuck => "stuck",
        Paused => "paused",
        _ => "failed",
    };
}
