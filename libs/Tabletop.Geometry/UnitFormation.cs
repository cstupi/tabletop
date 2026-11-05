namespace Tabletop.Geometry;

/// <summary>
/// What one move may do, as the move rules judge each model's: its allowance along the path its base takes round the impassable volumes and the
/// standing models (none when it passes models), the difficult volumes and their cap (held over the whole unit once one path enters, as p.11
/// holds it), the table, a landing on no other base and in no landing volume, and the unit's coherency after it (a chain within `Chain`, or
/// `Elevated` between models at different heights, every model within `Spread` of the rest). `Steep` volumes are difficult for a model whose move
/// starts outside them (Movement.SteepEntering).
/// </summary>
public sealed record FormationTerms(double Allowance, IReadOnlyList<WorldVolume> Impassable, IReadOnlyList<WorldVolume> Difficult, double? DifficultCap,
    IReadOnlyList<WorldVolume> Landing, IReadOnlyList<WorldVolume> Volumes, TableSize Table, IReadOnlyList<PlacedModel> Standing, bool PassesModels,
    double Chain, double Elevated, double Spread, IReadOnlyList<WorldVolume>? Steep = null);

/// <summary>
/// A unit moved together: one model to a chosen landing and the rest formed up on it. Deterministic, so whoever asks gets the same answer: the
/// game server asks whether a charge could have reached base contact in coherency (p.9 "or as close as possible, whilst still maintaining unit
/// coherency"), and the simulator's policy moves that way.
/// </summary>
public static class UnitFormation
{
    /// <summary>How far short of touching a trial landing stands, so a rest and float noise never read it as on the base it touches.</summary>
    public const double ContactGap = 0.05;

    /// <summary>The gaps a following model tries beside one already placed, inside the 1" chain.</summary>
    static readonly double[] Gaps = [0.3, 0.7];

    /// <summary>How many lead landings InContact forms up on before it gives up: bounded work for a crowd.</summary>
    const int Attempts = 8;

    /// <summary>
    /// The unit (`unit`, every model on the table, in order) with one model in base contact with a `targets` model and every model's move within
    /// the terms, or null when the attempts find none. Leads are tried nearest a target first, each at the first trial landing in contact
    /// (Movement.ContactPoints, ContactGap clear) its own move reaches, against each target nearest first, up to `Attempts` landings; each is
    /// formed up on (Follow). `members` are the unit's models that do not move, held where they stand for coherency.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Point2>? InContact(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members,
        IReadOnlyList<PlacedModel> targets, FormationTerms t)
    {
        var tries = 0;
        foreach (var lead in unit.OrderBy(u => targets.Min(x => Distance.EdgeToEdge(u.Start, x))))
            foreach (var target in targets.OrderBy(x => Distance.EdgeToEdge(lead.Start, x)))
            {
                if (Movement.ContactDistance(lead.Start, target, t.Allowance, t.Impassable, t.Table) is null) continue;
                foreach (var at in Movement.ContactPoints(lead.Start, target, ContactGap))
                {
                    if (!Fits(lead.Start, at, t, [])) continue;
                    if (Follow(unit, members, lead.Id, at, t) is { } formed) return formed;
                    if (++tries >= Attempts) return null;
                    break;
                }
            }
        return null;
    }

    /// <summary>
    /// The lead at `leadAt`, and each other model in order at the first landing its move reaches: its start moved as the lead moved, else beside a
    /// model already placed (the newest first) or a standing member, at each of Gaps on 16 bearings (Movement.ContactPoints, the side facing the
    /// model first). Null when a model has nowhere, the unit is out of coherency, or the difficult cap over the unit is broken.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Point2>? Follow(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members, Guid lead,
        Point2 leadAt, FormationTerms t)
    {
        var start = unit.Single(u => u.Id == lead).Start;
        var shift = leadAt - start.Placement.Position.Horizontal;
        var result = new Dictionary<Guid, Point2> { [lead] = leadAt };
        var placed = new List<PlacedModel> { Rest(start, leadAt, t) };
        foreach (var (id, from) in unit.Where(u => u.Id != lead))
        {
            var moved = from.Placement.Position.Horizontal + shift;
            Point2? at = Fits(from, moved, t, placed) ? moved : null;
            at ??= ((IEnumerable<PlacedModel>)placed).Reverse().Concat(members)
                .SelectMany(o => Gaps.SelectMany(gap => Movement.ContactPoints(from, o, gap, 16)))
                .Select(p => (Point2?)p).FirstOrDefault(p => Fits(from, p!.Value, t, placed));
            if (at is not { } there) return null;
            result[id] = there;
            placed.Add(Rest(from, there, t));
        }
        if (!Coherency.Check([.. placed, .. members], t.Chain, t.Elevated, t.Spread).Coherent) return null;
        if (t.DifficultCap is { } cap && cap < t.Allowance)
        {
            var paths = unit.Select(u => Path(u.Start, result[u.Id], t, t.Allowance, t.Difficult, cap, t.Steep)).ToList();
            if (paths.Any(p => p.EntersDifficult) && unit.Any(u => Path(u.Start, result[u.Id], t, cap, [], null, null).Distance > cap + Tolerance.Distance)) return null;
        }
        return result;
    }

    static PlacedModel Rest(PlacedModel start, Point2 at, FormationTerms t)
    {
        var there = start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, 0) } };
        return there with { Placement = there.Placement with { Position = new Point3(at.X, at.Y, Collision.RestHeight(there, t.Volumes)) } };
    }

    static MoveCheck Path(PlacedModel start, Point2 to, FormationTerms t, double allowance, IReadOnlyList<WorldVolume> difficult, double? cap, IReadOnlyList<WorldVolume>? steep) =>
        Movement.Check(start, to, allowance, t.Impassable, difficult, cap, t.Table, t.PassesModels ? null : t.Standing, t.Volumes, steep: steep);

    /// <summary>One model's landing: on no standing or placed base, in no landing volume, on a path within the allowance (and cap) that goes round
    /// what it may not pass and stays on the table.</summary>
    static bool Fits(PlacedModel start, Point2 at, FormationTerms t, IReadOnlyList<PlacedModel> placed)
    {
        var landed = Rest(start, at, t);
        if (t.Standing.Any(o => Overlap.Bases(landed, o)) || placed.Any(o => Overlap.Bases(landed, o))) return false;
        if (t.Landing.Count > 0 && Movement.Check(landed, at, 0, t.Landing, [], null, null).CrossesImpassable) return false;
        return Path(start, at, t, t.Allowance, t.Difficult, t.DifficultCap, t.Steep) is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false, CrossesModels: false };
    }
}
