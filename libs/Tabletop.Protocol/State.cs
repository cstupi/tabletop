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

/// <summary>One die. Triggered: what this die set off when it was rolled, `Rule` or `Rule +N hits`, named by the server; null when
/// nothing, in a die a reroll or an edit replaced, and in logs written before it. Equality compares the triggers by value.</summary>
public sealed record Die(int Raw, int Modified, bool Success, IReadOnlyList<string>? Triggered = null)
{
    public bool Equals(Die? other) => other is not null && Raw == other.Raw && Modified == other.Modified && Success == other.Success
        && (Triggered ?? []).SequenceEqual(other.Triggered ?? []);
    public override int GetHashCode() => HashCode.Combine(Raw, Modified, Success, Triggered?.Count ?? 0);
}
/// <summary>A change to a roll's dice. Via: the rule that granted the property whose effect this is (Rending for its AP), or for a
/// player's modifier (Source "player", AddModifier) the participant's name; null for a property held outright and in logs written before it.</summary>
public sealed record RollModifier(string Source, int Amount, string? Via = null);
/// <summary>
/// p.11's cover verdict for the whole target unit of a shooting attack, on each of its defense rolls. InCover: the majority test passed;
/// Models of Of: the living models counted in cover. IgnoredBy: the rule that took the +1 away from a unit that had it (a suppression
/// such as Blast, or Indirect's dropped sight half). The +1 applies when InCover and nothing ignored it.
/// </summary>
public sealed record CoverVerdict(bool InCover, int Models, int Of, string? IgnoredBy = null);

/// <summary>Where a threshold came from: the stat it was read from, the value printed in the slot it was read from, and each
/// replace_stat (SetTo) or modify_stat (Amount) row in the order applied. Null for a threshold no stat gave, and in logs written before it.</summary>
public sealed record ThresholdBasis(string Stat, int Printed, IReadOnlyList<ThresholdChange> Changes);
public sealed record ThresholdChange(string Source, int? SetTo, int Amount);   // SetTo for replace_stat, Amount for modify_stat
/// <summary>
/// A roll as it happened. Threshold is null for the roll-off. RequiredRaw is the raw face a roll judged on alone (null for a roll judged
/// on its modified result), so a reroll or an edit judges the die the way the roll did. Cover is set on a shooting attack's defense rolls
/// and null elsewhere and in logs written before it. Replay applies this; it never re-rolls.
/// </summary>
public sealed record RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice, int? RequiredRaw = null, ThresholdBasis? Basis = null,
    CoverVerdict? Cover = null);

/// <summary>What a sequence was invoked with. Action is the action row's name when the sequence is an action.</summary>
public sealed record SequenceContext(Guid? ActorUnitId, IReadOnlyList<Guid> TargetUnitIds, IReadOnlyList<Guid> WeaponIds, string? Action);
