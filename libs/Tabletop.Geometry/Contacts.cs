namespace Tabletop.Geometry;

/// <summary>What a landing meets: another model's base, a model or unit every path within the allowance would go through, another unit's model
/// within the Ask's Separation, impassable terrain under its base, or a unit every path within the allowance would pass within the Separation of.</summary>
public enum ContactKind { OnBase, Crosses, TooClose, InImpassable, PassesNear }

/// <summary>A landing's first contact, in that order; UnitId is the unit met (for a path, the unit it goes through or within the Separation of).</summary>
public sealed record Contact(ContactKind Kind, Guid? UnitId);

/// <summary>A model on the table or proposed: its unit, body and where it stands (null off the table).</summary>
public sealed record ContactBody(Guid Id, Guid UnitId, ModelBody Body, Placement? At);

/// <summary>A move Ask's measuring terms (MoveRegion's Allowance, ImpassableVolumeIds, DifficultVolumeIds, DifficultCap, SteepVolumeIds).</summary>
public sealed record ContactMove(double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult, double? DifficultCap, IReadOnlyList<Guid>? Steep = null);

/// <summary>Separation, PassesModels, whether a landing in Impassable terrain counts, the move (null for a placement), the table, the capped allowance.
/// `RoundModels`: a destination it answers true for is reached within the allowance (the capped one, once the unit is capped) by a path round
/// every model not proposed (the client's MoveReach, built once per Ask: MoveRegions.Reaches), so no path is searched for it to rule out Crosses.
/// `Bases`: the units a path is held to as bases, not as perimeters (a charge's target; MoveUnits).</summary>
public sealed record ContactTerms(double? Separation, bool PassesModels, bool Landing, ContactMove? Move = null, TableSize? Size = null, double? Unit = null,
    Func<Guid, Point2, bool>? RoundModels = null, IReadOnlyList<Guid>? Bases = null);

public static class Contacts
{
    /// <summary>Each proposed model's first contact: on a base, in impassable terrain, a landing within the Separation of another unit's model (judged
    /// before the path, so a landing too close is never read as a path through), then the path (Movement.Check with MoveUnits over the bodies' units):
    /// Crosses when it fits only through a unit's perimeter or a model, PassesNear when it fits only within the Separation of a unit.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Of(IReadOnlyList<ContactBody> models, IReadOnlyDictionary<Guid, Placement> proposed,
        IReadOnlyList<WorldVolume> volumes, ContactTerms terms)
    {
        var (separation, passesModels, landing, legal, size, unit) = (terms.Separation, terms.PassesModels, terms.Landing, terms.Move, terms.Size, terms.Unit);
        var byId = models.ToDictionary(m => m.Id);
        // Every model answered where it stands (a move Ask's ghosts as it opens) has no contact: return before settling a landing for each.
        if (proposed.All(kv => !byId.TryGetValue(kv.Key, out var m) || m.At is { } t && t.Position.Horizontal.DistanceTo(kv.Value.Position.Horizontal) <= 1e-6))
            return new Dictionary<Guid, Contact>();
        var impassable = landing ? volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList() : [];
        static PlacedModel Body(ContactBody m, Placement at) => new(m.Body, at);
        static PlacedModel? Standing(ContactBody m) => m.At is { } t ? Body(m, t) : null;
        var landed = proposed.Where(kv => byId.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv =>
        {
            var body = Body(byId[kv.Key], kv.Value);
            return Body(byId[kv.Key], kv.Value with { Position = kv.Value.Position with { Z = Collision.RestNear(body, volumes, kv.Value.Position.Z) } });   // as the server settles it: its open surface nearest the Z given
        });
        var standing = models.Where(m => !landed.ContainsKey(m.Id) && m.At is not null).Select(m => (m.Id, m.UnitId, At: Standing(m)!.Value)).ToList();
        var everyone = standing.Concat(landed.Select(kv => (Id: kv.Key, byId[kv.Key].UnitId, At: kv.Value))).ToList();
        var bodies = standing.Select(o => o.At).ToList();
        var bodyUnits = standing.Select(o => o.UnitId).ToList();
        var found = new Dictionary<Guid, Contact>();
        foreach (var (id, at) in landed)
        {
            var m = byId[id]; var start = Standing(m);
            if (start is { } s && s.Placement.Position.Horizontal.DistanceTo(at.Placement.Position.Horizontal) <= 1e-6) continue;   // answered where it stands: not moved
            if (everyone.FirstOrDefault(o => o.Id != id && Overlap.Bases(at, o.At)) is { Id: var hit } && hit != Guid.Empty)
            { found[id] = new Contact(ContactKind.OnBase, byId[hit].UnitId); continue; }
            if (impassable.Count > 0 && Movement.Check(at, at.Placement.Position.Horizontal, 0, impassable, [], null, null).CrossesImpassable)
            { found[id] = new Contact(ContactKind.InImpassable, null); continue; }
            if (separation is { } gap)
            {
                var near = start is { } was ? models.Where(o => o.UnitId != m.UnitId && Standing(o) is { } there && Closer(was, there, gap)).Select(o => o.UnitId).ToHashSet() : [];
                if (everyone.FirstOrDefault(o => o.UnitId != m.UnitId && !near.Contains(o.UnitId) && Closer(at, o.At, gap)) is { Id: var close } && close != Guid.Empty)
                { found[id] = new Contact(ContactKind.TooClose, byId[close].UnitId); continue; }
            }
            // A unit the cap holds measures the shortest way, whatever it enters (the server's DifficultCapBreach); else round difficult ground first.
            var capped = unit is { } u && u < legal?.Allowance;
            if (passesModels || legal is null || start is not { } from || terms.RoundModels?.Invoke(id, at.Placement.Position.Horizontal) == true) continue;
            var path = Movement.Check(from, at.Placement.Position.Horizontal, Math.Max(0, capped ? unit!.Value : legal.Allowance),
                volumes.Where(v => legal.Impassable.Contains(v.Id)).ToList(), capped ? [] : volumes.Where(v => legal.Difficult.Contains(v.Id)).ToList(),
                capped ? null : legal.DifficultCap, size, bodies, volumes,
                steep: capped || legal.Steep is not { Count: > 0 } ? null : volumes.Where(v => legal.Steep.Contains(v.Id)).ToList(),
                units: new MoveUnits(bodyUnits, m.UnitId, separation, terms.Bases));
            if (path.CrossesModels) found[id] = new Contact(ContactKind.Crosses, path.Unit);
            else if (path.NearModels) found[id] = new Contact(ContactKind.PassesNear, path.Unit);
        }
        return found;
    }

    /// <summary>Body to body under `gap`; bases farther apart in plan than that are never measured (the plan gap is no more than the 3D one).</summary>
    static bool Closer(PlacedModel a, PlacedModel b, double gap) => !Bodies.Apart(a, b, gap) && Distance.BodyToBody(a, b) < gap - 1e-6;
}
