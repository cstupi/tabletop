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

    /// <summary>The unit's models still in play — on the table or inside a container in play (TeamUnits.InPlay). Counts, wound allocation and "is it destroyed" ask this one (game flow → Inside a container: the shelf is out of play; a model inside a container is not, unless that container is still on the shelf).</summary>
    public static IReadOnlyList<Guid> AliveModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && Scheduler.TeamUnits.InPlay(state, m)).Select(m => m.Id).ToList();

    /// <summary>The container this unit is inside, or null. A unit embarks and disembarks whole, so the first occupant model answers for all.</summary>
    public static Guid? ContainerOf(GameState state, Guid unitId) =>
        state.Projection.Models.Values.FirstOrDefault(m => m.UnitId == unitId && m.Location is Inside) is { Location: Inside i } ? i.ContainerId : null;

    /// <summary>The units with at least one model inside the given container model, in projection order.</summary>
    public static IReadOnlyList<Guid> OccupantUnits(GameState state, Guid containerModelId) =>
        state.Projection.Models.Values.Where(m => m.Location is Inside i && i.ContainerId == containerModelId).Select(m => m.UnitId).Distinct().ToList();

    /// <summary>The unit's own properties plus the rules its grants give it: a grant whose row is a subject-self grant_property names a rule the
    /// unit holds, with the row's amount as its parameter ("gets AP(1)" → AP 1). Any other grant gives the unit no rule by name.</summary>
    public static IEnumerable<PropertyInstance> UnitProperties(GameState state, PropertySet props, Guid unitId)
    {
        var unit = state.Projection.Units[unitId];
        var granted = unit.Grants.Select(g => props.EffectById(g.EffectId))
            .Where(r => r is { Kind: EffectKind.GrantProperty, Subject: EffectSubject.Self, TargetEntry: not null })
            .Select(r => new PropertyInstance(r!.TargetEntry!, r.Amount));
        return unit.Definition.Properties.Concat(granted);
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
        if (UnitProperties(state, props, unitId).Any(p => p.Name == entry) || AuraGrants(state, props, unitId).Contains(entry)) return true;
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

    /// <summary>
    /// The rules carried Auras give the unit (army-book rules spec → Aura: "This model and its unit get X"): a played model entry with aggregation
    /// any and no targeting, on any of the unit's living carriers, whose unconditional subject-self grant_property row names X. The carriers are
    /// the models still in play (AliveModels) once any is, so a carrier removed as a casualty grants nothing; while every model is on the shelf
    /// they are the definition's, because a deployment rule (Scout, Ambush) is read before the unit has a position. An Aura held as the unit's
    /// own property, as an army imported before the army-book rules were generated holds a rule every model carried, is carried by every model.
    /// </summary>
    static IEnumerable<string> AuraGrants(GameState state, PropertySet props, Guid unitId)
    {
        var alive = AliveModels(state, unitId);
        var carriers = alive.Count > 0 ? alive.Select(id => state.Projection.Models[id].Slot) : state.Projection.Units[unitId].Definition.Models;
        var own = carriers.Any() ? state.Projection.Units[unitId].Definition.Properties : [];
        // "Counts as having X" chains: an Aura of a rule that counts as having another (Infiltrate Aura → Infiltrate → Ambush) gives both.
        var given = new HashSet<string>();
        var next = carriers.SelectMany(m => m.Properties).Concat(own).Select(p => p.Name).ToList();
        while (next.Count > 0)
        {
            next = next.Select(props.Entry).OfType<CatalogEntry>()
                .Where(e => e is { Kind: CatalogKind.Model, Aggregation: Aggregation.Any, Played: true, Targeting: null })
                .SelectMany(e => e.Effects.Where(f => f is { Kind: EffectKind.GrantProperty, Subject: EffectSubject.Self, TargetEntry: not null } && f.Conditions.Count == 0))
                .Select(f => f.TargetEntry!).Where(given.Add).ToList();
        }
        return given;
    }

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
