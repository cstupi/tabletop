using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

/// <summary>A scenario's own assertion, checked at the end of a run beside the universal invariants.</summary>
public abstract record Expectation
{
    public sealed record EventOfKind(string Kind, int AtLeast = 1) : Expectation;
    public sealed record NoEventOfKind(string Kind) : Expectation;
    public sealed record ReachedRound(int Round) : Expectation;
    public sealed record TeamWiped(int TeamOrdinal) : Expectation;
    public sealed record ModelsRemaining(int TeamOrdinal, int AtLeast) : Expectation;
    public sealed record AskAsked(string Question, int AtLeast = 1) : Expectation;
    public sealed record Outcome(string Kind) : Expectation;

    /// <summary>Exactly Count effective-log events of Kind carry a warning.</summary>
    public sealed record Warnings(string Kind, int Count) : Expectation;

    /// <summary>The live game_ended names exactly this team as the winner (p.6: most markers after round 4).</summary>
    public sealed record Won(int TeamOrdinal) : Expectation;

    /// <summary>The live game_ended names exactly the teams controlling the most markers in the final projection (p.6: "the player
    /// that controls most markers wins"): one team is a win, several a tie, every team when none holds a marker. Holds for any seed.
    /// Only control of an objective marker in play counts, as in the engine's own tally.</summary>
    public sealed record MostMarkersWin : Expectation;
}

/// <summary>Evaluation of a scenario's own assertions, checked at the end of a run beside the universal invariants.</summary>
public static class Expectations
{
    public static IReadOnlyList<string> Check(
        IReadOnlyList<Expectation> expectations, Projection projection,
        IReadOnlyList<GameEvent> log, Run.RunOutcome outcome, CompiledScenario scenario)
    {
        var failures = new List<string>();
        foreach (var expectation in expectations)
        {
            switch (expectation)
            {
                case Expectation.EventOfKind(var kind, var atLeast):
                    var count = log.Count(e => EventKinds.Of(e.Payload) == kind);
                    if (count < atLeast) failures.Add($"expected at least {atLeast} '{kind}' events, saw {count}");
                    break;
                case Expectation.NoEventOfKind(var kind) when log.Any(e => EventKinds.Of(e.Payload) == kind):
                    failures.Add($"expected no '{kind}' event, saw one at seq {log.First(e => EventKinds.Of(e.Payload) == kind).Seq}");
                    break;
                case Expectation.ReachedRound(var round) when projection.Scheduler.Round < round:
                    failures.Add($"expected round {round}, reached {projection.Scheduler.Round}");
                    break;
                case Expectation.TeamWiped(var ordinal) when ModelsOnTable(projection, scenario, ordinal) > 0:
                    failures.Add($"expected team {ordinal} wiped, it has {ModelsOnTable(projection, scenario, ordinal)} models in play");
                    break;
                case Expectation.ModelsRemaining(var ordinal, var atLeast) when ModelsOnTable(projection, scenario, ordinal) < atLeast:
                    failures.Add($"expected team {ordinal} to keep {atLeast} models, it has {ModelsOnTable(projection, scenario, ordinal)}");
                    break;
                case Expectation.AskAsked(var question, var atLeast):
                    var asked = log.Count(e => e.Payload is AskOpened o && o.Ask.Question == question);
                    if (asked < atLeast) failures.Add($"expected at least {atLeast} '{question}' asks, saw {asked}");
                    break;
                case Expectation.Warnings(var kind, var expected):
                    var warned = EventApplier.EffectiveLog(log).Count(e => EventKinds.Of(e.Payload) == kind && e.Warning is not null);
                    if (warned != expected) failures.Add($"expected {expected} warned '{kind}' events, saw {warned}");
                    break;
                case Expectation.Won(var ordinal):
                    var team = scenario.Info.Teams.Single(t => t.Ordinal == ordinal).Id;
                    var end = EventApplier.EffectiveLog(log).LastOrDefault(e => e.Payload is GameEnded)?.Payload as GameEnded;
                    if (end is null || end.Winners.Count != 1 || end.Winners[0] != team)
                        failures.Add($"expected team {ordinal} to win, got {(end is null ? "no game_ended" : $"winners [{Ordinals(end.Winners, scenario)}]")}");
                    break;
                case Expectation.MostMarkersWin:
                    var ended = EventApplier.EffectiveLog(log).LastOrDefault(e => e.Payload is GameEnded)?.Payload as GameEnded;
                    var objectives = Answers.PolicyView.ObjectiveMarkersOf(projection).Select(m => m.Id).ToHashSet();
                    var held = scenario.Info.Teams.OrderBy(t => t.Ordinal)
                        .Select(t => (t.Ordinal, t.Id, Count: projection.Control.Count(kv => kv.Value == t.Id && objectives.Contains(kv.Key)))).ToList();
                    var most = held.Where(h => h.Count == held.Max(m => m.Count)).Select(h => h.Id).ToList();
                    if (ended is null || !ended.Winners.Order().SequenceEqual(most.Order()))
                        failures.Add($"expected winners [{Ordinals(most, scenario)}] (markers held: {string.Join(", ", held.Select(h => $"team {h.Ordinal}: {h.Count}"))}), " +
                                     $"got {(ended is null ? "no game_ended" : $"winners [{Ordinals(ended.Winners, scenario)}]")}");
                    break;
                case Expectation.Outcome(var kind) when outcome.Kind != kind:
                    failures.Add($"expected outcome '{kind}', got '{outcome.Kind}': {outcome}");
                    break;
            }
        }
        return failures;
    }

    /// <summary>Team ordinals, comma-separated; an id that names no team shows its first eight hex digits.</summary>
    static string Ordinals(IEnumerable<Guid> teams, CompiledScenario scenario) =>
        string.Join(", ", teams.Select(w => scenario.Info.Teams.FirstOrDefault(t => t.Id == w)?.Ordinal.ToString() ?? w.ToString("N")[..8]));

    /// <summary>Models in play: <c>Location is not OnShelf</c>, the engine's own test (Scheduler/TeamUnits.cs).
    /// Casualties and set-aside units are on the shelf; a model inside a building or transport still counts.</summary>
    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, int teamOrdinal) =>
        ModelsOnTable(projection, scenario, scenario.Info.Teams.Single(t => t.Ordinal == teamOrdinal).Id);

    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, Guid teamId) =>
        projection.Models.Values.Count(m =>
            m.Location is not OnShelf && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) == teamId);
}
