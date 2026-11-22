using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>p.5 Line of sight (core) or p.56 Hybrid True Line of Sight (3.6.0 p.58).</summary>
public enum GfSightMethod { Core, HybridTrue }

/// <summary>p.5 "the perimeter of other units": each unit blocks with one outline (core), or each model with its body (optional-rules spec → Units as sight blockers).</summary>
public enum GfUnitSightBlocking { Perimeter, PerModel }

/// <summary>
/// The optional rules a Grimdark Future game opted into, parsed from GameInfo.OptionalRules (optional-rules spec → How the rules read it).
/// An absent key reads as Core's value. Each rule an option changes reads GfOptions.Of(state) where it decides; nothing caches it.
/// </summary>
public sealed record GfOptions(GfSightMethod LineOfSight, GfUnitSightBlocking UnitSightBlocking, bool MeleeSeparation)
{
    public static readonly GfOptions Core = new(GfSightMethod.Core, GfUnitSightBlocking.Perimeter, MeleeSeparation: true);

    const string UnitSightBlockingKey = "unit_sight_blocking", LineOfSightKey = "line_of_sight", MeleeSeparationKey = "melee_separation";

    /// <summary>
    /// The options, their book names and their values in display order; the first value is the default. Both editions offer the same, and
    /// the pages they rest on read alike in both, so one text serves both. Each description says in table terms what the engine plays:
    /// unit sight blocking p.5 / 3.6.0 p.5 "the perimeter of other units" (the outline is the optional-rules spec's convex hull; the target's
    /// own unit blocks per model by owner ruling 2026-10-15); line of sight p.5 / 3.6.0 p.5 eye level, p.56 / 3.6.0 p.58 Hybrid True's size
    /// classes and exceptions; melee separation p.10 / 3.6.0 p.10 "players may agree not to separate units by 1"" (with it off,
    /// GfMoves.ContactBreach drops the 1" only against a unit the mover touched when its move started).
    /// </summary>
    public static RulesOptionCatalog Catalog { get; } = new(
    [
        new RulesOption(UnitSightBlockingKey, "Unit sight blocking",
            [
                new RulesOptionValue("perimeter", "Each unit's perimeter blocks",
                    "A unit in the way blocks sight across its whole outline, gaps between its models included. The target's own unit blocks only with its models."),
                new RulesOptionValue("per_model", "Each model blocks",
                    "A unit in the way blocks sight only with its models' bodies, so a line can pass through the gaps between them."),
            ], "perimeter",
            "How other units standing between a model and its target block its line of sight."),
        new RulesOption(LineOfSightKey, "Line of sight method",
            [
                new RulesOptionValue("core", "Core line of sight",
                    "A model sees its target if a straight line from its eye level reaches the target without passing through terrain that blocks sight or another unit."),
                new RulesOptionValue("hybrid_true", "Hybrid True Line of Sight",
                    "Core line of sight, with each model given a size class by its Tough (Small, Medium, Large, Huge, Titanic). A model sees through a unit of a smaller class, through one no larger than itself when the target is larger than that unit, and through a unit standing lower when it stands on elevation."),
            ], "core",
            "How the game decides whether a model can see its target."),
        new RulesOption(MeleeSeparationKey, "Melee separation",
            [
                new RulesOptionValue("on", "Separate units by 1\"",
                    "After a melee in which neither unit was destroyed, the charging unit moves back 1\" so the units are no longer in contact."),
                new RulesOptionValue("off", "Units stay in contact",
                    "After a melee the units stay in contact. Both still activate, move, shoot and charge as normal, and may stay within 1\" of a unit they were touching when they move."),
            ], "on",
            "Whether the units of a melee step apart once it is over."),
    ],
    [new RulesPreset("Core rules", new Dictionary<string, string>(), null)]);

    public static GfOptions Of(GameState state) => Parse(state.Info.OptionalRules);

    /// <summary>The map read as options; an absent key, or a value Problems would name, reads as Core's.</summary>
    public static GfOptions Parse(IReadOnlyDictionary<string, string> map) => new(
        map.GetValueOrDefault(LineOfSightKey) == "hybrid_true" ? GfSightMethod.HybridTrue : GfSightMethod.Core,
        map.GetValueOrDefault(UnitSightBlockingKey) == "per_model" ? GfUnitSightBlocking.PerModel : GfUnitSightBlocking.Perimeter,
        MeleeSeparation: map.GetValueOrDefault(MeleeSeparationKey) != "off");

    /// <summary>Every key the catalog does not hold, and every value its option does not offer, in map order.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyDictionary<string, string> map) => map.Select(kv =>
        Catalog.Options.FirstOrDefault(o => o.Key == kv.Key) is not { } option ? $"unknown option {kv.Key}"
        : option.Values.All(v => v.Value != kv.Value) ? $"{kv.Key} has no value {kv.Value}" : null).OfType<string>().ToList();
}
