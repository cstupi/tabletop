using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// The end of an activation (army-book rules spec, batch 4): a point window for rows at `activation-end` (Dash: "at the end of this unit's
/// activation, you may place …"), and Coordinate: "At the end of this unit's activation, another friendly unit within 12" that hasn't
/// activated yet may be activated immediately. May not be used if this unit was activated via Coordinate."
/// </summary>
public static class GfActivationEnd
{
    public static Sequence Window() => new(GfNames.ActivationEndSeq, [new GfMoves.PointWindowStep(GfNames.ActivationEnd, "activation-end")]);
    public static Sequence AfterDeploy() => new(GfNames.AfterDeploySeq, [new GfMoves.PointWindowStep(GfNames.AfterDeploy, "after-deploy")]);
    public static Sequence Coordinate() => new(GfNames.CoordinateSeq, [new PickStep(), new ActStep(), new EndStep()]);

    /// <summary>
    /// What runs once an activation's own work is done, before the turn ends, in this order: the activation-end window (its rows), p.10's morale
    /// tests, then Coordinate. Started together, the last started runs first, so they are listed in reverse. A deployment and Delayed Action's pass are
    /// no activation of an action and end with nothing.
    /// </summary>
    public static IReadOnlyList<SequenceStart> Sequences(GameState state, PropertySet props, Scheduler.Activation? activation)
    {
        var actor = activation?.UnitId;
        // Fanatic, Vanguard: "After this model is deployed, it may be placed anywhere fully within 9” of its position": the after-deploy window.
        if (activation is { Action: GfNames.Deploy } deployed)
            return HasRows(state, props, deployed.UnitId, GfNames.AfterDeploy, null) ? [new SequenceStart(GfNames.AfterDeploySeq, new SequenceContext(deployed.UnitId, [], [], null))] : [];
        if (activation is { Action: GfNames.Delay }) return [];
        var list = new List<SequenceStart>();
        if (activation is { } a && Coordinated(state, props, a.UnitId).Count > 0) list.Add(new SequenceStart(GfNames.CoordinateSeq, new SequenceContext(a.UnitId, [], [], GfNames.Coordinate)));
        list.AddRange(GfHooks.EndOfTurnSequences(state, props, actor));
        if (activation is { } b && (HasRows(state, props, b.UnitId, GfNames.ActivationEnd, b.Action) || GfAmbush.MayReDeploy(state, b.UnitId)))
            list.Add(new SequenceStart(GfNames.ActivationEndSeq, new SequenceContext(b.UnitId, [], [], b.Action)));
        return list;
    }

    /// <summary>3.6 Scourge: "enemies count as having taken 2 extra wounds … if shooting wounds would trigger Morale Tests": the wounds count_wounds rows made
    /// each unit count as having taken in the shooting attack-sequences and the spells' hits on it since the actor's activation began (GfAttack.CountedWounds;
    /// owner ruling: a spell's hits count as shooting's do), which p.10's test reads with its wounds this activation (GfHooks.UnitsToTestAtEndOfActivation).
    /// None without an activation in the log.</summary>
    public static IReadOnlyDictionary<Guid, (int Count, IReadOnlyList<string> Rules)> CountedMoraleWounds(IReadOnlyList<GameEvent> log, Guid? actor) =>
        GfAttack.CountedWounds(log, GfHooks.ActivationStart(log, actor), shootingAndSpells: true);

