using Tabletop.Protocol;

namespace Tabletop.Sim.Invariants;

/// <summary>
/// Projection is a class, not a record, so it has no structural equality. This is that equality, field by
/// field, reporting every difference rather than the first — a wrong projection usually differs in a pattern.
/// </summary>
public static class ProjectionComparer
{
    public static IReadOnlyList<string> Differences(Projection expected, Projection actual)
    {
        var differences = new List<string>();
        void Check(string what, object? a, object? b)
        {
            if (!Equals(a, b)) differences.Add($"{what}: expected {a}, got {b}");
        }

        Check("LastSeq", expected.LastSeq, actual.LastSeq);
        Check("Scheduler.Round", expected.Scheduler.Round, actual.Scheduler.Round);
        Check("Scheduler.Phase", expected.Scheduler.Phase, actual.Scheduler.Phase);
        Check("Scheduler.ActiveTeamId", expected.Scheduler.ActiveTeamId, actual.Scheduler.ActiveTeamId);
        Check("Scheduler.Order", string.Join(",", expected.Scheduler.Order), string.Join(",", actual.Scheduler.Order));
        Check("Scheduler.Passed", string.Join(",", expected.Scheduler.Passed.Order()), string.Join(",", actual.Scheduler.Passed.Order()));
        Check("Stack", string.Join(",", expected.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")),
                        string.Join(",", actual.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")));
        Check("OpenAsks", string.Join(",", expected.OpenAsks.Keys.Order()), string.Join(",", actual.OpenAsks.Keys.Order()));
        Check("Markers", string.Join(",", expected.Markers.Keys.Order()), string.Join(",", actual.Markers.Keys.Order()));
        Check("Scores", string.Join(",", expected.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")),
                        string.Join(",", actual.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")));
        Check("Control", string.Join(",", expected.Control.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value:N}")),
                         string.Join(",", actual.Control.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value:N}")));
        static string? EndOf(Projection p) => p.Ended is { } e ? $"{e.Reason}|{string.Join(",", e.Winners)}" : null;
        Check("Ended", EndOf(expected), EndOf(actual));

        foreach (var id in expected.Models.Keys.Union(actual.Models.Keys).Order())
        {
            if (!expected.Models.TryGetValue(id, out var a) || !actual.Models.TryGetValue(id, out var b))
            { differences.Add($"model {id:N} is in one projection only"); continue; }
            Check($"model {id:N} Location", a.Location, b.Location);
            Check($"model {id:N} Wounds", a.Wounds, b.Wounds);
            Check($"model {id:N} SpellTokens", a.SpellTokens, b.SpellTokens);
            Check($"model {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
        }

        foreach (var id in expected.Units.Keys.Union(actual.Units.Keys).Order())
        {
            if (!expected.Units.TryGetValue(id, out var a) || !actual.Units.TryGetValue(id, out var b))
            { differences.Add($"unit {id:N} is in one projection only"); continue; }
            Check($"unit {id:N} Actions", string.Join(",", a.Actions.Order()), string.Join(",", b.Actions.Order()));
            Check($"unit {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
            Check($"unit {id:N} Grants", string.Join(",", a.Grants.Select(g => g.Id).Order()), string.Join(",", b.Grants.Select(g => g.Id).Order()));
            Check($"unit {id:N} WoundsThisActivation", a.WoundsThisActivation, b.WoundsThisActivation);
        }
        return differences;
    }
}
