namespace Tabletop.Geometry;

/// <summary>
/// The geometric facts rulebook p.11's cover rule is built from. The majority rule and the modifier are the ruleset's.
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
        foreach (var target in LineOfSight.TargetPoints(to))
            foreach (var id in LineOfSight.Obstructions(eye, target, blockers))
                seen.Add(id);
        return volumes.Where(v => seen.Contains(v.Id)).Select(v => v.Id).ToList();
    }

    /// <summary>
    /// Cover-declared volumes that do not block line of sight (a wall or fence, a barricade, a field) which at least one of the sight
    /// lines <see cref="LineOfSight.Check"/> draws passes through: p.11's "terrain features that models can hide ... behind". Volumes
    /// either model stands in (<see cref="Containment.VolumesContaining"/>: base centre over the footprint, standing on a Solid's top included)
    /// are left out, since that is the inside test, and each line stops <see cref="Contact"/> short of the target so a base touching the
    /// near face is not behind it. Cover that blocks line of sight is <see cref="SightBlockersBetween"/>'s. In input order.
    /// </summary>
    public static IReadOnlyList<Guid> CoverTerrainBetween(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume> volumes)
    {
        var cover = volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Cover) && !v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight)).ToList();
        if (cover.Count == 0) return [];
        var standing = Containment.VolumesContaining(from, cover).Concat(Containment.VolumesContaining(to, cover)).ToHashSet();
        var eye = from.Eye();
        var ends = LineOfSight.TargetPoints(to).Select(t => ShortOf(eye, t)).ToList();
        return cover.Where(v => !standing.Contains(v.Id) && ends.Any(t => v.IntersectsSegment(eye, t))).Select(v => v.Id).ToList();
    }

    /// <summary>How far short of the target point a cover line stops (inches), so a base in contact with cover on the shooter's side does not count as behind it.</summary>
    public const double Contact = 0.01;

    static Point3 ShortOf(Point3 eye, Point3 target)
    {
        var (dx, dy, dz) = (eye.X - target.X, eye.Y - target.Y, eye.Z - target.Z);
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length <= Contact) return eye;
        var k = Contact / length;
        return new Point3(target.X + dx * k, target.Y + dy * k, target.Z + dz * k);
    }
}
