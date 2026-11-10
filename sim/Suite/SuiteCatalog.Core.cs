using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Core() => [CoreGames.FourActions().BothEditions(), CoreGames.FiringLine().BothEditions(), CoreGames.ChargeAndStrikeBack(), CoreGames.MoraleBreak(), CoreGames.RoughGround(), CoreGames.Ruins(),
        CoreGames.CoverAndSight().BothEditions(), CoreGames.FenceCover().BothEditions(), CoreGames.DenseRuins().BothEditions(), CoreGames.TransportRun().BothEditions(), CoreGames.TransportDeploy().BothEditions(),
        CoreGames.Overkill().BothEditions(), CoreGames.HoldTheMarkers().BothEditions(), CoreGames.Deployment(), CoreGames.OverALowerModel().BothEditions(), CoreGames.RoundTheTerrain().BothEditions(), CoreGames.OffTheHill().BothEditions(),
        CoreGames.PerimeterSight().BothEditions(), CoreGames.PerModelSight().BothEditions(), CoreGames.HybridSight().BothEditions(), CoreGames.MeleeNoSeparation().BothEditions(), CoreGames.SelfPlay().BothEditions(), CoreGames.TeamGame().BothEditions(), CoreGames.FourTeams().BothEditions(), CoreGames.MoveDangers().BothEditions(), .. CoreGames.All36()];
}

/// <summary>
/// The core group's games (rulebook pp.4–12). The policy plays them; layouts, goals and seeds bring each rule into play, and dice fixes
/// set the rolls an exact check reads. Every expected value is a literal from the rule text, quoted with its page.
/// </summary>
internal static partial class CoreGames
{
    const string Group = SuiteCatalog.CoreGroup;

