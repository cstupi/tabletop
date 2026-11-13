using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Scheduler;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// The army-book rules that place a unit the army holds off the table (army-book rules spec → Readings, batch 7). The import adds each such unit as a
/// reserve unit (<see cref="UnitReserve"/>: the rule, and the model or unit holding it); a reserve unit never deploys, and the rule places it.
/// Spawn: "Once per game, when this model is activated, you may place a new unit of X fully within 6" of it." Offered at the activation-start window of the
/// carrying model's unit while its reserve has never been placed; `place-reserve` asks where.
/// Split: "When this unit is fully destroyed, you may place a new unit of X fully within 6" of it before removing the last model." deal-wounds' `assign`
/// leaves the last model on the table while the unit's reserve waits, and `split` asks where, anchored on it, then removes it.
/// Reinforcement: "When a unit where all models have this rule is Shaken or fully destroyed, you may remove it from the table as destroyed and place a new
/// copy of it fully within 12" of any table edge at the beginning of the next round after Ambushers have been deployed. Units that deploy via
/// Reinforcement can't seize or contest objectives on the round they deploy." At each round boundary after the Ambushers, a team whose Reinforcement unit is
/// Shaken or destroyed may pick its copy (`deploy_reinforcement`); a Shaken original is removed, and the copy is placed by hand, judged by
/// `reinforcement_edge`.
/// Reinforcement 3.6 (GfEdition.ReinforcementOnShaken, ReinforcementEdgeReach, ReinforcementClearance): "When a unit where all models have this rule is Shaken,
/// you may remove it from the table as destroyed. At the beginning of the next round after a unit where all models have this rule is destroyed, after Ambushers
/// have been deployed, you may place a new copy of it fully within 9" of any table edge, over 9" away from enemy units." The morale test's `reinforcement` step
/// offers the removal when the test made the unit Shaken; the round boundary offers only a destroyed original's copy, and only at the start of the round after the one it was destroyed in; `reinforcement_distance` judges the 9".
/// </summary>
public static class GfReserves
{
    public const string Spawn = "Spawn", Split = "Split", Reinforcement = "Reinforcement";
    public const string PlaceReserveSeq = "place-reserve";
    public const double Within = 6, EdgeReach = 12;
    public const string EdgeRule = "reinforcement_edge", DistanceRule = "reinforcement_distance";
    public const string RemoveQuestion = "reinforcement_remove", RemoveStep = "reinforcement";

    public static bool IsReserve(UnitInPlay unit) => unit.Definition.Reserve is not null;

    /// <summary>Any model of the unit was ever put on the table: a reserve is placed once ("once per game", "a new unit").</summary>
    public static bool EverPlaced(GameState state, Guid unit)
    {
        var models = state.Projection.Units[unit].Definition.Models.Select(m => m.Id).ToHashSet();
        return EventApplier.EffectiveLog(state.Applier.Log).Any(e => Changes(e.Payload).OfType<ModelsMoved>().Any(m => m.Moves.Any(r => r.To is not OnShelf && models.Contains(r.ModelId))));
    }

    static IEnumerable<StateChange> Changes(EventPayload p) => p switch
    {
        StateChange c => [c], StepCompleted s => s.Changes, RoundStarted r => r.Changes, RoundEnded r => r.Changes, TurnEnded t => t.Changes, _ => [],
    };

    /// <summary>The reserve unit the rule places for its holder (a model for Spawn, a unit otherwise), while it waits: every model on the shelf and never placed.</summary>
    public static Guid? Waiting(GameState state, string rule, Guid of) =>
        state.Projection.Units.Values.FirstOrDefault(u => u.Definition.Reserve is { } r && r.Property == rule && r.Of == of
            && TeamUnits.AllShelved(state, u) && !EverPlaced(state, u.Id))?.Id;

