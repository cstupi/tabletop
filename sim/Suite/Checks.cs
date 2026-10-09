using Tabletop.Protocol;
using Tabletop.Sim.Run;

namespace Tabletop.Sim.Suite;

/// <summary>The checks games compose. None computes a rules outcome: an exact check's expected value is a literal in the game, quoted with its page.</summary>
public static class Checks
{
    /// <summary>The game ended (game_ended) or reached the scenario's round cap.</summary>
    public static GameCheck Ended() => new("ended", run => run.Outcome switch
    {
        RunOutcome.Ended or RunOutcome.RoundLimit => null,
        RunOutcome.Stuck s => $"stuck ({s.Reason}): {s.Detail}",
        RunOutcome.Failed f => $"failed: {f.Detail}",
        var o => $"did not end: {o.Kind}",
    });

    /// <summary>The sim's eight invariants held, and in process the driver's projection matched the server's.</summary>
    public static GameCheck Invariants() => new("invariants", run =>
        run.InvariantFailures.Count == 0 ? null : string.Join("; ", run.InvariantFailures));

    /// <summary>Every entry the game covers fired (TestGame.FiredIn: the game's evidence, else Fired.In).</summary>
    public static GameCheck FiredAll() => new("fired", run =>
        run.Game.Covers.Where(c => !run.Game.FiredIn(run.EffectiveLog, c)).ToList() is { Count: > 0 } silent
            ? $"never fired: {string.Join(", ", silent)}"
            : null);

    /// <summary>An exact check over the effective log; the name quotes the rule and its page.</summary>
    public static GameCheck Event(string name, Func<IReadOnlyList<GameEvent>, string?> failure) =>
        new(name, run => failure(run.EffectiveLog));
}
