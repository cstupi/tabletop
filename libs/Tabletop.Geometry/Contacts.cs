namespace Tabletop.Geometry;

/// <summary>What a landing meets: another model's base, a model every path within the allowance would cross, another unit's model within the
/// Ask's Separation, or impassable terrain under its base.</summary>
public enum ContactKind { OnBase, Crosses, TooClose, InImpassable }

/// <summary>A landing's first contact, in that order; UnitId is the unit met (null for a path).</summary>
public sealed record Contact(ContactKind Kind, Guid? UnitId);

/// <summary>A model on the table or proposed: its unit, body and where it stands (null off the table).</summary>
public sealed record ContactBody(Guid Id, Guid UnitId, ModelBody Body, Placement? At);

/// <summary>A move Ask's measuring terms (MoveRegion's Allowance, ImpassableVolumeIds, DifficultVolumeIds, DifficultCap, SteepVolumeIds).</summary>
public sealed record ContactMove(double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult, double? DifficultCap, IReadOnlyList<Guid>? Steep = null);

/// <summary>Separation, PassesModels, whether a landing in Impassable terrain counts, the move (null for a placement), the table, the capped allowance.</summary>
public sealed record ContactTerms(double? Separation, bool PassesModels, bool Landing, ContactMove? Move = null, TableSize? Size = null, double? Unit = null);

public static class Contacts
{
    /// <summary>Each proposed model's first contact, in <see cref="ContactKind"/> order.</summary>
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
        var found = new Dictionary<Guid, Contact>();
        foreach (var (id, at) in landed)
        {
            var m = byId[id]; var start = Standing(m);
            if (start is { } s && s.Placement.Position.Horizontal.DistanceTo(at.Placement.Position.Horizontal) <= 1e-6) continue;   // answered where it stands: not moved
            if (everyone.FirstOrDefault(o => o.Id != id && Overlap.Bases(at, o.At)) is { Id: var hit } && hit != Guid.Empty)
            { found[id] = new Contact(ContactKind.OnBase, byId[hit].UnitId); continue; }
            if (impassable.Count > 0 && Movement.Check(at, at.Placement.Position.Horizontal, 0, impassable, [], null, null).CrossesImpassable)
            { found[id] = new Contact(ContactKind.InImpassable, null); continue; }
            // A unit the cap holds measures the shortest way, whatever it enters (the server's DifficultCapBreach); else round difficult ground first.
            var capped = unit is { } u && u < legal?.Allowance;
            if (!passesModels && legal is not null && start is { } from && Movement.Check(from, at.Placement.Position.Horizontal, Math.Max(0, capped ? unit!.Value : legal.Allowance),
                    volumes.Where(v => legal.Impassable.Contains(v.Id)).ToList(), capped ? [] : volumes.Where(v => legal.Difficult.Contains(v.Id)).ToList(),
                    capped ? null : legal.DifficultCap, size, bodies, volumes,
                    steep: capped || legal.Steep is not { Count: > 0 } ? null : volumes.Where(v => legal.Steep.Contains(v.Id)).ToList()).CrossesModels)
            { found[id] = new Contact(ContactKind.Crosses, null); continue; }
            if (separation is not { } gap) continue;
            var near = start is { } was ? models.Where(o => o.UnitId != m.UnitId && Standing(o) is { } there && Closer(was, there, gap)).Select(o => o.UnitId).ToHashSet() : [];
            if (everyone.FirstOrDefault(o => o.UnitId != m.UnitId && !near.Contains(o.UnitId) && Closer(at, o.At, gap)) is { Id: var close } && close != Guid.Empty)
                found[id] = new Contact(ContactKind.TooClose, byId[close].UnitId);
        }
        return found;
    }

    /// <summary>Body to body under `gap`; bases farther apart in plan than that are never measured (the plan gap is no more than the 3D one).</summary>
    static bool Closer(PlacedModel a, PlacedModel b, double gap) => !Bodies.Apart(a, b, gap) && Distance.BodyToBody(a, b) < gap - 1e-6;
}
