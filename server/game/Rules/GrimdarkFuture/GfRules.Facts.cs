using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The in-game UX plan's partial: the facts the session tells the client (in-game UX spec → Model facts, Sight question).</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>Tough(X): "must take X wounds to be killed"; every other model is killed by one.</summary>
    public int MaxWounds(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1;

    /// <summary>
    /// p.8, Who can shoot: "All models in a unit with line of sight to the target, and that have a weapon that is within range of it, may fire
    /// at it" — GfAttack.EligibleModels per ranged weapon group (p.8, Multiple weapon types), united. Each group's live set is settled as
    /// GfAttack.AttackInteraction settles it with no frame: no charge range, no faces, under the action of the unit's running activation.
    /// </summary>
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId)
    {
        var p = state.Projection;
        if (!p.Units.ContainsKey(unitId) || !p.Units.ContainsKey(targetUnitId)) return [];
        if (Lookup.AliveModels(state, unitId).Count == 0 || Lookup.LivingModels(state, targetUnitId).Count == 0) return [];
        var action = p.Stack.FirstOrDefault(f => f.Context.ActorUnitId == unitId)?.Context.Action;
        return GfShooting.Groups(state, properties, unitId, null).SelectMany(group =>
        {
            var models = p.Models.Values.Where(m => m.Slot.Weapons.Any(w => group.WeaponIds.Contains(w.Id))).Select(m => m.Id).ToList();
            var live = LiveProperties.Settle(new Interaction(state, properties, Party.Of(state, unitId, models, group.WeaponIds), Party.Whole(state, targetUnitId),
                AttackKind.Shooting, action, Battlefield.UnitDistance(state, unitId, targetUnitId), Battlefield.TerrainUnder(state, targetUnitId)));
            return GfAttack.EligibleModels(state, live, unitId, targetUnitId, group.WeaponIds, AttackKind.Shooting);
        }).Distinct().ToList();
    }
}