    static ScenarioWeapon Rifle(int attacks = 1, string name = "Rifle") => new(name, new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = $"{attacks}" }, []);
    static ScenarioWeapon HandWeapon(int attacks = 1) => new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, []);

    /// <summary>Each model would rather stand at the nearest enemy model: the legal destination nearest it wins, so the armies close and fight.</summary>
    public static Point2? NearestEnemy(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed || view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => view.TeamOf(m.UnitId) is { } t && t != team)
            .Select(m => view.PlacedOf(m.Id)?.Placement.Position.Horizontal)
            .Where(p => p is not null)
            .MinBy(p => p!.Value.DistanceTo(at));
    }

    static TestGame Game(string name, Scenario scenario, IReadOnlyList<GameCheck> checks, IReadOnlyList<string>? covers = null, IReadOnlyList<DiceFix>? dice = null,
        IReadOnlyDictionary<string, Func<IReadOnlyList<GameEvent>, bool>>? evidence = null) =>
        new(name, Group, scenario, covers ?? [], dice ?? [], [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), .. checks], evidence);

    /// <summary>A check over the run read as frames, steps, dice and positions.</summary>
    static GameCheck Check(string name, Func<CoreLog, string?> failure, params string[] entries) =>
        new(name, run => failure(new CoreLog(run)), entries.Length == 0 ? null : entries);

    /// <summary>
    /// A round-1 charge (each declared from out of contact with a target it can reach, by construction) moves: its move Ask offers no pass
    /// and is answered by a move. A later charge may start in contact, where staying put is the move. The quote is the edition's p.9 sentence.
    /// </summary>
    static GameCheck ChargeMoves(string page) => Check($"{page}: \"Charging models must move by up to 12” to get into base contact with an enemy model from the target unit\" (the charge move Ask offers no pass)", log =>
    {
        var closed = log.Events.Select(e => e.Payload).OfType<AskClosed>().ToDictionary(c => c.AskId, c => c.Answers.FirstOrDefault()?.Answer);
        return Require(log.Events.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask)
            .Where(a => a.Legal is MoveRegion { ContactWith.Count: > 0 } && log.Frame(a.FrameId) is { Sequence: "charge", Round: 1 }).ToList(), "no round-1 charge move Ask", a =>
            a.AllowPass ? $"the charge move Ask {a.Id} offered a pass"
            : closed.GetValueOrDefault(a.Id) is Destinations ? null : $"the charge move Ask {a.Id} was answered with {closed.GetValueOrDefault(a.Id)?.GetType().Name ?? "nothing"}");
    });

    /// <summary>
    /// p.7: "Advance - Moves 6"", "Rush - Moves 12"", "Charge - Moves 12" into melee"; p.9: the target unit's models "must move by up to 3"";
    /// p.10: "the other unit may move by up to 3"" when one was destroyed, else "the charging unit must move back by 1"". The offered allowance
    /// and every model's displacement on the table.
    /// </summary>
    static GameCheck MoveAllowances() => Check("p.7, p.9, p.10: move allowances (Advance 6\", Rush 12\", Charge 12\", close-in 3\", consolidation 3\" or 1\")", log =>
    {
        var seen = new HashSet<string>();
        foreach (var f in log.Frames)
            foreach (var s in f.Steps)
            {
                double? expected = (f.Sequence, s.Step.Step) switch
                {
                    ("advance", "move") => 6, ("rush", "move") => 12, ("charge", "move") => 12, ("charge", "defender-closes") => 3,
                    ("melee", "consolidate") => log.UnitDestroyedBefore(f.Actor!.Value, s.Seq) || log.UnitDestroyedBefore(f.Targets[0], s.Seq) ? 3 : 1,
                    _ => null,
                };
                if (expected is not { } allowance) continue;
                seen.Add(f.Sequence);
                foreach (var ask in s.Asks.Where(a => a.Question == "move"))
                    if (ask.Legal is MoveRegion { Containment: null } m && Math.Abs(m.Allowance - allowance) > 1e-9)
                        return $"{f.Sequence} {s.Step.Step} offered {m.Allowance}\", expected {allowance}\"";
                foreach (var move in Moved(s.Step))
                    if (move is { From: OnTable from, To: OnTable to } && from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal) > allowance + 1e-6)
                        return $"{f.Sequence} {s.Step.Step} moved a model {from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal):0.00}\", allowance {allowance}\"";
            }
        return new[] { "advance", "rush", "charge", "melee" }.Except(seen).ToList() is { Count: > 0 } missing ? $"no {string.Join(", ", missing)} move was made" : null;
    });

    /// <summary>p.7: "After 4 rounds have been played the game ends" — strict ends it, warn asks every team End or Continue and (the game scripted to
    /// continue) starts round 5 with a warning, silent starts it quietly.</summary>
    static GameCheck FinalRound(Enforcement enforcement) => Check($"p.7: \"After 4 rounds have been played the game ends\" ({enforcement.ToString().ToLowerInvariant()})", log =>
    {
        var fifth = log.Events.FirstOrDefault(e => e.Payload is RoundStarted { Round: 5 });
        var ended = log.Events.FirstOrDefault(e => e.Payload is GameEnded);
        var asked = log.Events.FirstOrDefault(e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver });
        if (!log.Events.Any(e => e.Payload is RoundStarted { Round: 4 })) return "round 4 never started";
        return enforcement switch
        {
            Enforcement.Strict => ended is null ? "no game_ended" : fifth is not null ? "round 5 started" : asked is not null ? "game_over was asked" : null,
            Enforcement.Warn => ended is not null ? "the game ended" : asked is null ? "game_over was never asked" : fifth is null ? "round 5 never started"
                : fifth.Seq < asked.Seq ? "round 5 started before game_over was asked" : fifth.Warning is null ? "round 5 started with no warning" : null,
            _ => asked is not null ? "game_over was asked" : ended is not null ? "the game ended" : fifth is null ? "round 5 never started" : fifth.Warning is not null ? $"round 5 warned: {fifth.Warning}" : null,
        };
    });

    /// <summary>
    /// p.4: "If you score the model's Quality value or higher, then it counts as a success, else it counts as a fail"; p.5: "the new value counts
    /// as the final result, however a roll of 6 always counts as a success and a roll of 1 always counts as a fail". Every hit and defense die.
    /// </summary>
    static GameCheck ScoresAgainstThreshold() => Check("p.4: \"If you score the model's Quality value or higher, then it counts as a success\"; p.5: \"a roll of 6 always counts as a success and a roll of 1 always counts as a fail\"", log =>
        Require(log.Rolls("hit").Concat(log.Rolls("defense")).ToList(), "no hit or defense roll", r =>
            log.Dice(r.Roll).FirstOrDefault(d => d.Success != (d.Raw == 6 || (d.Raw != 1 && d.Modified >= r.Roll.Threshold))) is { } bad
                ? $"{r.Roll.Roll} at {r.Roll.Threshold}+: raw {bad.Raw}, final {bad.Modified}, success {bad.Success}" : null));

    /// <summary>A fixed roll's first dice read exactly these faces and outcomes ("4+,3-": a 4 that succeeds, a 3 that fails); more checks what followed from it.</summary>
    static GameCheck FixedRoll(string name, string kind, int ordinal, string faces, Func<CoreLog, (RollRecord Roll, CoreFrame Frame), string?>? more = null) => Check(name, log =>
    {
        if (log.Edited(kind, ordinal) is not { } dice) return $"{kind} #{ordinal} was not edited";
        if (Faces([.. dice.Take(faces.Split(',').Length)]) != $"[{faces}]") return $"{kind} #{ordinal} reads {Faces(dice)}, expected [{faces}, ...]";
        return more?.Invoke(log, log.Rolls(kind)[ordinal]);
    });

    /// <summary>The defense dice rolled for a hit roll's hits: its attack's resolve-hits child.</summary>
    static int DefenseDiceAfter(CoreLog log, CoreFrame attack) =>
        log.Children(attack).Where(c => c.Sequence == "resolve-hits").SelectMany(c => c.Steps).SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "defense").Sum(r => r.Dice.Count);

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------

    static IEnumerable<ModelRelocation> Moved(StepCompleted step) => step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves);

    static string Faces(IReadOnlyList<Die> dice) => $"[{string.Join(",", dice.Select(d => $"{d.Raw}{(d.Success ? "+" : "-")}"))}]";

    /// <summary>Every item passes, and there is at least one: a check with nothing to look at fails rather than passing vacuously.</summary>
    static string? Require<T>(IReadOnlyList<T> items, string none, Func<T, string?> failure) =>
        items.Count == 0 ? none : items.Select(failure).FirstOrDefault(f => f is not null);
}
