using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The Ordinal-th roll of kind Roll in the game (RollRecord.Roll: hit, defense, morale, casting, or a rule's own roll such as Fearless),
/// counted from zero in log order, gets these faces by SetDice: Faces[i] is die i's raw face. With a Key, only the rolls whose frame the
/// key matches are counted, so the fix names one unit's roll whatever else the game rolled before it. The roll must come from a step its
/// frame outlives (the runner turns the game's step gate on), and a fix that never meets its roll fails the game.
/// </summary>
public sealed record DiceFix(string Roll, int Ordinal, IReadOnlyList<int> Faces, RollKey? Key = null);

/// <summary>
/// Which rolls a DiceFix counts, read from the SequenceContext of the frame that rolled: Actor and Target are unit names (the actor is the
/// shooter or striker in an attack-sequence and the frames under it, the testing unit in a morale-test), Weapon the name of the context's
/// first weapon, Action the action row's name (Hold, Advance, Rush, Charge). A null field matches anything.
/// </summary>
public sealed record RollKey(string? Actor = null, string? Target = null, string? Weapon = null, string? Action = null)
{
    public bool Matches(SequenceContext context, Projection projection)
    {
        string? Unit(Guid id) => projection.Units.TryGetValue(id, out var u) ? u.Definition.Name : null;
        if (Actor is not null && (context.ActorUnitId is not { } actor || Unit(actor) != Actor)) return false;
        if (Target is not null && !context.TargetUnitIds.Any(t => Unit(t) == Target)) return false;
        if (Weapon is not null && (context.WeaponIds.Count == 0
            || projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == context.WeaponIds[0])?.Name != Weapon)) return false;
        return Action is null || context.Action == Action;
    }

    public override string ToString() =>
        string.Join(" ", new[] { Actor is null ? null : $"by {Actor}", Target is null ? null : $"at {Target}", Weapon is null ? null : $"with {Weapon}", Action is null ? null : $"in {Action}" }.OfType<string>());
}

/// <summary>
/// One check a game makes of its run: Failure returns null for a pass, else the reason. Entries: the catalog entries an exact check is
/// about, reported against each entry's coverage; null for a check about no entry. A check built by <see cref="Each"/> can fail for several
/// reasons at once; the runner reports each as its own failure line, so a pin matching one reason never covers another.
/// </summary>
public sealed record GameCheck(string Name, Func<SuiteGameRun, string?> Failure, IReadOnlyList<string>? Entries = null)
{
    /// <summary>Every reason the check failed for, one per failure line; set by <see cref="Each"/>, else Failure's one reason.</summary>
    public Func<SuiteGameRun, IReadOnlyList<string>>? Reasons { get; init; }

    /// <summary>A check whose reasons are reported one per line: it passes when there is none. Failure joins them.</summary>
    public static GameCheck Each(string name, Func<SuiteGameRun, IEnumerable<string>> reasons, IReadOnlyList<string>? entries = null) =>
        new(name, run => reasons(run).ToList() is { Count: > 0 } all ? string.Join("; ", all) : null, entries) { Reasons = run => [.. reasons(run)] };

    /// <summary>The reasons this run fails the check for, empty for a pass.</summary>
    public IReadOnlyList<string> FailuresOf(SuiteGameRun run) => Reasons is { } reasons ? reasons(run) : Failure(run) is { } one ? [one] : [];
}

/// <summary>One exact check's verdict, as an entry's coverage lists it.</summary>
public sealed record ExactResult(string Game, string Check, bool Passed);

/// <summary>
/// A themed sim scenario with what it covers (catalog entry names: rules and spells), the dice it fixes and the checks it makes.
/// Group is core, special, army:&lt;edition&gt;:&lt;book uid&gt; or spells:&lt;edition&gt;:&lt;book uid&gt;. Evidence: for an entry that acts where no step completes (in the
/// scheduler or in action legality), the game's own predicate over the effective log stands in for Fired.In.
/// Pattern: the Army Forge pattern whose generated wrappers this game covers.
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

    /// <summary>The game-rules id this game runs under (RuleSets): 3.5.1 unless the game names one (In36, BothEditions, a book's edition).
    /// SuiteCatalog.Expand sets the scenario's rules to it, and the runner compiles under it.</summary>
    public string Edition { get; init; } = RuleSets.Gf351;

    /// <summary>The editions the game runs under, once each (SuiteCatalog.Expand); null is Edition alone.</summary>
    public IReadOnlyList<string>? Editions { get; init; }

    /// <summary>The engine defects this game is pinned to; empty for a game expected to pass.</summary>
    public IReadOnlyList<KnownDefect> Known { get; init; } = [];

    /// <summary>The pattern this game plays for every generated wrapper of it (army-book rules spec → Coverage): Aura, Buff, Mark or Debuff.</summary>
    public string? Pattern { get; init; }

    /// <summary>This game pinned to defect <paramref name="id"/> for failures that start with <paramref name="check"/> and contain <paramref name="reason"/>.</summary>
    public TestGame Pinned(string id, string check, string reason, DriverKind? driver = null) =>
        this with { Known = [.. Known, new KnownDefect(id, check, reason, driver)] };

    /// <summary>The pins that apply to a run on that driver.</summary>
    public IReadOnlyList<KnownDefect> KnownOn(DriverKind driver) => [.. Known.Where(k => k.Driver is null || k.Driver == driver)];
}

/// <summary>
/// An engine defect a game is pinned to: Id names it (docs/handoff.md → Rules-suite gaps). A failure line is the defect's
/// when it starts with Check (a check's name, or a base failure such as "invariants" or "stuck") and contains Reason, text the defect
/// itself produces, so the same check failing for another reason stays unexpected. Driver, when set, limits the pin to runs on that
/// driver. The game keeps its failing check; the report files that failure under the defect instead of as unexpected.
/// </summary>
public sealed record KnownDefect(string Id, string Check, string Reason, DriverKind? Driver = null)
{
    public bool Matches(string failure) =>
        failure.StartsWith(Check, StringComparison.Ordinal) && failure.Contains(Reason, StringComparison.Ordinal);
}

/// <summary>What a game's checks read: the outcome, the effective log (undone ranges removed), the final projection and the sim's invariant failures.</summary>
public sealed record SuiteGameRun(TestGame Game, RunOutcome Outcome, IReadOnlyList<GameEvent> EffectiveLog,
    Projection Final, IReadOnlyList<string> InvariantFailures)
{
    /// <summary>The verdicts of the game's checks that name entries, by entry; set by the runner once the checks have run.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ExactResult>> Exact { get; init; } = new Dictionary<string, IReadOnlyList<ExactResult>>();
}
