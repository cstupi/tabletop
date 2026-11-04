using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfCasting
{
    /// <summary>A spell's targets from its caster model (Targets).</summary>
    public static IReadOnlyList<Guid> SpellTargets(StepContext ctx, Guid casterModel, CatalogEntry spell) => Targets(ctx.State, casterModel, spell.Targeting!, spell: true);

    /// <summary>
    /// p.15 first: containment answers before range and sight, so no spell crosses a container's wall either way, and a caster inside
    /// one reaches the units in there with it and the container itself with nothing to measure. Otherwise a unit is a target when one
    /// of its models is within range of the caster model and in its line of sight (p.13), or within range alone when the targeting
    /// says so (a Buff: "pick one friendly unit within 12""). A model-grain targeting offers those models. Spells and pick entries share it.
    /// A spell's target under 3.6.0 p.13 ("Pick a target and roll one die", GfEdition.CasterTargetNeedsSight false) needs sight only when the
    /// spell's own targeting says LineOfSight true; a pick entry's null is its "in line of sight" in both editions.
    /// OncePerRound (Mind Control: "Units may only be moved once per round in this way"): a unit a use of the entry moved this round is none (MovedByThisRound).
    /// </summary>
    public static IReadOnlyList<Guid> Targets(GameState state, Guid casterModel, SpellTargeting t, bool spell = false, string? entry = null)
    {
        var needsSight = t.LineOfSight ?? (!spell || GfEdition.Of(state).CasterTargetNeedsSight);
        var casterUnit = state.Projection.Models[casterModel].UnitId; var team = Teams.Of(state, casterUnit);
        var moved = t.OncePerRound && entry is not null ? MovedByThisRound(state, entry) : [];
        var candidates = state.Projection.Units.Values.Where(u => (Teams.Of(state, u.Id) == team) == (t.Side == TargetSide.Friendly) && !moved.Contains(u.Id)).Select(u => u.Id).ToList();
        var result = new List<Guid>();
        var sight = new Lazy<Sight>(() => Sight.From(state, casterUnit));                                            // built once for every candidate model
        foreach (var unit in candidates)
        {
            var reaches = Battlefield.InsideReach(state, casterUnit, unit);
            var models = reaches switch
            {
                true => Lookup.AliveModels(state, unit),
                false => [],
                null => Lookup.LivingModels(state, unit).Where(m => Battlefield.Distance(state, casterModel, m) <= t.Range
                    && (m == casterModel || !needsSight || sight.Value.CanSee(casterModel, m))).ToList(),
            };
            // "with Caster", "with Tough": the unit holds the rule (one of its models or the unit itself), or the model does.
            if (t.Requires is { } rule)
                models = t.Grain == TargetGrain.Model ? models.Where(m => Holds(state, m, rule)).ToList()
                    : state.Projection.Units[unit].Definition.Properties.Any(p => p.Name == rule) || Lookup.AliveModels(state, unit).Any(m => Holds(state, m, rule)) ? models : [];
            if (models.Count == 0) continue;
            if (t.Grain == TargetGrain.Unit) result.Add(unit); else result.AddRange(models);
        }
        // Extended Buff Range: "If this unit is within 24" of another friendly unit with this rule that has a Hero in it, then that Hero may use special rules
        // that allow it to pick friendly units within 12" (except for spells) on this unit as if it was in range." A Buff's pick (range alone) only.
        if (t is { Side: TargetSide.Friendly, Grain: TargetGrain.Unit, LineOfSight: false, Requires: null } && Holds(state, casterModel, GfNames.Hero) && Extends(state, casterUnit))
            result.AddRange(candidates.Where(u => u != casterUnit && !result.Contains(u) && Extends(state, u) && Battlefield.UnitDistance(state, casterUnit, u) is { } d && d <= ExtendedReach));
        return result;
    }

    /// <summary>
    /// The units a use of the entry moved since the round began, by any carrying model of any unit: a ModelsMoved in a step completed while a pick-rule
    /// frame of the entry was open (its own steps and its spell-test's forced move). A unit picked whose test passed was not moved.
    /// </summary>
    static HashSet<Guid> MovedByThisRound(GameState state, string entry)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).ToList();
        var from = log.FindLastIndex(p => p is RoundStarted) + 1;
        var open = new List<(Guid Frame, bool Use)>();
        var moved = new HashSet<Guid>();
        foreach (var p in log.Skip(from))
            switch (p)
            {
                case SequenceStarted s: open.Add((s.FrameId, s.Sequence == GfNames.PickRuleSeq && GfPick.Parse(s.Context.Action) is var (name, _) && name == entry)); break;
                case SequenceFinished f: open.RemoveAll(x => x.Frame == f.FrameId); break;
                case SequenceAbandoned a: open.RemoveAll(x => x.Frame == a.FrameId); break;
                case StepCompleted c when open.Any(x => x.Use):
                    foreach (var m in c.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves))
                        if (state.Projection.Models.TryGetValue(m.ModelId, out var model)) moved.Add(model.UnitId);
                    break;
            }
        return moved;
    }

    static bool Holds(GameState state, Guid model, string rule) => state.Projection.Models[model].Slot.Properties.Any(p => p.Name == rule || rule == GfNames.Hero && GfNames.IsHero(p.Name));

    public const string ExtendedBuffRange = "Extended Buff Range";
    const double ExtendedReach = 24;
    static bool Extends(GameState state, Guid unit) =>
        state.Projection.Units[unit].Definition.Properties.Any(p => p.Name == ExtendedBuffRange) || Lookup.AliveModels(state, unit).Any(m => Holds(state, m, ExtendedBuffRange));
}
