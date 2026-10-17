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
    /// of another unit of the same team, whose unit is the one being deployed, on the table or still on the shelf. A Transport deployed earlier
    /// takes no one at deployment. The unit boarding is one that has not deployed (NeverDeployed) and deploys when the Transport does: set aside
    /// exactly when the Transport is, so a reserved unit never rides into the ordinary deployment and an ordinary one never into a Scout's or an
    /// Ambusher's. The scheduler then counts it deployed (it is no longer all on the shelf), and its set-aside clears with the Transport's.
    /// </summary>
    public bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying)
    {
        var p = state.Projection;
        if (!p.Models.TryGetValue(containerModelId, out var c) || c.UnitId == unitId || !deploying.Contains(c.UnitId) || !GfMoves.IsContainer(state, c.Id)) return false;
        if (!p.Units.TryGetValue(unitId, out var unit) || Teams.Of(state, c.UnitId) is not { } team || team != Teams.Of(state, unitId)) return false;
        if (unit.Conditions.Contains(GfNames.SetAside) != p.Units[c.UnitId].Conditions.Contains(GfNames.SetAside)) return false;
        return NeverDeployed(state, unit, c.UnitId);
    }

    /// <summary>
    /// Every model of the unit is on the shelf or already boarded into the deploying Transport's unit, and the effective log never put one of them
    /// in play by any other way: no model ever placed on the table, none ever removed back to the shelf. A unit that deployed, lost models or was
    /// destroyed is not undeployed, whatever the shelf shows now.
    /// </summary>
    static bool NeverDeployed(GameState state, UnitInPlay unit, Guid transportUnit)
    {
        var p = state.Projection;
        var models = unit.Definition.Models.Select(m => m.Id).ToHashSet();
        if (models.Any(m => p.Models[m].Location is not (OnShelf or Inside))) return false;
        if (models.Any(m => p.Models[m].Location is Inside i && (!p.Models.TryGetValue(i.ContainerId, out var holder) || holder.UnitId != transportUnit))) return false;
        return !EventApplier.EffectiveLog(state.Applier.Log)
            .SelectMany(e => e.Payload is StepCompleted sc ? sc.Changes.Cast<EventPayload>() : [e.Payload]).OfType<ModelsMoved>()
            .SelectMany(m => m.Moves).Any(r => models.Contains(r.ModelId) && (r.To is OnTable || (r.To is OnShelf && r.From is not OnShelf)));
    }
}
