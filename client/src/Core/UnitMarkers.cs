using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Key looks up the theme's icon; Text shows when the theme has none.</summary>
public sealed record MarkerIcon(string? Key, string Text);
public sealed record UnitMarker(Guid UnitId, Point3 Anchor, IReadOnlyList<MarkerIcon> Icons);
public sealed record FloatingResult(Guid UnitId, string Text, Point3? At);

/// <summary>Spec → Markers: over each unit on the table, at its models' centroid above the tallest model: activated and each condition. Per-model marks are ModelMarks.</summary>
public static class UnitMarkers
{
    public static IReadOnlyList<UnitMarker> Of(Projection p)
    {
        var list = new List<UnitMarker>();
        foreach (var u in p.Units.Values)
        {
            var on = p.Models.Values.Where(m => m.UnitId == u.Id && m.Location is OnTable).ToList();
            var icons = new List<MarkerIcon>();
            if (u.Actions.Count > 0) icons.Add(new("activated", "✓"));
            icons.AddRange(u.Conditions.Concat(on.SelectMany(m => m.Conditions)).Distinct().Order(StringComparer.Ordinal).Select(c => new MarkerIcon(c, RosterView.Words(c))));
            if (on.Count == 0 || icons.Count == 0) continue;
            var at = on.Select(m => ((OnTable)m.Location).Placement.Position).ToList();
            list.Add(new(u.Id, new Point3(at.Average(q => q.X), at.Average(q => q.Y), on.Max(m => ((OnTable)m.Location).Placement.Position.Z + m.Slot.Height)), icons));
        }
        return list;
    }
}

/// <summary>Spec → Floating results: a StepCompleted that removes models, wounds one or sets a condition floats a line per unit. Wounds on the wire are totals, so this
/// keeps each model's last total. A batch while Session is catching up (a join or a resync) or with an undo only re-reads the totals and floats nothing.</summary>
public sealed class FloatingResults
{
    public const double Seconds = 1.2;
    readonly Dictionary<Guid, int> _wounds = new();
    public void Seed(Projection p) { _wounds.Clear(); foreach (var m in p.Models.Values) _wounds[m.Id] = m.Wounds; }
    public void OnBoard() => _wounds.Clear();

    /// <summary>Results by event seq, shown when the Animator plays that event.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<FloatingResult>> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool catchingUp)
    {
        var silent = catchingUp || batch.Any(e => e.Payload is Undone);
        var shown = new Dictionary<long, IReadOnlyList<FloatingResult>>();
        foreach (var e in batch) if (Of(e.Payload, p) is { Count: > 0 } r && !silent) shown[e.Seq] = r;
        if (silent) Seed(p);
        return shown;
    }

    IReadOnlyList<FloatingResult> Of(EventPayload payload, Projection p)
    {
        IReadOnlyList<StateChange> changes = payload switch { StepCompleted s => s.Changes, RoundStarted r => r.Changes, StateChange c => [c], _ => [] };
        Guid UnitOf(Guid model) => p.Models[model].UnitId;
        var gone = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => r.From is not OnShelf && r.To is OnShelf).ToList();
        var removed = gone.Select(r => r.ModelId).ToHashSet();
        var taken = changes.OfType<WoundsSet>().Where(w => !removed.Contains(w.ModelId)).Select(w => (Unit: UnitOf(w.ModelId), Taken: w.Wounds - _wounds.GetValueOrDefault(w.ModelId))).Where(w => w.Taken > 0).ToList();
        foreach (var w in changes.OfType<WoundsSet>()) _wounds[w.ModelId] = w.Wounds;
        if (payload is not StepCompleted) return [];
        // A unit wiped out has no centroid left: it floats where its first removed model stood.
        Point3? At(Guid unit) => CameraFocus.Centroid(p, unit) ?? gone.Where(r => UnitOf(r.ModelId) == unit).Select(r => r.From).OfType<OnTable>().Select(o => (Point3?)o.Placement.Position).FirstOrDefault();
        FloatingResult Line(Guid unit, string text) => new(unit, text, At(unit));
        return removed.GroupBy(UnitOf).Select(g => Line(g.Key, $"-{g.Count()} model{(g.Count() == 1 ? "" : "s")}"))
            .Concat(taken.GroupBy(w => w.Unit).Select(g => { var k = g.Sum(w => w.Taken); return Line(g.Key, $"-{k} wound{(k == 1 ? "" : "s")}"); }))
            .Concat(changes.OfType<ConditionSet>().Where(c => c.Present).Select(c => Line(c.Subject.Kind == SubjectKind.Unit ? c.Subject.Id : UnitOf(c.Subject.Id), RosterView.Words(c.Condition))))
            .ToList();
    }
}
