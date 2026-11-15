using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

public sealed partial class AnswerPolicy
{
    // p.7 UNIT COHERENCY and p.11's 3" at different elevation, as the server's move_coherency reads them (GfMoves.CoherencyBreach).
    private const double CoherencyChain = 1, CoherencyElevated = 3, CoherencySpread = 9;

    /// <summary>The gaps a formation tries between a model and the one it stands beside, inside the 1" chain.</summary>
    private static readonly double[] FormationGaps = [0.3, 0.7];

    /// <summary>
    /// Every unit with a model in the region that was in coherency before the move is in coherency after it, its models where the destinations put
    /// them (null: where they stand, or still inside), as the server judges it: a unit with no model on the table before counts as in coherency.
    /// </summary>
    private static bool Coherent(MoveRegion m, PolicyView view, IReadOnlyDictionary<Guid, Point2?> destinations)
    {
        foreach (var unit in m.ModelIds.Select(id => view.ModelOf(id).UnitId).Distinct())
        {
            var members = view.Projection.Models.Values.Where(x => x.UnitId == unit).Select(x => x.Id).ToList();
            var before = members.Select(view.PlacedOf).OfType<PlacedModel>().ToList();
            if (!Check(before)) continue;
            var after = members.Select(id => destinations.GetValueOrDefault(id) is { } d ? Landing(id, view.PlacedOf(id), d, view) : view.PlacedOf(id))
                .OfType<PlacedModel>().ToList();
            if (!Check(after)) return false;
        }
        return true;
    }

    private static bool Check(IReadOnlyList<PlacedModel> models) => Coherency.Check(models, CoherencyChain, CoherencyElevated, CoherencySpread).Coherent;

    /// <summary>
    /// The unit kept in coherency: the lead keeps its landing, and each other model, in ModelIds order, takes the first landing that passes its own
    /// checks (Passes, or a disembark's ExitFits) and Clear: its start moved as the lead moved, else beside a model of its unit already placed
    /// or standing (Movement.ContactPoints at FormationGaps, the side facing the model first); with a goal for the model, the passing one nearest
    /// it. A model with none stands (or stays inside). Null
    /// when the result is still out of coherency or breaks the difficult cap over the unit (p.11), which the caller answers by standing still.
    /// </summary>
    private Dictionary<Guid, Point2?>? FormUp(MoveRegion m, PolicyView view, Guid lead, Point2 leadAt, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, IReadOnlyList<WorldVolume> landing)
    {
        var taken = m.ModelIds.Select(id => (id, At: view.PlacedOf(id))).Where(x => x.At is not null).ToDictionary(x => x.id, x => x.At!.Value);
        if (view.PlacedOf(lead) is { } leadStart && !(Passes(leadStart, leadAt, m, impassable, difficult, view, landing, null) && Clear(lead, leadStart, leadAt, m, view, taken)))
            return null;
        var result = m.ModelIds.ToDictionary(id => id, _ => (Point2?)null);
        result[lead] = leadAt;
        taken[lead] = Landing(lead, view.PlacedOf(lead), leadAt, view);
        var shift = view.PlacedOf(lead) is { } ls ? leadAt - ls.Placement.Position.Horizontal : (Point2?)null;
        var unitOf = m.ModelIds.ToDictionary(id => id, id => view.ModelOf(id).UnitId);
        var standing = view.Projection.Models.Values.Where(x => !m.ModelIds.Contains(x.Id) && unitOf.ContainsValue(x.UnitId))
            .Select(x => (x.Id, x.UnitId, At: view.PlacedOf(x.Id))).Where(x => x.At is not null).Select(x => (x.Id, x.UnitId, At: x.At!.Value)).ToList();
        var placed = new List<(Guid Id, Guid UnitId, PlacedModel At)> { (lead, unitOf[lead], taken[lead]) };
        foreach (var id in m.ModelIds.Where(id => id != lead))
        {
            var start = view.PlacedOf(id);
            // A minimum (p.13 Aircraft) holds a following model as it held the lead: where the table has room for it, at least that far.
            bool Fits(Point2 at) => (start is { } s ? Passes(s, at, m, impassable, difficult, view, landing, null)
                    && (m.MinDistance is not { } min || Movement.FarthestOnTable(s, view.Table).Distance < min || s.Placement.Position.Horizontal.DistanceTo(at) >= min - 1e-6)
                : ExitFits(id, at, m, view, landing)) && Clear(id, start, at, m, view, taken);
            var proxy = start ?? (m.Containment?.ExitingId is { } c && view.PlacedOf(c) is { } container
                ? new PlacedModel(view.BodyOf(id), new Placement(container.Placement.Position, new Yaw(0))) : (PlacedModel?)null);
            IEnumerable<Point2> candidates = shift is { } by && start is { } from ? [from.Placement.Position.Horizontal + by] : [];
            if (proxy is { } p)
                candidates = candidates.Concat(placed.Concat(standing).Where(o => o.UnitId == unitOf[id]).Reverse()
                    .SelectMany(o => FormationGaps.SelectMany(gap => Movement.ContactPoints(p, o.At, gap, 16))));
            if (goal?.Invoke(id, view) is { } target) candidates = candidates.OrderBy(x => x.DistanceTo(target)).ToList();
            var at = candidates.Select(x => (Point2?)x).FirstOrDefault(x => Fits(x!.Value));
            if (at is not { } there) continue;
            result[id] = there;
            taken[id] = Landing(id, start, there, view);
            placed.Add((id, unitOf[id], taken[id]));
        }
        if (!Coherent(m, view, result)) return null;
        if (m.DifficultCap is { } cap && cap < m.Allowance)
        {
            var routes = m.ModelIds.Where(id => view.PlacedOf(id) is not null && result[id] is not null)
                .Select(id => Route(view.PlacedOf(id)!.Value, result[id]!.Value, m, view, m.Allowance, difficult, cap, Standing(m, view))).ToList();
            if (routes.Any(r => r.EntersDifficult) && m.ModelIds.Any(id => view.PlacedOf(id) is { } p && result[id] is { } d
                    && Route(p, d, m, view, cap, [], null, Standing(m, view)).Distance > cap + 1e-6)) return null;
        }
        return result;
    }

