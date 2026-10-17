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
    /// at it" — GfAttack.EligibleModels per ranged weapon group (p.8, Multiple weapon types), united. Each group's live set is
    /// GfAttack.AttackInteraction's with no frame: no charge range, no faces, under the action of the unit's running activation.
    /// </summary>
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId)
    {
        var p = state.Projection;
        if (!p.Units.ContainsKey(unitId) || !p.Units.ContainsKey(targetUnitId)) return [];
        if (Lookup.AliveModels(state, unitId).Count == 0 || Lookup.LivingModels(state, targetUnitId).Count == 0) return [];
        var action = p.Stack.FirstOrDefault(f => f.Context.ActorUnitId == unitId)?.Context.Action;
        return GfShooting.Groups(state, properties, unitId, null).SelectMany(group =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(state, properties, null, unitId, targetUnitId, group.WeaponIds, AttackKind.Shooting, action));
            return GfAttack.EligibleModels(state, live, unitId, targetUnitId, group.WeaponIds, AttackKind.Shooting);
        }).Distinct().ToList();
    }

    /// <summary>
    /// p.15: "Transports may deploy with units inside", and "units inside Transports are deployed at the same time as the Transport": a Transport
    /// of another unit of the same team, whose unit is the one being deployed, on the table or still on the shelf. The unit boarding has no model
    /// in play anywhere else. A Transport deployed earlier takes no one at deployment.
    /// </summary>
    public bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying)
    {
        var p = state.Projection;
        if (!p.Models.TryGetValue(containerModelId, out var c) || c.UnitId == unitId || !deploying.Contains(c.UnitId) || !GfMoves.IsContainer(state, c.Id)) return false;
        if (Teams.Of(state, c.UnitId) is not { } team || team != Teams.Of(state, unitId)) return false;
        return Lookup.AliveModels(state, unitId).All(m => p.Models[m].Location is Inside i && p.Models.TryGetValue(i.ContainerId, out var holder) && holder.UnitId == c.UnitId);
    }
}
