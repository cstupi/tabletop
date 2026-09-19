namespace Tabletop.Geometry;

/// <summary>
/// The two geometric facts rulebook p.11's cover rule is built from. The majority rule and the modifier are the ruleset's.
/// </summary>
public static class Cover
{
    /// <summary>Cover-declared volumes the whole base is inside: "fully inside a piece of cover terrain".</summary>
    public static IReadOnlyList<Guid> InsideCoverVolumes(PlacedModel model, IReadOnlyList<WorldVolume> volumes) =>
        Containment.VolumesFullyContaining(model, volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Cover)).ToList());

    /// <summary>
    /// Blocking volumes that stop at least one of the sight lines <see cref="LineOfSight.Check"/> draws, in input order.
    /// Non-empty while Check is clear means "behind a sight blocker".
    /// </summary>
    public static IReadOnlyList<Guid> SightBlockersBetween(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume> volumes)
    {
        var eye = from.Eye();
        var blockers = LineOfSight.Blockers(volumes);
        var seen = new HashSet<Guid>();
        var ordered = new List<Guid>();
        foreach (var target in LineOfSight.TargetPoints(to))
            foreach (var id in LineOfSight.Obstructions(eye, target, blockers))
                if (seen.Add(id)) ordered.Add(id);
        return ordered;
    }
}