    /// <summary>
    /// p.7: "If a model is not in coherency with its unit at the beginning of its activation, then you must use an action so that the model gets
    /// back into coherency", as the server's move_coherency holds a move to it (GfMoves.CoherencyBreach): a unit of the region that stands out of
    /// coherency is brought back when this move can (UnitFormation.Regroup), else one of its models ends nearer the chain (UnitFormation.Closer).
    /// Null when every unit is in coherency or neither is found, and the move is answered as any other.
    /// </summary>
    private static IReadOnlyDictionary<Guid, Point2>? Regrouped(MoveRegion m, PolicyView view)
    {
        foreach (var unit in m.ModelIds.Select(id => view.ModelOf(id).UnitId).Distinct())
        {
            var members = view.Projection.Models.Values.Where(x => x.UnitId == unit).Select(x => x.Id).ToList();
            var before = members.Select(view.PlacedOf).OfType<PlacedModel>().ToList();
            if (before.Count == 0 || Check(before)) continue;
            var movers = m.ModelIds.Where(id => view.ModelOf(id).UnitId == unit).Select(id => (Id: id, Start: view.PlacedOf(id))).Where(c => c.Start is not null)
                .Select(c => (c.Id, Start: c.Start!.Value)).ToList();
            var standing = view.Projection.Models.Keys.Where(x => !m.ModelIds.Contains(x)).Select(x => (Id: x, At: view.PlacedOf(x))).Where(x => x.At is not null).ToList();
            var held = standing.Where(x => view.ModelOf(x.Id).UnitId == unit).Select(x => x.At!.Value).ToList();
            var terms = new FormationTerms(m.Allowance, view.VolumesById(m.ImpassableVolumeIds), view.VolumesById(m.DifficultVolumeIds), m.DifficultCap,
                m.IgnoresTerrain ? [] : view.Volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList(), view.Volumes, view.Table,
                standing.Select(x => x.At!.Value).ToList(), m.PassesModels, CoherencyChain, CoherencyElevated, CoherencySpread, SteepOf(m, view),
                standing.Where(x => view.ModelOf(x.Id).UnitId != unit).Select(x => x.At!.Value).ToList(), m.Separation ?? 0);
            if (UnitFormation.Regroup(movers, held, terms) is { } back)
                return back.Where(kv => movers.First(x => x.Id == kv.Key).Start.Placement.Position.Horizontal.DistanceTo(kv.Value) > 1e-9).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (UnitFormation.Closer(movers, held, terms) is var (id, at)) return new Dictionary<Guid, Point2> { [id] = at };
        }
        return null;
    }

    /// <summary>
    /// p.9 "or as close as possible, whilst still maintaining unit coherency": the charging unit in base contact with a ContactWith model and in
    /// coherency, found as the server's charge_contact looks for it (UnitFormation.InContact over the Ask's own parameters, its ContactVertical
    /// included), each landing at the height it rests at there. Null when there is none.
    /// </summary>
    private static IReadOnlyDictionary<Guid, Point3>? ChargeFormation(MoveRegion m, PolicyView view)
    {
        var chargers = m.ModelIds.Select(id => (Id: id, Start: view.PlacedOf(id))).Where(c => c.Start is not null).Select(c => (c.Id, Start: c.Start!.Value)).ToList();
        var targets = m.ContactWith!.Select(view.PlacedOf).OfType<PlacedModel>().ToList();
        if (chargers.Count == 0 || targets.Count == 0) return null;
        var units = m.ModelIds.Select(id => view.ModelOf(id).UnitId).ToHashSet();
        var standing = view.Projection.Models.Keys.Where(x => !m.ModelIds.Contains(x)).Select(x => (Id: x, At: view.PlacedOf(x))).Where(x => x.At is not null).ToList();
        var terms = new FormationTerms(m.Allowance, view.VolumesById(m.ImpassableVolumeIds), view.VolumesById(m.DifficultVolumeIds), m.DifficultCap,
            m.IgnoresTerrain ? [] : view.Volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList(), view.Volumes, view.Table,
            standing.Select(x => x.At!.Value).ToList(), m.PassesModels, CoherencyChain, CoherencyElevated, CoherencySpread, SteepOf(m, view),
            ContactVertical: m.ContactVertical);
        return UnitFormation.InContact(chargers, standing.Where(x => units.Contains(view.ModelOf(x.Id).UnitId)).Select(x => x.At!.Value).ToList(), targets, terms);
    }

    /// <summary>A disembark's landing (SampleExit's checks): fully within the exit distance of the container, fully on the table, clear of impassable terrain.</summary>
    private static bool ExitFits(Guid id, Point2 at, MoveRegion m, PolicyView view, IReadOnlyList<WorldVolume> landing)
    {
        if (m.Containment is not { ExitingId: { } exiting, ExitWithin: { } within } || view.PlacedOf(exiting) is not { } container) return false;
        var placed = new PlacedModel(view.BodyOf(id), new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
        return Containment.BaseFullyWithin(placed, container, within) && Table.ContainsBase(view.Table, placed) && !Lands(placed, at, landing);
    }
}
