using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book deployment rules (army-book rules spec, batch 3) read by name where deployment is judged, which has no properties set to hand: a rule is
/// carried when the unit's definition holds it, on the unit or on a model slot, itself or as its Aura.
/// </summary>
public static class GfAmbush
{
    public const string Infiltrate = "Infiltrate", SurpriseAttack = "Surprise Attack", RapidAmbush = "Rapid Ambush", AmbushBeacon = "Ambush Beacon",
        RepelAmbushers = "Repel Ambushers", InfiltratingPrecisionStrike = "Infiltrating Precision Strike", AmbushReDeployment = "Ambush Re-Deployment", ReDeployment = "Re-Deployment";
    /// <summary>A unit that arrived from Ambush this round (Ambushing Piercing Shot: "on the round in which it deploys via this rule"); cleared as a round starts.</summary>
    public const string AmbushedThisRound = "ambushed this round";
    /// <summary>A unit removed and placed again by Re-Deployment; cleared as round one starts.</summary>
    public const string ReDeployed = "re-deployed";
    public const string AmbushRedeploySeq = "ambush-redeploy";

    /// <summary>The definition carries the rule or its Aura (a deployment rule is read before the unit has a position).</summary>
    public static bool Carries(GameState state, Guid unit, string rule) => Carries(state.Projection.Units[unit].Definition, rule);

    public static bool Carries(Tabletop.Content.Unit d, string rule)
    {
        bool Named(string n) => n == rule || n == rule + " Aura";
        return d.Properties.Any(p => Named(p.Name)) || d.Models.Any(m => m.Properties.Any(p => Named(p.Name)));
    }

    /// <summary>"May be deployed anywhere over 3” away from enemy unit" (Infiltrate, and Surprise Attack and 3.6 Infiltrating Precision Strike, which "count as having
    /// Infiltrate"); else Ambush's 9".</summary>
    public static double Reach(GameState state, Guid unit) => Reach(state.Projection.Units[unit].Definition);
    public static double Reach(Tabletop.Content.Unit d) =>
        Carries(d, Infiltrate) || Carries(d, SurpriseAttack) || Carries(d, InfiltratingPrecisionStrike) ? 3 : GfNames.AmbushDistance;

    /// <summary>The distance an Ambusher keeps from an enemy unit's models: its own reach, or over 12" from a unit with Repel Ambushers.</summary>
    public static double Kept(GameState state, Guid enemyUnit, double reach) => Kept(state.Projection.Units[enemyUnit].Definition, reach);
    public static double Kept(Tabletop.Content.Unit enemy, double reach) => Carries(enemy, RepelAmbushers) ? Math.Max(reach, RepelDistance) : reach;
    public const double RepelDistance = 12;

    /// <summary>"Friendly units using Ambush may ignore distance restrictions from enemies if they are deployed within 6" of this model."</summary>
    public static bool NearBeacon(GameState state, Guid? team, PlacedModel placed) =>
        state.Projection.Models.Values.Any(m => m.Location is OnTable && Teams.Of(state, m.UnitId) == team && m.Slot.Properties.Any(p => p.Name == AmbushBeacon)
            && Distance.EdgeToEdge(placed, Battlefield.Placed(state, m.Id)!.Value) <= 6 + 1e-6);

    public static StateChange Arrived(Guid unit) => new ConditionSet(new Subject(SubjectKind.Unit, unit), AmbushedThisRound, true);

    /// <summary>Ambush Re-Deployment: "Once per game, when a unit where all models have this rule ends its activation, you may immediately remove it from the
    /// table … and deploy it as if it had Ambush at the beginning of the next round." Offered at the activation's end while unused.</summary>
    public static bool MayReDeploy(GameState state, Guid unit) =>
        Lookup.LivingModels(state, unit).Count > 0 && !state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)
        && state.Projection.Units[unit].Definition.Models.All(m => m.Properties.Any(p => p.Name == AmbushReDeployment) || state.Projection.Units[unit].Definition.Properties.Any(p => p.Name == AmbushReDeployment))
        && !RemovedToAmbush(state, unit);

    /// <summary>The unit was removed by Ambush Re-Deployment, so it deploys "as if it had Ambush".</summary>
    public static bool RemovedToAmbush(GameState state, Guid unit) =>
        EventApplier.EffectiveLog(state.Applier.Log).Any(e => e.Payload is SequenceStarted { Sequence: AmbushRedeploySeq } s && s.Context.ActorUnitId == unit);

    public static Sequence AmbushRedeploy() => new(AmbushRedeploySeq, [new RemoveStep()]);

    /// <summary>Every model of the unit to the shelf, set aside: the next round's Ambushers include it.</summary>
    sealed class RemoveStep : IStep
    {
        public string Name => "remove";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var models = Lookup.LivingModels(ctx.State, unit);
            ctx.Applied.Add(AmbushReDeployment);
            return new Complete([new ModelsMoved(models.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList()),
                new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.SetAside, true)]);
        }
    }

    /// <summary>Re-Deployment: "After all other units are deployed (excluding units that were set aside), you may remove up to two friendly units from the table
    /// and deploy them again." The units a team may still pick in the Scouts phase: its own on the table, not yet re-deployed, while it holds a unit with the
    /// rule and has picked fewer than two.</summary>
    public static IReadOnlyList<Guid> ReDeployable(GameState state, Guid team)
    {
        var units = Scheduler.TeamUnits.Of(state, team);
        if (!units.Any(u => Carries(state, u.Id, ReDeployment))) return [];
        if (units.Count(u => u.Conditions.Contains(ReDeployed)) >= 2) return [];
        return units.Where(u => !u.Conditions.Contains(ReDeployed) && Scheduler.TeamUnits.ModelsOf(state, u).Any(m => m.Location is OnTable)).Select(u => u.Id).ToList();
    }
}
