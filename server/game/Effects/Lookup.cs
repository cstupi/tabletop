using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

/// <summary>Name checks and instance gathering over a unit's own properties, its models' and weapons', and the grants store.</summary>
public static class Lookup
{
    /// <summary>The unit's models that have a position. Every geometric question asks this one.</summary>
    public static IReadOnlyList<Guid> LivingModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => m.Id).ToList();

    /// <summary>The unit's models still in play — on the table or inside a container. Counts, wound allocation and "is it destroyed" ask this one (game flow → Inside a container: the shelf is out of play; a transported model is not).</summary>
    public static IReadOnlyList<Guid> AliveModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is not OnShelf).Select(m => m.Id).ToList();

    /// <summary>The container this unit is inside, or null. A unit embarks and disembarks whole, so the first occupant model answers for all.</summary>
    public static Guid? ContainerOf(GameState state, Guid unitId) =>
        state.Projection.Models.Values.FirstOrDefault(m => m.UnitId == unitId && m.Location is Inside) is { Location: Inside i } ? i.ContainerId : null;

    /// <summary>The units with at least one model inside the given container model, in projection order.</summary>
    public static IReadOnlyList<Guid> OccupantUnits(GameState state, Guid containerModelId) =>
        state.Projection.Models.Values.Where(m => m.Location is Inside i && i.ContainerId == containerModelId).Select(m => m.UnitId).Distinct().ToList();

    /// <summary>The unit's own properties plus its grants as instances. A grant's parameter is the granting effect's amount ("gets AP(1)" → AP with parameter 1), read from the pinned set by Grant.EffectId.</summary>
    public static IEnumerable<PropertyInstance> UnitProperties(GameState state, PropertySet props, Guid unitId)
    {
        var unit = state.Projection.Units[unitId];
        return unit.Definition.Properties.Concat(unit.Grants.Select(g => new PropertyInstance(g.EntryName, props.AllEffects().FirstOrDefault(e => e.Id == g.EffectId)?.Amount)));
    }

    public static IEnumerable<(Guid ModelId, PropertyInstance Property)> ModelProperties(GameState state, Guid unitId, IEnumerable<Guid> modelIds) =>
        modelIds.SelectMany(id => state.Projection.Models[id].Slot.Properties.Select(p => (id, p)));

    public static IEnumerable<(Guid WeaponId, Guid ModelId, PropertyInstance Property)> WeaponProperties(GameState state, IEnumerable<Guid> weaponIds)
    {
        var wanted = weaponIds.ToHashSet();
        return state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons.Where(w => wanted.Contains(w.Id)).SelectMany(w => w.Properties.Select(p => (w.Id, m.Id, p))));
    }

    /// <summary>Own properties, model properties under the entry's aggregation over living models, and grants. A unit-kind entry on a model slot is
    /// the unit's rule and is read off the definition, which is the reading LiveProperties.Gather takes as well.</summary>
    public static bool UnitHas(GameState state, PropertySet props, Guid unitId, string entry)
    {
        if (UnitProperties(state, props, unitId).Any(p => p.Name == entry)) return true;
        if (props.Entry(entry) is not { } e) return false;
        if (e.Kind == CatalogKind.Unit) return UnitKindOnSlots(state, props, unitId).Any(x => x.Property.Name == entry);
        var living = LivingModels(state, unitId);
        var with = living.Count(id => state.Projection.Models[id].Slot.Properties.Any(p => p.Name == entry));
        return e.Aggregation switch
        {
            Aggregation.All => living.Count > 0 && with == living.Count, Aggregation.Any or Aggregation.None => with > 0,
            Aggregation.Majority => with * 2 > living.Count, _ => false,
        };
    }

    /// <summary>The unit-kind properties written onto the unit's model slots, over the definition rather than the projection: a rule scoped to the unit
    /// does not depend on which of its models are standing, and a deployment rule is read before anything has a position at all.</summary>
    public static IEnumerable<(PropertyInstance Property, CatalogEntry Entry)> UnitKindOnSlots(GameState state, PropertySet props, Guid unitId) =>
        state.Projection.Units[unitId].Definition.Models.SelectMany(m => m.Properties)
            .Select(p => (Property: p, Entry: props.Entry(p.Name)))
            .Where(x => x.Entry is { Kind: CatalogKind.Unit })
            .Select(x => (x.Property, Entry: x.Entry!));

    public static int? UnitParameter(GameState state, PropertySet props, Guid unitId, string entry)
    {
        var own = UnitProperties(state, props, unitId).Where(p => p.Name == entry).Select(p => p.Parameter).ToList();
        if (own.Count > 0) return own.Sum(p => p ?? 0);
        var model = LivingModels(state, unitId).Select(id => ModelParameter(state, id, entry)).Where(p => p is not null).ToList();
        return model.Count == 0 ? null : model.Sum(p => p ?? 0);
    }

    public static int? ModelParameter(GameState state, Guid modelId, string entry) =>
        state.Projection.Models[modelId].Slot.Properties.FirstOrDefault(p => p.Name == entry)?.Parameter;
}
