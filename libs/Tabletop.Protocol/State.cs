using System.Text.Json.Serialization;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>Where a model is: on the table, on its owner's shelf, or inside a container (a model or terrain piece).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OnTable), "table")]
[JsonDerivedType(typeof(OnShelf), "shelf")]
[JsonDerivedType(typeof(Inside), "inside")]
public abstract record Location;
public sealed record OnTable(Placement Placement) : Location;
public sealed record OnShelf : Location;
public sealed record Inside(Guid ContainerId) : Location;

/// <summary>One model changing place. Deployment, a hand move, a casualty and disembarking are all this.</summary>
public sealed record ModelRelocation(Guid ModelId, Location From, Location To);

public enum SubjectKind { Unit, Model }
public sealed record Subject(SubjectKind Kind, Guid Id);

/// <summary>A once-effect written onto a unit; consumed the first time it applies.</summary>
public sealed record Grant(Guid Id, Guid EffectId, string EntryName, Guid CasterUnitId, int RemainingUses);

public sealed record Die(int Raw, int Modified, bool Success);
public sealed record RollModifier(string Source, int Amount);

/// <summary>
/// A roll as it happened. Threshold is null for the roll-off. RequiredRaw is the raw face a roll judged on alone (null for a roll judged
/// on its modified result), so a reroll or an edit judges the die the way the roll did. Replay applies this; it never re-rolls.
/// </summary>
public sealed record RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice, int? RequiredRaw = null);

/// <summary>What a sequence was invoked with. Action is the action row's name when the sequence is an action.</summary>
public sealed record SequenceContext(Guid? ActorUnitId, IReadOnlyList<Guid> TargetUnitIds, IReadOnlyList<Guid> WeaponIds, string? Action);
