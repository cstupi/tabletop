using Tabletop.Protocol;

namespace Tabletop.Sim.Run;

/// <summary>Why the engine stopped making progress. Every one of these is a bug the simulator exists to find.</summary>
public enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }

/// <summary>
/// Five ends, as spec → What a full game means tabulates. Ended (the game logged game_ended, with its winners), RoundLimit and Stuck
/// are the game ending; Failed is an assertion breach — an unscripted Ask in a script-only run, a failed expectation or a failed
/// invariant — and Paused is the step-and-wait seam. A wipe-out is not an end (p.6).
/// </summary>
public abstract record RunOutcome
{
    /// <summary>One winner is a win, several a tie, none no result.</summary>
    public sealed record Ended(int Round, IReadOnlyList<Guid> Winners) : RunOutcome;
    public sealed record RoundLimit(int Rounds) : RunOutcome;
    public sealed record Stuck(StuckReason Reason, string Detail) : RunOutcome;
    public sealed record Failed(string Detail) : RunOutcome;
    /// <summary>StepAndWait: the loop stopped with this Ask open and answered nothing.</summary>
    public sealed record Paused(Guid AskId, string Question) : RunOutcome;

    public string Kind => this switch
    {
        Ended => "ended",
        RoundLimit => "round_limit",
        Stuck => "stuck",
        Paused => "paused",
        _ => "failed",
    };
}
