using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a landing meets: another model's base, a model on its straight path, or another unit's model within the Ask's Separation.</summary>
public enum ContactKind { OnBase, Crosses, TooClose }

/// <summary>A landing's first contact, in that order; UnitId is the unit met (null for a path).</summary>
public sealed record Contact(ContactKind Kind, Guid? UnitId);

/// <summary>
/// The contacts a drag shows before the server judges it: the same geometry the server measures a move answer and a hand placement with
/// (Overlap.Bases, Collision.Crosses, Distance.BodyToBody, each landing rested by Collision.RestHeight), taken from the Ask's own parameters
/// (Separation, PassesModels). The server stays the judge: a unit a mover already stood within the separation of is left to it.
/// </summary>
public static class ContactCheck
{
    /// <summary>A move answer's landings: on a base, through a model unless the region passes models, within its Separation.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Move(Projection p, MoveRegion legal, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes) =>
        Of(p, proposed, volumes, legal.Separation, legal.PassesModels);

    /// <summary>A hand placement's landings (a deployment, a hand move): on a base only.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Placement(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes) =>
        Of(p, proposed, volumes, null, passesModels: true);

    public static string Words(Contact c, double? separation, Func<Guid, string> unitName) => c.Kind switch
    {
        ContactKind.OnBase => "overlaps a base",
        ContactKind.Crosses => "path crosses a model",
        _ => $"within {separation ?? 0:0.#}\" of {(c.UnitId is { } u ? unitName(u) : "another unit")}",
    };

    /// <summary>Each contact as words, by model.</summary>
    public static IReadOnlyDictionary<Guid, string> Words(IReadOnlyDictionary<Guid, Contact> found, double? separation, Func<Guid, string> unitName) =>
        found.ToDictionary(kv => kv.Key, kv => Words(kv.Value, separation, unitName));

    static IReadOnlyDictionary<Guid, Contact> Of(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes, double? separation, bool passesModels)
    {
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
            if (!passesModels && start is { } from && Collision.Crosses(from, at.Placement.Position.Horizontal, bodies, volumes))
            { found[id] = new Contact(ContactKind.Crosses, null); continue; }
            if (separation is not { } gap) continue;
            var near = start is { } was ? p.Models.Values.Where(o => o.UnitId != m.UnitId && Standing(o) is { } there && Distance.BodyToBody(was, there) < gap - 1e-6).Select(o => o.UnitId).ToHashSet() : [];
            if (everyone.FirstOrDefault(o => o.UnitId != m.UnitId && !near.Contains(o.UnitId) && Distance.BodyToBody(at, o.At) < gap - 1e-6) is { Id: var close } && close != Guid.Empty)
                found[id] = new Contact(ContactKind.TooClose, p.Models[close].UnitId);
        }
        return found;
    }
}