    /// <summary>Spawn's offers at the activation start: each model of the unit in play carrying it whose reserve waits and which has not been offered this activation.</summary>
    public static IEnumerable<(Guid Model, Guid Reserve)> SpawnOffers(GameState state, Guid unit)
    {
        var started = SinceActivation(state, unit).Where(s => s.Sequence == PlaceReserveSeq).Select(s => s.Context.TargetUnitIds.FirstOrDefault()).ToHashSet();
        foreach (var model in Lookup.LivingModels(state, unit))
        {
            if (!state.Projection.Models[model].Slot.Properties.Any(p => p.Name == Spawn) && !state.Projection.Units[unit].Definition.Properties.Any(p => p.Name == Spawn)) continue;
            if (Waiting(state, Spawn, model) is { } reserve && !started.Contains(reserve)) yield return (model, reserve);
        }
    }

    /// <summary>The sequences started since the unit's activation began (its latest root action sequence).</summary>
    static IEnumerable<SequenceStarted> SinceActivation(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var start = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq or GfNames.IdleSeq } s && s.Context.ActorUnitId == unit);
        return start < 0 ? [] : log.Skip(start).Select(e => e.Payload).OfType<SequenceStarted>();
    }

    /// <summary>Split's anchor: the last model of a fully destroyed unit, which `assign` left on the table at its full wounds while the reserve waits.</summary>
    public static bool KeepsLastModel(GameState state, Guid unit) =>
        Holds(state.Projection.Units[unit].Definition, Split) && Waiting(state, Split, unit) is not null;

    static bool Holds(Unit d, string rule) => d.Properties.Any(p => p.Name == rule) || d.Models.Any(m => m.Properties.Any(p => p.Name == rule));

    /// <summary>"A unit where all models have this rule": the unit's own entry, or every model's.</summary>
    static bool HoldsAll(Unit d, string rule) => d.Properties.Any(p => p.Name == rule) || d.Models.All(m => m.Properties.Any(p => p.Name == rule));

    static Guid? Anchor(GameState state, Guid unit) =>
        Lookup.LivingModels(state, unit).Where(m => GfAttack.AtCapacity(state, m) && !(GfMoves.IsContainer(state, m) && Lookup.OccupantUnits(state, m).Count > 0)).Cast<Guid?>().FirstOrDefault();

    public static Sequence PlaceReserve() => new(PlaceReserveSeq, [new PlaceStep()]);

    /// <summary>
    /// "Place a new unit of X fully within 6" of it": one Ask over the reserve's models, each placed fully within 6" of the anchor (the Spawn model, Split's
    /// last model), on the table, clear of impassable terrain, as a disembark is (GfMoves.ExitPlacement, which AnswerBreach judges through the region's
    /// containment). "You may": the Ask may be passed, and a model whose landing is refused stays off the table. The context names the reserve as its target.
    /// </summary>
    sealed class PlaceStep : IStep
    {
        public string Name => "place";
        public StepOutcome Run(StepContext ctx)
        {
            var reserve = ctx.Frame.Context.TargetUnitIds[0];
            var rule = ctx.Frame.Context.Action!;
            // Split's anchor: the last model `assign` kept, or a routed unit's one model left (GfMorale's consequence, p.10: a Rout "counts as destroyed").
            var anchor = rule == Spawn ? ctx.Projection.Units[reserve].Definition.Reserve!.Of
                : Anchor(ctx.State, ctx.Frame.Context.ActorUnitId!.Value) ?? (Lookup.LivingModels(ctx.State, ctx.Frame.Context.ActorUnitId!.Value) is [var last] ? last : null);
            if (anchor is not { } at || Battlefield.Placed(ctx.State, at) is null) return new Finish([]);
            var models = TeamUnits.ModelsOf(ctx.State, ctx.Projection.Units[reserve]).Where(m => m.Location is OnShelf).Select(m => m.Id).ToList();
            if (ctx.History.LastClosed("place") is not { } closed)
                return new OpenAsks([ctx.Ask("place", $"{rule}: place {ctx.Projection.Units[reserve].Definition.Name} fully within {Within:0.#}\" of {ctx.Projection.Models[at].Slot.Name}",
                    ctx.TeamOf(reserve)!.Value, new MoveRegion(models, 0, [], [], null, new MoveContainment([], at, Within), Separation: GfNames.Separation, PassesModels: true), allowPass: true)]);
            var moves = GfAnswers.Moves(ctx.State, closed).Where(m => m.From is OnShelf && m.To is OnTable)
                .Select(m => GfMoves.ExitPlacement(ctx.State, m.ModelId, at, ((OnTable)m.To).Placement, Within) is { } p ? new ModelRelocation(m.ModelId, m.From, new OnTable(p)) : null)
                .OfType<ModelRelocation>().ToList();
            if (moves.Count == 0) return new Complete([]);
            ctx.Applied.Add(rule);
            return new Complete([new ModelsMoved(moves)]);
        }
    }

    /// <summary>deal-wounds' step after `assign`: a unit Split keeps on the table at its last model places its reserve (`place-reserve`), then the model is removed.</summary>
    public sealed class SplitStep : IStep
    {
        public string Name => "split";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Context.TargetUnitIds is not [var unit, ..] || Anchor(ctx.State, unit) is not { } last) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, unit).Any(m => !GfAttack.AtCapacity(ctx.State, m))) return new Complete([]);
            var removal = new ModelsMoved([new ModelRelocation(last, ctx.Projection.Models[last].Location, new OnShelf())]);
            if (ctx.History.Children.Count > 0)
                return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete([removal]) : new OpenAsks([]);
            return Waiting(ctx.State, Split, unit) is { } reserve
                ? new Push(PlaceReserveSeq, new SequenceContext(unit, [reserve], [], Split))
                : new Complete([removal]);
        }
    }

    /// <summary>The Reinforcement copies a team may deploy now: each waits, and its original is Shaken on the table or fully destroyed (not set aside).</summary>
    public static IReadOnlyList<Guid> Reinforcements(GameState state, Guid team) =>
        TeamUnits.Of(state, team).Where(u => u.Definition.Reserve is { Property: Reinforcement } r && state.Projection.Units.TryGetValue(r.Of, out var original)
                && Waiting(state, Reinforcement, r.Of) == u.Id
                && (GfEdition.Of(state).ReinforcementOnShaken ? Destroyed(state, original) && DestroyedLastRound(state, original) : ShakenOrDestroyed(state, original)))
            .Select(u => u.Id).ToList();

    /// <summary>3.6: "At the beginning of the next round after a unit where all models have this rule is destroyed": the original's last model left
    /// the table since the round before this one started, so the copy is offered at that one round's start and at no later one.</summary>
    static bool DestroyedLastRound(GameState state, UnitInPlay original)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var rounds = Enumerable.Range(0, log.Count).Where(i => log[i].Payload is RoundStarted).ToList();
        if (rounds.Count < 2) return true;
        var models = original.Definition.Models.Select(m => m.Id).ToHashSet();
        var gone = log.FindLastIndex(e => Changes(e.Payload).OfType<ModelsMoved>().Any(m => m.Moves.Any(r => r.To is not OnTable && models.Contains(r.ModelId))));
        return gone > rounds[^2];
    }

    static bool ShakenOrDestroyed(GameState state, UnitInPlay unit)
    {
        var inPlay = Lookup.AliveModels(state, unit.Id).Count > 0;
        return inPlay ? unit.Conditions.Contains(GfNames.Shaken) : !unit.Conditions.Contains(GfNames.SetAside);
    }

    static bool Destroyed(GameState state, UnitInPlay unit) => Lookup.AliveModels(state, unit.Id).Count == 0 && !unit.Conditions.Contains(GfNames.SetAside);

    /// <summary>"Remove it from the table as destroyed": the original's models in play go to the shelf.</summary>
    public static IReadOnlyList<StateChange> RemoveOriginal(GameState state, Guid copy) =>
        state.Projection.Units[copy].Definition.Reserve is { } r && Lookup.AliveModels(state, r.Of) is { Count: > 0 } left
            ? [new ModelsMoved(left.Select(m => new ModelRelocation(m, state.Projection.Models[m].Location, new OnShelf())).ToList())]
            : [];

    /// <summary>"Fully within 12" of any table edge" (3.6: 9"): the whole base inside a strip of that depth along one edge.</summary>
    public static bool NearEdge(TableSize size, PlacedModel placed, double reach = EdgeReach)
    {
        PlacedModel Shift(double dx, double dy) => placed with { Placement = placed.Placement with { Position = placed.Placement.Position with { X = placed.Placement.Position.X - dx, Y = placed.Placement.Position.Y - dy } } };
        var (w, l) = (size.Width, size.Length);
        return Table.ContainsBase(new TableSize(reach, l), placed) || Table.ContainsBase(new TableSize(reach, l), Shift(w - reach, 0))
            || Table.ContainsBase(new TableSize(w, reach), placed) || Table.ContainsBase(new TableSize(w, reach), Shift(0, l - reach));
    }

    /// <summary>A Reinforcement copy's model placed from the shelf must stand fully within 12" of a table edge (3.6: 9"), and under 3.6 over 9" from every
    /// enemy model on the table, base to base (p.5).</summary>
    public static AnswerBreachFound? EdgeBreach(GameState state, IReadOnlyList<ModelRelocation> moves)
    {
        var p = state.Projection;
        var edition = GfEdition.Of(state);
        var reach = edition.ReinforcementEdgeReach;
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model) || model.Location is not OnShelf) continue;
            if (p.Units[model.UnitId].Definition.Reserve is not { Property: Reinforcement }) continue;
            var placed = Battlefield.At(state, model.Id, at);
            if (!NearEdge(state.Board.Table.Size, placed, reach))
                return new(EdgeRule, $"{model.Slot.Name} would be placed beyond {reach:0.#}\" of every table edge: Reinforcement places the new copy fully within {reach:0.#}\" of any table edge");
            if (GfKeepAway.Reinforcement(state, model.UnitId) is { } keep && GfKeepAway.Broken(state, keep, placed, moving) is not null)
            {
                var clear = edition.ReinforcementClearance;
                return new(DistanceRule, $"{model.Slot.Name} would be placed within {clear:0.#}\" of an enemy unit: Reinforcement places the new copy over {clear:0.#}\" away from enemy units");
            }
        }
        return null;
    }

    /// <summary>
    /// 3.6 Reinforcement's "When a unit where all models have this rule is Shaken, you may remove it from the table as destroyed", the morale test's last step
    /// under GfEdition.ReinforcementOnShaken: when this test made the unit Shaken (it rolled, failed, and was neither routed nor held by No Retreat) and its
    /// copy waits, one Ask; "remove" puts its models on the shelf. After a destroyed transport's `place` (p.15: its survivors are Shaken) the same offer
    /// follows the placement (`afterMorale` false).
    /// </summary>
    public sealed class OfferRemovalStep(bool afterMorale = true) : IStep
    {
        public string Name => RemoveStep;
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is { } closed)
            {
                if (closed.Single is not Chosen { OptionId: "remove" }) return new Complete([]);
                ctx.Applied.Add(Reinforcement);
                return new Complete([new ModelsMoved(Lookup.AliveModels(ctx.State, unit).Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())]);
            }
            var steps = ctx.Frame.Frame.Steps;
            var rolled = !afterMorale || steps.Any(s => s.Step == "test" && s.Rolls.Any(r => r.Roll == GfNames.Morale));
            var shook = afterMorale ? steps.LastOrDefault(s => s.Step == "consequence")?.Values is { } v && v.GetValueOrDefault("passed") == 0 && v.GetValueOrDefault("routed") == 0
                : steps.Any(s => s.Step == "place");
            if (!rolled || !shook || !ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken) || Lookup.AliveModels(ctx.State, unit).Count == 0
                || !HoldsAll(ctx.Projection.Units[unit].Definition, Reinforcement) || Waiting(ctx.State, Reinforcement, unit) is null) return new Complete([]);
            return new OpenAsks([ctx.Ask(RemoveQuestion, $"Reinforcement: remove {ctx.Projection.Units[unit].Definition.Name} from the table as destroyed?", ctx.TeamOf(unit)!.Value,
                new Options([new AskOption("remove", "Remove as destroyed", [unit]), new AskOption("pass", "Keep it on the table", [])]))]);
        }
    }
}
