using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The Ordinal-th roll of kind Roll in the game (RollRecord.Roll: hit, defense, morale, casting), counted from zero in log order,
/// gets these faces by SetDice: Faces[i] is die i's raw face. The roll must come from a step its frame outlives (the runner turns the
/// game's step gate on), and a fix that never meets its roll fails the game.
/// </summary>
public sealed record DiceFix(string Roll, int Ordinal, IReadOnlyList<int> Faces);

/// <summary>One check a game makes of its run: Failure returns null for a pass, else the reason.</summary>
public sealed record GameCheck(string Name, Func<SuiteGameRun, string?> Failure);

/// <summary>
/// A themed sim scenario with what it covers (catalog entry names: rules and spells), the dice it fixes and the checks it makes.
/// Group is core, special, army:&lt;book uid&gt; or spells:&lt;book uid&gt;. Evidence: for an entry that acts where no step completes (in the
/// scheduler or in action legality), the game's own predicate over the effective log stands in for Fired.In.
/// </summary>
public sealed record TestGame(string Name, string Group, Scenario Scenario, IReadOnlyList<string> Covers,
    IReadOnlyList<DiceFix> Dice, IReadOnlyList<GameCheck> Checks,
    IReadOnlyDictionary<string, Func<IReadOnlyList<GameEvent>, bool>>? Evidence = null)
{
    /// <summary>Whether the entry fired in this game's effective log: the game's evidence for it when it names some, else Fired.In.</summary>
    public bool FiredIn(IReadOnlyList<GameEvent> effectiveLog, string entry) =>
        Evidence?.TryGetValue(entry, out var evidence) == true
            ? evidence(EventApplier.EffectiveLog(effectiveLog))
            : Suite.Fired.In(effectiveLog, entry);
}

/// <summary>What a game's checks read: the outcome, the effective log (undone ranges removed), the final projection and the sim's invariant failures.</summary>
public sealed record SuiteGameRun(TestGame Game, RunOutcome Outcome, IReadOnlyList<GameEvent> EffectiveLog,
    Projection Final, IReadOnlyList<string> InvariantFailures);
