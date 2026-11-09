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

    /// <summary>The options, their book names and their values in display order; the first value is the default. Both editions offer the same.</summary>
    public static RulesOptionCatalog Catalog { get; } = new(
    [
        new RulesOption(UnitSightBlockingKey, "Unit sight blocking",
            [new RulesOptionValue("perimeter", "Each unit's perimeter blocks"), new RulesOptionValue("per_model", "Each model blocks")], "perimeter"),
        new RulesOption(LineOfSightKey, "Line of sight method",
            [new RulesOptionValue("core", "Core line of sight"), new RulesOptionValue("hybrid_true", "Hybrid True Line of Sight")], "core"),
        new RulesOption(MeleeSeparationKey, "Melee separation",
            [new RulesOptionValue("on", "Separate units by 1\""), new RulesOptionValue("off", "Units stay in contact")], "on"),
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
