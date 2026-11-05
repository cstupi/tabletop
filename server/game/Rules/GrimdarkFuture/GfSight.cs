using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>p.56 Hybrid True Line of Sight: "all models are applied a size class, based on their Tough".</summary>
public enum GfSizeClass { Small, Medium, Large, Huge, Titanic }

/// <summary>
/// The sight options as an ISightPolicy (optional-rules spec): Unit sight blocking `perimeter` (p.5 "the perimeter of other units") and
/// Line of sight method `hybrid_true` (p.56). Bound to one state for the life of one Sight; size classes are cached per model.
/// </summary>
public sealed class GfSight : ISightPolicy
{
    readonly GameState state;
    readonly GfOptions options;
    readonly Dictionary<Guid, GfSizeClass> models = [];
    readonly Dictionary<Guid, GfSizeClass> units = [];

    GfSight(GameState state, GfOptions options) => (this.state, this.options) = (state, options);

    /// <summary>The policy for this state; the core game's per-model policy when neither sight option is set.</summary>
    public static ISightPolicy For(GameState state)
    {
        var options = GfOptions.Of(state);
        return options is { LineOfSight: GfSightMethod.Core, UnitSightBlocking: GfUnitSightBlocking.PerModel } ? ISightPolicy.PerModel : new GfSight(state, options);
    }

    public bool Perimeters => options.UnitSightBlocking == GfUnitSightBlocking.Perimeter;

    /// <summary>
    /// p.56: "The miniature can't draw line of sight through other units, with these exceptions: Models may draw line of sight through units of
    /// smaller size class. Models may draw line of sight through units of equal or lower size class, if the target is of greater size class than
    /// the unit in the way. Models on elevation may draw line of sight through units that are at a lower elevation than them."
    /// </summary>
    public bool SeesThrough(Guid observerModel, Guid targetModel, Guid blockingUnit)
    {
        if (options.LineOfSight != GfSightMethod.HybridTrue) return false;
        var (observer, inTheWay) = (Class(observerModel), UnitClass(blockingUnit));
        if (inTheWay < observer) return true;
        if (inTheWay <= observer && Class(targetModel) > inTheWay) return true;
        return Battlefield.Placed(state, observerModel) is { } eye && eye.Bottom() > Elevation
            && Lookup.LivingModels(state, blockingUnit).All(m => Battlefield.Placed(state, m)!.Value.Bottom() < eye.Bottom() - Elevation);
    }

    /// <summary>"On elevation": a base resting above the slab, which is at 0 (Collision.RestHeight rests a base on a terrain top or the slab).</summary>
    const double Elevation = 1e-6;

    GfSizeClass Class(Guid model) => models.TryGetValue(model, out var c) ? c
        : models[model] = SizeClass(Lookup.ModelParameter(state, model, GfNames.Tough), state.Projection.Models[model].Slot.Properties.Any(p => GfNames.IsHero(p.Name)));

    /// <summary>A unit is of a class below the observer's only when every one of its models is: its largest on-table model's class.</summary>
    GfSizeClass UnitClass(Guid unit) => units.TryGetValue(unit, out var c) ? c
        : units[unit] = Lookup.LivingModels(state, unit).Select(Class).DefaultIfEmpty(GfSizeClass.Small).Max();

    /// <summary>
    /// p.56's bands: "Small - No Tough and Tough(3) heroes", "Medium - Tough(3) and Tough(6) heroes", "Large - Tough(6) to Tough(12)",
    /// "Huge - Tough(15) to Tough(21)", "Titanic - Tough(24) or higher". A model reaches a band at the first value the band names and keeps
    /// the band below until then; only "Tough(6) heroes" is carved out of Large (optional-rules spec → Larger models seeing over smaller ones).
    /// </summary>
    public static GfSizeClass SizeClass(int? tough, bool hero) => (tough ?? 0) switch
    {
        >= 24 => GfSizeClass.Titanic,
        >= 15 => GfSizeClass.Huge,
        > 6 => GfSizeClass.Large,
        6 => hero ? GfSizeClass.Medium : GfSizeClass.Large,
        >= 3 => hero ? GfSizeClass.Small : GfSizeClass.Medium,
        _ => GfSizeClass.Small,
    };
}
