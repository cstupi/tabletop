using System.Text.Json.Serialization;
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
    IReadOnlyList<Weapon> Weapons, string? ArmyBook = null)
{
    /// <summary>A vehicle: the army list published no base for the entry (Army Forge's `bases` "none" or empty, the model standing on its hull), so
    /// the import gave it the vehicle hull. Both clients draw its move path as treads. Written only when true, so a slot without it reads as before.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Vehicle { get; init; }
}

/// <summary>Reserve is set on a unit the army holds off the table until a property places it (null for every other unit). Points is what the
/// army list says the unit cost, upgrades and joined entries included; null when the list gave none. Nothing in the rules reads it.</summary>
public sealed record Unit(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    IReadOnlyList<ModelSlot> Models, UnitReserve? Reserve = null, int? Points = null)
{
    /// <summary>The names of the official products the army list names for the unit's entries (labels only: never a link, image or description);
    /// null when it named none or the army was imported before they were kept. Only the web mesh picker reads them, to rank its suggestions.</summary>
    public IReadOnlyList<string>? ProductNames { get; init; }
}

/// <summary>What places a reserve unit: Property, a property name held at Of, which is a model slot id or a unit id of the same army.</summary>
public sealed record UnitReserve(string Property, Guid Of);

/// <summary>GameSystem is the GameRules identifier; Source is where it was imported from.</summary>
public sealed record Army(Guid Id, string Name, string GameSystem, string Source, IReadOnlyList<Unit> Units)
{
    /// <summary>One of <see cref="ArmyEditions.All"/>. An init property, so positional construction stays 3.5.</summary>
    public string Edition { get; init; } = ArmyEditions.V35;

    /// <summary>The unit a reserve unit is a copy of: the unit it is reserved of, when that holds the same name and as many models (a rule
    /// that places "a new copy" of a unit); null for any other unit.</summary>
    public Unit? CopiedBy(Unit unit) =>
        unit.Reserve is { } r && Units.FirstOrDefault(u => u.Id == r.Of) is { } of && of.Name == unit.Name && of.Models.Count == unit.Models.Count ? of : null;

    /// <summary>The army with each copy's models on the bodies of the unit it copies, slot for slot: mesh, base and height. A copy is the
    /// same miniatures put back on the table, so only the original is given them.</summary>
    public Army WithCopiesBodied() => this with
    {
        Units = Units.Select(u => CopiedBy(u) is { } of
            ? u with { Models = u.Models.Select((m, i) => m with { AssetId = of.Models[i].AssetId, Base = of.Models[i].Base, Height = of.Models[i].Height }).ToList() }
            : u).ToList(),
    };
}

/// <summary>
/// The army as it stood when setup ended: name, game system and the whole unit tree, with no edge back to the army and no Source.
/// Unit and slot ids are the ids in play. Every type below is an immutable record, so <c>Of</c> shares the list rather than cloning
/// it — the copy that matters is the serialised one, written once to <c>participant.army_snapshot</c> and never updated, which is
/// what stops an edit to a saved army reaching into a live match (content values → ARMYSNAPSHOT is a deep copy).
/// </summary>
public sealed record ArmySnapshot(Guid Id, string Name, string GameSystem, IReadOnlyList<Unit> Units)
{
    /// <summary>One of <see cref="ArmyEditions.All"/>. An init property, so a snapshot written before editions reads as 3.5.</summary>
    public string Edition { get; init; } = ArmyEditions.V35;

    public static ArmySnapshot Of(Army army, Guid id) => new(id, army.Name, army.GameSystem, army.Units) { Edition = army.Edition };
}
