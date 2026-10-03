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
                case Expectation.Outcome(var kind) when outcome.Kind != kind:
                    failures.Add($"expected outcome '{kind}', got '{outcome.Kind}': {outcome}");
                    break;
            }
        }
        return failures;
    }

    /// <summary>Models in play: <c>Location is not OnShelf</c>, the engine's own test (Scheduler/TeamUnits.cs).
    /// Casualties and set-aside units are on the shelf; a model inside a building or transport still counts.</summary>
    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, int teamOrdinal) =>
        ModelsOnTable(projection, scenario, scenario.Info.Teams.Single(t => t.Ordinal == teamOrdinal).Id);

    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, Guid teamId) =>
        projection.Models.Values.Count(m =>
            m.Location is not OnShelf && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) == teamId);
}
