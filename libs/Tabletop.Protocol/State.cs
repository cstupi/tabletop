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

/// <summary>One die. Triggered: what this die's face set off, `Rule` or `Rule +N hits`, named by the server when the die is rolled, rerolled
/// or set by hand (from its record's FaceTriggers); null when nothing and in logs written before it. Equality compares the triggers by value.</summary>
public sealed record Die(int Raw, int Modified, bool Success, IReadOnlyList<string>? Triggered = null)
{
    public bool Equals(Die? other) => other is not null && Raw == other.Raw && Modified == other.Modified && Success == other.Success
        && (Triggered ?? []).SequenceEqual(other.Triggered ?? []);
    public override int GetHashCode() => HashCode.Combine(Raw, Modified, Success, Triggered?.Count ?? 0);
}
/// <summary>A change to a roll's dice. Via: the rule that granted the property whose effect this is (Rending for its AP); null for a
/// property held outright and in logs written before it.</summary>
public sealed record RollModifier(string Source, int Amount, string? Via = null);
/// <summary>
/// p.11's cover verdict for the whole target unit of a shooting attack, on each of its defense rolls. InCover: the majority test passed;
/// Models of Of: the living models counted in cover. IgnoredBy: the rule that took the +1 away from a unit that had it (a suppression
/// such as Blast, or Indirect's dropped sight half). The +1 applies when InCover and nothing ignored it.
/// </summary>
public sealed record CoverVerdict(bool InCover, int Models, int Of, string? IgnoredBy = null);

/// <summary>Where a threshold came from: the stat it was read from, the value printed in the slot it was read from, and each
/// replace_stat (SetTo) or modify_stat (Amount) row in the order applied. Ignored: the rows on the stat of rules a suppression removed
/// (detailed-log spec); null when none, and in logs written before it. Null for a threshold no stat gave, and in logs written before it.</summary>
public sealed record ThresholdBasis(string Stat, int Printed, IReadOnlyList<ThresholdChange> Changes, IReadOnlyList<IgnoredEffect>? Ignored = null);
public sealed record ThresholdChange(string Source, int? SetTo, int Amount);   // SetTo for replace_stat, Amount for modify_stat
/// <summary>A rule's row that would have acted and was taken away (detailed-log spec): Source the rule, By the rule whose row took it away,
/// Amount its signed amount (a roll or stat modifier; 0 for a whole rule), SetTo the value a replace_stat would have set.</summary>
public sealed record IgnoredEffect(string Source, string By, int Amount = 0, int? SetTo = null);
/// <summary>
/// A roll as it happened. Threshold is null for the roll-off. RequiredRaw is the raw face a roll judged on alone (null for a roll judged
/// on its modified result), so a reroll or an edit judges the die the way the roll did. Cover is set on a shooting attack's defense rolls
/// and null elsewhere and in logs written before it. FaceTriggers: what each raw face sets off on this roll (Die.Triggered), one entry per
/// face that sets anything off, so a die a forced reroll or SetDice replaces carries its new face's; null when no face does and in logs written
/// before it. Ignored: the roll modifiers a suppression took away (detailed-log spec), cover aside (Cover says it); null when none and in
/// logs written before it. HitsTriggerNothing: the roll's successful dice set off no special rule (3.6.0 p.9, a fatigued melee's hits), so
/// score-hits reads no extra hit or grant off them and FaceTriggers names nothing for them; false elsewhere and in logs written before it.
/// Replay applies this; it never re-rolls.
/// </summary>
public sealed record RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice, int? RequiredRaw = null, ThresholdBasis? Basis = null,
    CoverVerdict? Cover = null, IReadOnlyList<FaceTriggers>? FaceTriggers = null, IReadOnlyList<IgnoredEffect>? Ignored = null, bool HitsTriggerNothing = false)
{
    /// <summary>The die with what its raw face sets off on this roll.</summary>
    public Die Stamp(Die die) => FaceTriggers?.FirstOrDefault(f => f.Raw == die.Raw)?.Triggered is { } t ? die with { Triggered = t } : die with { Triggered = null };
}

/// <summary>What one raw face sets off on its roll, `Rule` or `Rule +N hits`, as Die.Triggered names it.</summary>
public sealed record FaceTriggers(int Raw, IReadOnlyList<string> Triggered);

/// <summary>What a sequence was invoked with. Action is the action row's name when the sequence is an action.</summary>
public sealed record SequenceContext(Guid? ActorUnitId, IReadOnlyList<Guid> TargetUnitIds, IReadOnlyList<Guid> WeaponIds, string? Action);
