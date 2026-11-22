using Tabletop.Content;
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
    /// GfAttack.AttackInteraction's with no frame: no charge range, no faces, under the action of the unit's running activation. A group is judged in
    /// the parts its attack resolves as (GfGroups.Split), each over its own carriers, as `fire-groups` and `GfShooting.ValidTargets` judge it.
    /// </summary>
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId)
    {
        var p = state.Projection;
        if (!p.Units.ContainsKey(unitId) || !p.Units.ContainsKey(targetUnitId)) return [];
        if (Lookup.AliveModels(state, unitId).Count == 0 || Lookup.LivingModels(state, targetUnitId).Count == 0) return [];
        var action = p.Stack.FirstOrDefault(f => f.Context.ActorUnitId == unitId)?.Context.Action;
        return GfGroups.Split(state, properties, GfShooting.Groups(state, properties, unitId, null)).SelectMany(part =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(state, properties, null, unitId, targetUnitId, part.WeaponIds, AttackKind.Shooting, action));
            return GfAttack.EligibleModels(state, live, unitId, targetUnitId, part.WeaponIds, AttackKind.Shooting);
        }).Distinct().ToList();
    }

    /// <summary>
    /// Each ranged weapon group's range against the target, on the live set Shooters judges it with: the printed range, the range a model of
    /// the group reaches with (the longest of its weapons), and the rules that changed it (Shadowborn's "-4\" range"), so a client can say why a
    /// model that sees the target cannot shoot it. Each part of the group (GfGroups.Split) is read on its own live set, as its attack reads it, and the group
    /// shows the longest part's range and the rules that changed that part's.
    /// </summary>
    public IReadOnlyList<ShotRange> ShotRanges(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId)
    {
        var p = state.Projection;
        if (!p.Units.ContainsKey(unitId) || !p.Units.ContainsKey(targetUnitId)) return [];
        if (Lookup.AliveModels(state, unitId).Count == 0 || Lookup.LivingModels(state, targetUnitId).Count == 0) return [];
        var action = p.Stack.FirstOrDefault(f => f.Context.ActorUnitId == unitId)?.Context.Action;
        var weapons = p.Models.Values.SelectMany(m => m.Slot.Weapons).ToDictionary(w => w.Id);
        return GfShooting.Groups(state, properties, unitId, null).Select(group =>
        {
            var printed = group.WeaponIds.Max(w => StatValue.Read(StatKind.Distance, weapons[w].Stats[GfNames.Range]).Inches);
            var (inches, by) = GfGroups.Split(state, properties, [group]).Select(part =>
            {
                var live = LiveProperties.Settle(GfAttack.AttackInteraction(state, properties, null, unitId, targetUnitId, part.WeaponIds, AttackKind.Shooting, action));
                var changed = new List<string>();
                return (Inches: part.WeaponIds.Max(w => StatReader.WeaponRange(live, Side.Attacker, w, changed) ?? 0), By: changed);
            }).MaxBy(x => x.Inches);
            return new ShotRange(group.Name, printed, inches, inches == printed ? [] : by.Distinct().ToList());
        }).ToList();
    }

    /// <summary>
    /// p.15: "Transports may deploy with units inside", and "units inside Transports are deployed at the same time as the Transport" (passengers
    /// spec). The container is a Transport model of another unit of the same team whose unit is being placed now (<paramref name="deploying"/>) or has
    /// never deployed, so it is on the shelf: a Transport deployed earlier takes no one at deployment. The unit boarding has not deployed
    /// (NeverDeployed) and is set aside exactly when the Transport is, so a reserved unit never rides into the ordinary deployment and an ordinary one
    /// never into a Scout's or an Ambusher's. It fits: p.15's spaces (Spaces) of the Transport's other occupants plus its own are at most X.
    /// The scheduler then counts it deployed with the Transport, and its set-aside clears with the Transport's.
    /// </summary>
    public bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying) =>
        MayDeployInside(state, unitId, containerModelId, deploying, Deployed(state));

    static bool MayDeployInside(GameState state, Guid unitId, Guid containerModelId, IReadOnlySet<Guid> deploying, IReadOnlySet<Guid> deployed)
    {
        var p = state.Projection;
        if (!p.Models.TryGetValue(containerModelId, out var c) || c.UnitId == unitId || !GfMoves.IsContainer(state, c.Id)) return false;
        if (!p.Units.TryGetValue(unitId, out var unit) || Teams.Of(state, c.UnitId) is not { } team || team != Teams.Of(state, unitId)) return false;
        var transport = p.Units[c.UnitId];
        if (GfReserves.IsReserve(unit) || GfReserves.IsReserve(transport)) return false;   // a reserve unit is placed by its rule only
        if (!deploying.Contains(c.UnitId) && !(Scheduler.TeamUnits.AllShelved(state, transport) && transport.Definition.Models.All(m => !deployed.Contains(m.Id)))) return false;
        if (unit.Conditions.Contains(GfNames.SetAside) != transport.Conditions.Contains(GfNames.SetAside)) return false;
        return NeverDeployed(state, unit, c.UnitId, deployed) && Fits(state, unit, c.Id);
    }

    /// <summary>
    /// Every model of the unit is on the shelf or already inside the Transport's unit, and none is in <paramref name="deployed"/>: a unit that deployed,
    /// lost models or was destroyed is not undeployed, whatever the shelf shows now.
    /// </summary>
    static bool NeverDeployed(GameState state, UnitInPlay unit, Guid transportUnit, IReadOnlySet<Guid> deployed)
    {
        var p = state.Projection;
        var models = unit.Definition.Models.Select(m => m.Id).ToList();
        if (models.Any(m => p.Models[m].Location is not (OnShelf or Inside))) return false;
        if (models.Any(m => p.Models[m].Location is Inside i && (!p.Models.TryGetValue(i.ContainerId, out var holder) || holder.UnitId != transportUnit))) return false;
        return !models.Any(deployed.Contains);
    }

    /// <summary>
    /// The models the effective log has put in play: placed on the table, or moved to the shelf from anywhere but the shelf or the inside of a container
    /// that had never been on the table. Taking a passenger back out of a Transport still waiting on the shelf is not deploying; a casualty is.
    /// </summary>
    static HashSet<Guid> Deployed(GameState state)
    {
        var deployed = new HashSet<Guid>();
        foreach (var r in EventApplier.EffectiveLog(state.Applier.Log)
            .SelectMany(e => e.Payload is StepCompleted sc ? sc.Changes.Cast<EventPayload>() : [e.Payload]).OfType<ModelsMoved>().SelectMany(m => m.Moves))
        {
            if (r.To is OnTable) deployed.Add(r.ModelId);
            else if (r.To is OnShelf && r.From is not OnShelf && !(r.From is Inside i && !deployed.Contains(i.ContainerId))) deployed.Add(r.ModelId);
        }
        return deployed;
    }

    /// <summary>
    /// p.15: "May transport up to X models or Heroes with up to Tough(6), and non-Heroes with up to Tough(3) which occupy 3 spaces each", and its
    /// example: "Regular models and Heroes with Tough(3) or Tough(6) occupy 1 space, Tough(3) models occupy 3 spaces, and models with Tough(6) or
    /// higher can't be transported". The unit's models (in play, <paramref name="living"/>: its living models only) plus the other units already inside
    /// take at most X spaces. Read for deploying inside and for embarking in play (GfMoves.IsFriendlyContainer).
    /// </summary>
    static bool Fits(GameState state, UnitInPlay unit, Guid containerModelId) => Fits(state, unit.Id, unit.Definition.Models.Select(m => m.Id), containerModelId);

    internal static bool Fits(GameState state, Guid unitId, IEnumerable<Guid> models, Guid containerModelId)
    {
        if (Lookup.ModelParameter(state, containerModelId, GfNames.Transport) is not { } room) return false;
        var mine = models.Select(m => Spaces(state, m)).ToList();
        if (mine.Any(s => s is null)) return false;
        var others = state.Projection.Models.Values.Where(m => m.UnitId != unitId && m.Location is Inside i && i.ContainerId == containerModelId)
            .Sum(m => Spaces(state, m.Id) ?? 0);
        return others + mine.Sum(s => s!.Value) <= room;
    }

    /// <summary>p.15: the spaces a model takes in a Transport, or null when it can't be transported.</summary>
    static int? Spaces(GameState state, Guid modelId)
    {
        var tough = Lookup.ModelParameter(state, modelId, GfNames.Tough);
        var hero = state.Projection.Models[modelId].Slot.Properties.Any(p => GfNames.IsHero(p.Name));
        return (tough, hero) switch
        {
            (null, _) => 1,
            (<= 6, true) => 1,
            (<= 3, false) => 3,
            _ => null,
        };
    }

    /// <summary>
    /// p.13 Ambush, p.14 Scout: "May be set aside before deployment". The units, every model on the shelf and none ever deployed, that hold Ambush or Scout
    /// by Lookup.UnitHas (its "counts as" chains: Infiltrate, an Infiltrate Aura) or carry Surprise Attack ("Counts as having Infiltrate"), as Ambushers reads
    /// them; a set-aside one stays named, so it may return to deployment. A reserve unit is placed by its rule only. Before round one only: empty once
    /// rounds begin, without reading the log.
    /// </summary>
    public IReadOnlyList<Guid> MaySetAside(GameState state, PropertySet properties)
    {
        var p = state.Projection;
        if (p.Scheduler.Round >= 1) return [];
        var candidates = p.Units.Values.Where(u => !GfReserves.IsReserve(u)
                && u.Definition.Models.All(m => p.Models.TryGetValue(m.Id, out var x) && x.Location is OnShelf && x.Wounds == 0)
                && (Lookup.UnitHas(state, properties, u.Id, GfNames.Ambush) || Lookup.UnitHas(state, properties, u.Id, GfNames.Scout) || GfAmbush.Carries(state, u.Id, GfAmbush.SurpriseAttack)))
            .ToList();
        if (candidates.Count == 0) return [];
        var deployed = Deployed(state);
        return candidates.Where(u => !u.Definition.Models.Any(m => deployed.Contains(m.Id))).Select(u => u.Id).ToList();
    }

    /// <summary>
    /// Per unit MaySetAside names, the rule it carries that lets it be set aside, as its army book prints it: the first of the unit's own rules, then
    /// its models', that is Ambush or Scout or grants one (Infiltrate "Counts as having Ambush", Infiltrating Precision Strike, an Aura of one), else
    /// Surprise Attack ("Counts as having Infiltrate"), else Ambush; with that entry's rule text.
    /// </summary>
    public IReadOnlyDictionary<Guid, SetAsideRule> SetAsideRules(GameState state, PropertySet properties) =>
        MaySetAside(state, properties).ToDictionary(id => id, id => SetAsideRuleOf(state, properties, id));

    static SetAsideRule SetAsideRuleOf(GameState state, PropertySet props, Guid unitId)
    {
        var d = state.Projection.Units[unitId].Definition;
        var carried = d.Properties.Select(p => p.Name).Concat(d.Models.SelectMany(m => m.Properties.Select(p => p.Name))).Distinct();
        var name = carried.FirstOrDefault(n => LetsSetAside(props, n, 0)) ?? (GfAmbush.Carries(d, GfAmbush.SurpriseAttack) ? GfAmbush.SurpriseAttack : GfNames.Ambush);
        return new SetAsideRule(name, props.Entry(name)?.RuleText ?? "");
    }

    /// <summary>The entry is Ambush or Scout, an Aura of one, or grants one through its grant_property rows, a few links deep.</summary>
    static bool LetsSetAside(PropertySet props, string name, int depth)
    {
        if (name is GfNames.Ambush or GfNames.Scout) return true;
        if (depth > 4) return false;
        if (name.EndsWith(" Aura", StringComparison.Ordinal) && LetsSetAside(props, name[..^" Aura".Length], depth + 1)) return true;
        return props.Entry(name) is { } e && e.Effects.Any(f => f.Kind == EffectKind.GrantProperty && f.TargetEntry is { } t && LetsSetAside(props, t, depth + 1));
    }

    /// <summary>
    /// Passengers spec → What the client is told: for each unit that may be put in a Transport now (every model on the shelf), the Transport models
    /// MayDeployInside allows with nothing being placed. Empty once no Transport waits on the shelf, without reading the log.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside(GameState state)
    {
        var p = state.Projection;
        var waiting = p.Models.Values.Where(m => m.Location is OnShelf && GfMoves.IsContainer(state, m.Id) && Scheduler.TeamUnits.AllShelved(state, p.Units[m.UnitId]))
            .Select(m => m.Id).ToList();
        if (waiting.Count == 0) return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var deployed = Deployed(state);
        var none = new HashSet<Guid>();
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var unit in p.Units.Values.Where(u => Scheduler.TeamUnits.AllShelved(state, u)))
            if (waiting.Where(c => MayDeployInside(state, unit.Id, c, none, deployed)).ToList() is { Count: > 0 } into) result[unit.Id] = into;
        return result;
    }
}
