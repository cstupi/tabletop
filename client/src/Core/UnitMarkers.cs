using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Key looks up the theme's icon; Text shows when the theme has none.</summary>
public sealed record MarkerIcon(string? Key, string Text);
public sealed record UnitMarker(Guid UnitId, Point3 Anchor, IReadOnlyList<MarkerIcon> Icons, bool Focus = false);
public sealed record FloatingResult(Guid UnitId, string Text, Point3? At);

/// <summary>Spec → Markers: over each unit on the table, at its models' centroid above the tallest model: activated, the cover shield while the
/// server names the unit in cover where it stands (ModelFacts.InCover, the unit card's badge), and each condition the roster shows (not Bookkeeping's). A unit in focus (selected or hovered) shows every such condition; any other only those the theme has an
/// icon for, so no text chip stands over the table unasked. Per-model marks are ModelMarks.</summary>
public static class UnitMarkers
{
    public const string Cover = "cover";

    public static IReadOnlyList<UnitMarker> Of(Projection p, Func<string, bool> hasIcon, IReadOnlyCollection<Guid>? focus = null, IReadOnlySet<Guid>? inCover = null)
    {
        var list = new List<UnitMarker>();
        foreach (var u in p.Units.Values)
        {
            var on = p.Models.Values.Where(m => m.UnitId == u.Id && m.Location is OnTable).ToList();
            var focused = focus?.Contains(u.Id) == true;
            var icons = new List<MarkerIcon>();
            if (u.Actions.Count > 0) icons.Add(new("activated", "✓"));
            if (inCover?.Contains(u.Id) == true) icons.Add(new(Cover, "Cover"));
            icons.AddRange(u.Conditions.Concat(on.SelectMany(m => m.Conditions)).Where(Bookkeeping.Shown).Where(c => focused || hasIcon(c)).Distinct()
                .Order(StringComparer.Ordinal).Select(c => new MarkerIcon(c, RosterView.Words(c))));
            if (on.Count == 0 || icons.Count == 0) continue;
            var at = on.Select(m => ((OnTable)m.Location).Placement.Position).ToList();
            list.Add(new(u.Id, new Point3(at.Average(q => q.X), at.Average(q => q.Y), on.Max(m => ((OnTable)m.Location).Placement.Position.Z + m.Slot.Height)), icons, focused));
        }
        return list;
    }
}

/// <summary>A marker row where the camera puts it: its screen rectangle, its distance from the camera, and whether its unit is in focus.</summary>
public readonly record struct PlacedMarker(Guid UnitId, HudRect Rect, float Depth, bool Focus);

/// <summary>Which marker rows draw this frame: the focused first, then nearest the camera first; a row that would overlap one already drawn is
/// left out, so rows never stack over each other.</summary>
public static class MarkerDeclutter
{
    public static IReadOnlySet<Guid> Shown(IEnumerable<PlacedMarker> rows)
    {
        var kept = new List<HudRect>(); var shown = new HashSet<Guid>();
        foreach (var r in rows.OrderByDescending(r => r.Focus).ThenBy(r => r.Depth))
        {
            if (!r.Focus && kept.Any(k => k.Overlaps(r.Rect))) continue;
            kept.Add(r.Rect); shown.Add(r.UnitId);
        }
        return shown;
    }
}

/// <summary>Spec → Floating results: a StepCompleted that removes models or sets a condition (not Bookkeeping's) floats a line per unit; a
/// model's wounds float as WoundNumbers over its head. A batch while Session is catching up (a join or a resync) or with an undo floats nothing.</summary>
public sealed class FloatingResults
{
    public const double Seconds = 1.2;

    /// <summary>Results by event seq, shown when the Animator plays that event.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<FloatingResult>> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool catchingUp)
    {
        var shown = new Dictionary<long, IReadOnlyList<FloatingResult>>();
        if (catchingUp || batch.Any(e => e.Payload is Undone)) return shown;
        foreach (var e in batch) if (Of(e.Payload, p) is { Count: > 0 } r) shown[e.Seq] = r;
        return shown;
    }

    IReadOnlyList<FloatingResult> Of(EventPayload payload, Projection p)
    {
        if (payload is not StepCompleted step) return [];
        var changes = step.Changes;
        Guid UnitOf(Guid model) => p.Models[model].UnitId;
        var gone = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => r.From is not OnShelf && r.To is OnShelf).ToList();
        var removed = gone.Select(r => r.ModelId).ToHashSet();
        // A unit wiped out has no centroid left: it floats where its first removed model stood.
        Point3? At(Guid unit) => CameraFocus.Centroid(p, unit) ?? gone.Where(r => UnitOf(r.ModelId) == unit).Select(r => r.From).OfType<OnTable>().Select(o => (Point3?)o.Placement.Position).FirstOrDefault();
        FloatingResult Line(Guid unit, string text) => new(unit, text, At(unit));
        return removed.GroupBy(UnitOf).Select(g => Line(g.Key, $"-{g.Count()} model{(g.Count() == 1 ? "" : "s")}"))
            .Concat(changes.OfType<ConditionSet>().Where(c => c.Present && Bookkeeping.Shown(c.Condition)).Select(c => Line(c.Subject.Kind == SubjectKind.Unit ? c.Subject.Id : UnitOf(c.Subject.Id), RosterView.Words(c.Condition))))
            .ToList();
    }
}
