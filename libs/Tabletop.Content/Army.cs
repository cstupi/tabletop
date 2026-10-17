using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A property entry on a unit, slot or weapon: a catalog name and its zero-or-one integer parameter.</summary>
public sealed record PropertyInstance(string Name, int? Parameter);

/// <summary>Lives on the model slot. A melee weapon has no Range stat at all.</summary>
public sealed record Weapon(Guid Id, string Name, IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties);

/// <summary>One model. Base and Height are the hitbox; AssetId is appearance and a soft reference. ArmyBook is the Army Forge book uid
/// of the entry the slot was imported from (`armyId` in the TTS export); p.13 picks a caster's spells from it. Null when unknown.</summary>
public sealed record ModelSlot(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    Base Base, double Height, Guid? AssetId,
    IReadOnlyList<Weapon> Weapons, string? ArmyBook = null);

/// <summary>Reserve is set on a unit the army holds off the table until a property places it (null for every other unit).</summary>
public sealed record Unit(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    IReadOnlyList<ModelSlot> Models, UnitReserve? Reserve = null);

/// <summary>What places a reserve unit: Property, a property name held at Of, which is a model slot id or a unit id of the same army.</summary>
public sealed record UnitReserve(string Property, Guid Of);

/// <summary>GameSystem is the GameRules identifier; Source is where it was imported from.</summary>
public sealed record Army(Guid Id, string Name, string GameSystem, string Source, IReadOnlyList<Unit> Units);

/// <summary>
/// The army as it stood when setup ended: name, game system and the whole unit tree, with no edge back to the army and no Source.
/// Unit and slot ids are the ids in play. Every type below is an immutable record, so <c>Of</c> shares the list rather than cloning
/// it — the copy that matters is the serialised one, written once to <c>participant.army_snapshot</c> and never updated, which is
/// what stops an edit to a saved army reaching into a live match (content values → ARMYSNAPSHOT is a deep copy).
/// </summary>
public sealed record ArmySnapshot(Guid Id, string Name, string GameSystem, IReadOnlyList<Unit> Units)
{
    public static ArmySnapshot Of(Army army, Guid id) => new(id, army.Name, army.GameSystem, army.Units);
}
