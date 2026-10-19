using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a landing meets: another model's base, a model every path within the allowance would cross, another unit's model within the
/// Ask's Separation, or impassable terrain under its base.</summary>
public enum ContactKind { OnBase, Crosses, TooClose, InImpassable }

/// <summary>A landing's first contact, in that order; UnitId is the unit met (null for a path).</summary>
public sealed record Contact(ContactKind Kind, Guid? UnitId);

/// <summary>
/// The contacts a drag shows before the server judges it: the same geometry the server measures a move answer and a hand placement with
/// (Overlap.Bases, Movement.Check's CrossesModels along the path, Distance.BodyToBody, each landing rested by Collision.RestHeight), taken from the Ask's own parameters
/// (Separation, PassesModels). The server stays the judge: a unit a mover already stood within the separation of is left to it.
/// </summary>
public static class ContactCheck
{
    /// <summary>A move answer's landings: on a base, in impassable terrain unless the region ignores terrain (p.11, p.7; every impassable volume on
    /// the table, whatever the Ask lists, as the server's LandsInImpassable: a Flying unit's lists none; an Aircraft's IgnoresTerrain, p.13), through a
    /// model unless the region passes models, within its Separation. A path stays on the `size` table (p.7); `unit` is the unit's allowance when the
    /// cap holds it (MoveRegions.Allowance, p.11), and then the way round the models is held to it.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Move(Projection p, MoveRegion legal, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes,
        TableSize? size = null, double? unit = null) =>
        Of(p, proposed, volumes, legal.Separation, legal.PassesModels, landing: !legal.IgnoresTerrain, legal, size, unit);

    /// <summary>A hand placement's landings (a deployment, a hand move): on a base only.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Placement(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes) =>
        Of(p, proposed, volumes, null, passesModels: true, landing: false);

    public static string Words(Contact c, double? separation, Func<Guid, string> unitName) => c.Kind switch
    {
        ContactKind.OnBase => "overlaps a base",
        ContactKind.Crosses => "path crosses a model",
        ContactKind.InImpassable => "ends in impassable terrain",
        _ => $"within {separation ?? 0:0.#}\" of {(c.UnitId is { } u ? unitName(u) : "another unit")}",
    };

    /// <summary>Each contact as words, by model.</summary>
    public static IReadOnlyDictionary<Guid, string> Words(IReadOnlyDictionary<Guid, Contact> found, double? separation, Func<Guid, string> unitName) =>
        found.ToDictionary(kv => kv.Key, kv => Words(kv.Value, separation, unitName));

    static IReadOnlyDictionary<Guid, Contact> Of(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes, double? separation, bool passesModels, bool landing, MoveRegion? legal = null,
        TableSize? size = null, double? unit = null)
    {
        var impassable = landing ? volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList() : [];
        static PlacedModel Body(ModelInPlay m, Placement at) => new(new ModelBody(m.Slot.Base, m.Slot.Height), at);
        static PlacedModel? Standing(ModelInPlay m) => m.Location is OnTable t ? Body(m, t.Placement) : null;
        var landed = proposed.Where(kv => p.Models.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv =>
        {
            var body = Body(p.Models[kv.Key], kv.Value);
            return Body(p.Models[kv.Key], kv.Value with { Position = kv.Value.Position with { Z = Collision.RestHeight(body, volumes) } });
        });
        var standing = p.Models.Values.Where(m => !landed.ContainsKey(m.Id) && m.Location is OnTable).Select(m => (m.Id, m.UnitId, At: Standing(m)!.Value)).ToList();
        var everyone = standing.Concat(landed.Select(kv => (Id: kv.Key, p.Models[kv.Key].UnitId, At: kv.Value))).ToList();
        var bodies = standing.Select(o => o.At).ToList();
        var found = new Dictionary<Guid, Contact>();
        foreach (var (id, at) in landed)
        {
            var m = p.Models[id]; var start = Standing(m);
            if (start is { } s && s.Placement.Position.Horizontal.DistanceTo(at.Placement.Position.Horizontal) <= 1e-6) continue;   // answered where it stands: not moved
            if (everyone.FirstOrDefault(o => o.Id != id && Overlap.Bases(at, o.At)) is { Id: var hit } && hit != Guid.Empty)
            { found[id] = new Contact(ContactKind.OnBase, p.Models[hit].UnitId); continue; }
            if (impassable.Count > 0 && Movement.Check(at, at.Placement.Position.Horizontal, 0, impassable, [], null, null).CrossesImpassable)
            { found[id] = new Contact(ContactKind.InImpassable, null); continue; }
            // A unit the cap holds measures the shortest way, whatever it enters (the server's DifficultCapBreach); else round difficult ground first.
            var capped = unit is { } u && u < legal?.Allowance;
            if (!passesModels && legal is not null && start is { } from && Movement.Check(from, at.Placement.Position.Horizontal, Math.Max(0, capped ? unit!.Value : legal.Allowance),
                    volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList(), capped ? [] : volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList(),
                    capped ? null : legal.DifficultCap, size, bodies, volumes).CrossesModels)
            { found[id] = new Contact(ContactKind.Crosses, null); continue; }
            if (separation is not { } gap) continue;
            var near = start is { } was ? p.Models.Values.Where(o => o.UnitId != m.UnitId && Standing(o) is { } there && Distance.BodyToBody(was, there) < gap - 1e-6).Select(o => o.UnitId).ToHashSet() : [];
            if (everyone.FirstOrDefault(o => o.UnitId != m.UnitId && !near.Contains(o.UnitId) && Distance.BodyToBody(at, o.At) < gap - 1e-6) is { Id: var close } && close != Guid.Empty)
                found[id] = new Contact(ContactKind.TooClose, p.Models[close].UnitId);
        }
        return found;
    }
}