    /// <summary>Whether the unit holds a move or place row at the point now: the window opens only then, so an activation without one logs nothing.</summary>
    public static bool HasRows(GameState state, PropertySet props, Guid unit, string point, string? action)
    {
        if (Lookup.LivingModels(state, unit).Count == 0 || state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return false;
        var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, action, null, Battlefield.TerrainUnder(state, unit), point));
        return live.AtPoint(Side.Attacker, point).Any(x => x.Effect.Kind is EffectKind.Move or EffectKind.Place);
    }

    /// <summary>The units Coordinate may activate now: the unit's team's others within 12" that have not activated this round and are not Shaken
    /// (a Shaken unit's activation is idle, p.10). Empty when the unit does not hold Coordinate or is Shaken ("use any active special rules", p.10).</summary>
    public static IReadOnlyList<UnitInPlay> Coordinated(GameState state, PropertySet props, Guid unit)
    {
        var self = state.Projection.Units[unit];
        if (self.Conditions.Contains(GfNames.Shaken) || !Lookup.UnitHas(state, props, unit, GfNames.Coordinate) || Teams.Of(state, unit) is not { } team) return [];
        return TeamUnits.Of(state, team).Where(u => u.Id != unit && u.Actions.Count == 0 && !u.Conditions.Contains(GfNames.Shaken) && TeamUnits.AnyOnTable(state, u)
            && Battlefield.UnitDistance(state, unit, u.Id) is { } d && d <= CoordinateRange).ToList();
    }

    public const double CoordinateRange = 12;

    /// <summary>Ask which unit and action; the answer records the action (and an Instinctive activation's mark) and resets the wounds-this-activation
    /// counters, which the first activation's morale tests have read.</summary>
    sealed class PickStep : IStep
    {
        public string Name => "coordinate-pick";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var rows = ctx.Properties.Actions.Select(a => a.Name).ToList();
            var options = Coordinated(ctx.State, ctx.Properties, unit)
                .SelectMany(u => { var (actions, note) = GrimdarkFutureRules.ActivationChoices(ctx.State, ctx.Properties, u, rows); return actions.Select(a => ActivationOption.For(u, a, note)); }).ToList();
            if (ctx.History.LastClosed() is { } closed)
            {
                if (GfAnswers.Option(closed) is not { } option || !options.Any(o => o.Id == option.Id) || !ActivationOption.TryParse(option.Id, out var picked, out var action)) return new Finish([]);
                ctx.Applied.Add(GfNames.Coordinate);
                var resets = ctx.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0));
                return new Complete([.. GrimdarkFutureRules.ActivationChanges(ctx.State, picked, action), .. resets]);
            }
            if (options.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("coordinate", "Coordinate: activate another unit", ctx.ActorTeam!.Value, new Options(options), allowPass: true)]);
        }
    }

    static (Guid Unit, string Action)? Picked(FrameView frame) =>
        frame.Frame.Steps.FirstOrDefault(s => s.Step == "coordinate-pick")?.Answer is Chosen c && ActivationOption.TryParse(c.OptionId, out var unit, out var action) ? (unit, action) : null;

    /// <summary>The coordinated unit's action sequence.</summary>
    sealed class ActStep : IStep
    {
        public string Name => "coordinate-act";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0 || Picked(ctx.Frame) is not var (unit, action) || ctx.Rules.ActionSequence(action) is not { } sequence) return new Complete([]);
            return new Push(sequence, new SequenceContext(unit, [], [], action));
        }
    }

    /// <summary>The coordinated activation's own end: its activation-end window, then a morale test for each unit its wounds left at half or less (p.10).</summary>
    sealed class EndStep : IStep
    {
        public string Name => "coordinate-end";
        public StepOutcome Run(StepContext ctx)
        {
            if (Picked(ctx.Frame) is not var (unit, action)) return new Complete([]);
            var log = EventApplier.EffectiveLog(ctx.State.Applier.Log).ToList();
            var from = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == ctx.Frame.Id);
            var started = log.Skip(from + 1).Select(e => e.Payload).OfType<SequenceStarted>().ToList();
            // As at a scheduled activation's end (Sequences): its rows, or Ambush Re-Deployment ("when a unit where all models have this rule ends its activation").
            if (!started.Any(s => s.Sequence == GfNames.ActivationEndSeq)
                && (HasRows(ctx.State, ctx.Properties, unit, GfNames.ActivationEnd, action) || GfAmbush.MayReDeploy(ctx.State, unit)))
                return new Push(GfNames.ActivationEndSeq, new SequenceContext(unit, [], [], action));
            var tested = started.Where(s => s.Sequence == GfNames.MoraleTest).Select(s => s.Context.ActorUnitId).ToHashSet();
            if (GfHooks.UnitsToTestAtEndOfActivation(ctx.State, ctx.Properties, unit).FirstOrDefault(u => !tested.Contains(u)) is { } next && next != Guid.Empty)
                return new Push(GfNames.MoraleTest, new SequenceContext(next, [], [], "wounds"));
            return new Complete([]);
        }
    }
}
