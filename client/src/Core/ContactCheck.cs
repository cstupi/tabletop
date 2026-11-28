using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The contacts a drag shows before the server judges it: the same geometry the server measures a move answer and a hand placement with
/// (Overlap.Bases, Movement.Check's CrossesModels along the path, Distance.BodyToBody, each landing rested by Collision.RestNear at its Z, as Battlefield.Settle rests it), taken from the Ask's own parameters
/// (Separation, PassesModels). The server stays the judge: a unit a mover already stood within the separation of is left to it.
/// </summary>
public static class ContactCheck
{
    /// <summary>A move answer's landings: on a base, in impassable terrain unless the region ignores terrain (p.11, p.7; every impassable volume on
    /// the table, whatever the Ask lists, as the server's LandsInImpassable: a Flying unit's lists none; an Aircraft's IgnoresTerrain, p.13), through a
    /// model unless the region passes models, within its Separation. A path stays on the `size` table (p.7); `unit` is the unit's allowance when the
    /// cap holds it (MoveRegions.Allowance, p.11), and then the way round the models is held to it.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Move(Projection p, MoveRegion legal, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes,
        TableSize? size = null, double? unit = null, MoveRegions? regions = null) =>
        Of(p, proposed, volumes, legal.Separation, legal.PassesModels, landing: !legal.IgnoresTerrain, legal, size, unit,
            // The Ask's reaches (built once, round the same models) answer most landings: only one they do not reach is searched again, each drag frame.
            regions is null ? null : (id, to) => regions.Reaches(id, to, unit is { } u && u < legal.Allowance ? u : null));

    /// <summary>A hand placement's landings (a deployment, a hand move): on a base only.</summary>
    public static IReadOnlyDictionary<Guid, Contact> Placement(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes) =>
        Of(p, proposed, volumes, null, passesModels: true, landing: false);

    public static string Words(Contact c, double? separation, Func<Guid, string> unitName) => c.Kind switch
    {
        ContactKind.OnBase => "overlaps a base",
        ContactKind.Crosses => c.UnitId is { } through ? $"path crosses {unitName(through)}" : "path crosses a model",
        ContactKind.PassesNear => $"passes within {separation ?? 0:0.#}\" of {(c.UnitId is { } near ? unitName(near) : "another unit")}",
        ContactKind.InImpassable => "ends in impassable terrain",
        _ => $"within {separation ?? 0:0.#}\" of {(c.UnitId is { } u ? unitName(u) : "another unit")}",
    };

    /// <summary>The words as the decision box's sentence about `subject` (the model's name), as the server words its own refusals; words that
    /// are not a contact's stay as they are.</summary>
    public static string Sentence(string words, string subject) => words switch
    {
        "overlaps a base" => $"{subject} would be placed on another model's base",
        "path crosses a model" => $"{subject} would move through another model",
        _ when words.StartsWith("path crosses ", StringComparison.Ordinal) => $"{subject} would move through {words["path crosses ".Length..]}",
        _ when words.StartsWith("passes within ", StringComparison.Ordinal) => $"{subject} would pass {words["passes ".Length..]}",
        "ends in impassable terrain" => $"{subject} would end its move in impassable terrain",
        _ when words.StartsWith("within ", StringComparison.Ordinal) => $"{subject} would end {words}",
        _ when words.StartsWith("placed ", StringComparison.Ordinal) => $"{subject} would be {words}",   // placed within 9" of a unit, beyond 9" of its zone, outside its zone
        _ when words.StartsWith("beyond ", StringComparison.Ordinal) => $"{subject} would be placed {words}",
        "would leave the table" => $"{subject} would leave the table",
        _ => words,
    };

    /// <summary>Each contact as words, by model.</summary>
    public static IReadOnlyDictionary<Guid, string> Words(IReadOnlyDictionary<Guid, Contact> found, double? separation, Func<Guid, string> unitName) =>
        found.ToDictionary(kv => kv.Key, kv => Words(kv.Value, separation, unitName));

    static IReadOnlyDictionary<Guid, Contact> Of(Projection p, IReadOnlyDictionary<Guid, Placement> proposed, IReadOnlyList<WorldVolume> volumes, double? separation, bool passesModels, bool landing, MoveRegion? legal = null,
        TableSize? size = null, double? unit = null, Func<Guid, Point2, bool>? roundModels = null) =>
        Contacts.Of(p.Models.Values.Select(m => new ContactBody(m.Id, m.UnitId, new ModelBody(m.Slot.Base, m.Slot.Height), m.Location is OnTable t ? t.Placement : null)).ToList(), proposed, volumes,
            new ContactTerms(separation, passesModels, landing, legal is null ? null : new ContactMove(legal.Allowance, legal.ImpassableVolumeIds.ToList(), legal.DifficultVolumeIds.ToList(), legal.DifficultCap, legal.SteepVolumeIds?.ToList()), size, unit, roundModels,
                legal?.ContactWith is { Count: > 0 } c ? [.. c.Where(p.Models.ContainsKey).Select(id => p.Models[id].UnitId).Distinct()] : null));
}
