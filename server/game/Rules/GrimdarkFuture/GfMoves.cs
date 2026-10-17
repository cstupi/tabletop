using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>Outline labels: p.7's words for the action ("Hold - Doesn't move, can shoot"; "Advance - Moves 6” and can shoot after moving";
    /// "Rush - Moves 12” but can’t shoot", so Rush's shoot-or-not, reached only through Quick Shot, is not a player step). The dangerous terrain
    /// test, the reaction window and Hold's casting checkpoint are internal.</summary>
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new CastingWindowStep(), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Advance, false).Labelled("Move"), new GfRuleHits.CrossingStep(), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Rush, false).Labelled("Move"), new GfRuleHits.CrossingStep(), new ShootOrNotStep()]);
    /// <summary>p.10's idle activation is an activation (p.11 "or is activated in it"): a Shaken unit idling in dangerous terrain, or counting as in it, still tests.</summary>
    public static Sequence Idle() => new(GfNames.IdleSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start")]);
    public static Sequence DangerousTerrain() => new(GfNames.DangerousTerrain, [new DangerousTestStep(), new GfAttack.PushStep("deal", GfNames.DealWounds, ctx => new SequenceContext(null, [ctx.Frame.Context.ActorUnitId!.Value], [], null))]);

    static LiveSet Lone(StepContext ctx, Guid unit, string? action) => ctx.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, action, null, Battlefield.TerrainUnder(ctx.State, unit)));

    /// <summary>A count_as_terrain row of this type live on the holder (spell library, kind 13), recorded as read so the step that acts spends it.</summary>
    static bool CountsAsIn(LiveSet live, TargetTerrain terrain)
    {
        var rows = live.Effects(Side.Attacker, EffectKind.CountAsTerrain).Where(x => x.Effect.TargetTerrain == terrain).ToList();
        foreach (var x in rows) live.Read(x.Source);
        return rows.Count > 0;
    }

    static IReadOnlyList<WorldVolume> Declared(IReadOnlyList<WorldVolume> terrain, TerrainDeclarations flag) => terrain.Where(v => v.Volume.Declarations.HasFlag(flag)).ToList();

    /// <summary>
    /// p.13 Flying: "Models with this special rule may move through units and terrain"; p.11: "Units may not move through impassable terrain, unless they
    /// have any rules that allow them to ignore it". A Flying unit's move Ask lists no impassable volume to sweep; where it ends is still judged
    /// (LandsInImpassable). Read from the unit's live set, since Flying can be granted.
    /// </summary>
    public static bool Flies(GameState state, PropertySet props, Guid unit) =>
        LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, null, null, Battlefield.TerrainUnder(state, unit))).Has(Side.Attacker, GfNames.Flying);

    /// <summary>The impassable volumes a move of this unit may not sweep: none for a Flying unit (Flies).</summary>
    public static IReadOnlyList<Guid> ImpassableFor(GameState state, bool flying) =>
        flying ? [] : Declared(Battlefield.Terrain(state), TerrainDeclarations.Impassable).Select(v => v.Id).ToList();

    /// <summary>
    /// p.11: units "may not end their move partially overhanging gaps or terrain", and p.7: models may never "be placed in physically impossible
    /// locations". Whether the model's base, at this landing, touches an impassable volume's footprint — the test Movement.Check's sweep makes at
    /// each point, so a unit that may not pass through impassable terrain and one that flies over it are held to the same landing.
    /// </summary>
    public static bool LandsInImpassable(GameState state, Guid modelId, Placement landing, IReadOnlyList<WorldVolume> impassable)
    {
        var at = Battlefield.At(state, modelId, landing);
        return Movement.Check(at, at.Placement.Position.Horizontal, 0, impassable, [], null, null).CrossesImpassable;
    }

    /// <summary>p.11: one die per model, or the model's Tough value in dice.</summary>
    public static int DangerousDice(GameState state, IEnumerable<Guid> models) => models.Sum(m => Lookup.ModelParameter(state, m, "Tough") ?? 1);

    /// <summary>
    /// p.15: "units can't both embark/disembark as part of the same activation". Read from the log, bounded by the activation the
    /// way GfHooks.FoughtMeleeThisActivation is — every relocation into or out of a container is a models_moved (a step's arrives inside its step_completed), so this is a scan,
    /// never a flag. A hand move from the menu counts, which is what a player doing it by hand should expect.
    /// </summary>
    public static (bool Embarked, bool Disembarked) ContainerMovesThisActivation(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        // Anchored on the four root sequences, which only the scheduler starts: nested frames inherit the action name in their context (R-N3).
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted s
            && s.Sequence is GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq && s.Context.ActorUnitId == unit);
        if (activation < 0) return (false, false);
        var mine = Lookup.AliveModels(state, unit).ToHashSet();
        var moves = log.Skip(activation).SelectMany(e => e.Payload is StepCompleted sc ? sc.Changes.Cast<EventPayload>() : [e.Payload]).OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => mine.Contains(r.ModelId)).ToList();
        return (moves.Any(r => r.To is Inside), moves.Any(r => r.From is Inside));
    }

    /// <summary>p.15: a model that can hold others — `Transport` is on it. The (X) is read only for deploying inside (GrimdarkFutureRules.MayDeployInside); game flow leaves capacity unenforced in play. The name lives here because `Effects/` may not name an OPR rule (constraint 1, Task 18).</summary>
    public static bool IsContainer(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Transport) is not null;

    /// <summary>p.15: a container this unit may be inside — a Transport on the table belonging to another unit of the unit's team. Capacity is not checked in play (game flow); deploying inside is (GrimdarkFutureRules.MayDeployInside).</summary>
    public static bool IsFriendlyContainer(GameState state, Guid unit, Guid containerModelId) =>
        state.Projection.Models.TryGetValue(containerModelId, out var c) && c.Location is OnTable && c.UnitId != unit && IsContainer(state, c.Id)
        && Teams.Of(state, c.UnitId) is { } team && team == Teams.Of(state, unit);

    /// <summary>
    /// p.15: the containers a model of this unit could reach — a friendly container (IsFriendlyContainer) within the move's
    /// remaining allowance of one of the unit's models, by a straight path crossing no impassable volume (the same reading as
    /// charge-target's). Only one model has to reach it, so the answer is per container, not per model.
    /// </summary>
    public static IReadOnlyList<Guid> ContainersInReach(StepContext ctx, Guid unit, double remaining, IReadOnlyList<WorldVolume> impassable)
    {
        var movers = Lookup.LivingModels(ctx.State, unit);
        if (movers.Count == 0) return [];
        return ctx.Projection.Models.Values.Where(c => IsFriendlyContainer(ctx.State, unit, c.Id))
            .Where(c => movers.Any(m => Battlefield.Distance(ctx.State, m, c.Id) <= remaining
                && !Movement.Check(Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, c.Id)!.Value.Placement.Position.Horizontal,
                    remaining, impassable, [], null, ctx.State.Board.Table.Size).CrossesImpassable))
            .Select(c => c.Id).ToList();
    }

    /// <summary>
    /// p.15 and p.21: "must stay fully within 6" of it when exiting", p.7: "models may also never move outside of the battlefield (no part
    /// of them, at any point), or be placed in physically impossible locations", and p.11: units "may not end their move partially overhanging
    /// gaps or terrain". The settled placement when the whole base is inside the radius of the container's base, on the table and touching no
    /// impassable volume (LandsInImpassable, whatever the Ask lists: a Flying occupant's lists none), null otherwise. Checked on the server
    /// rather than trusted from the client; a model whose placement is refused simply stays inside (a destroyed transport's survivor is
    /// destroyed instead: GfContainers).
    /// </summary>
    public static Placement? ExitPlacement(GameState state, Guid modelId, Guid container, Placement proposed, double within)
    {
        var settled = Battlefield.Settle(state, modelId, proposed);
        var at = Battlefield.At(state, modelId, settled);
        return Battlefield.Placed(state, container) is { } c && Containment.BaseFullyWithin(at, c, within) && Table.ContainsBase(state.Board.Table.Size, at)
            && !LandsInImpassable(state, modelId, settled, Declared(Battlefield.Terrain(state), TerrainDeclarations.Impassable)) ? settled : null;
    }

    /// <summary>
    /// p.11: "If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6"
    /// for that movement." The region's DifficultCap is what is left of that cap in this segment (null for Strider, Flying and Aircraft, which it does not
    /// bind): once any listed model's straight path from where it stands touches a listed difficult volume, or an earlier segment of the same move did
    /// (the move step's EnteredDifficultKey), every model moving on the table is held to it. Null when none is over. A disembark (no start to measure
    /// from) and an embark are not moves on the table and are not judged.
    /// </summary>
    public static AnswerBreachFound? DifficultCapBreach(GameState state, Ask ask, MoveRegion region, Destinations answer)
    {
        if (region.DifficultCap is not { } cap || region.Containment?.ExitingId is not null) return null;
        var difficult = Battlefield.Terrain(state).Where(v => region.DifficultVolumeIds.Contains(v.Id)).ToList();
        var checks = GfAnswers.Moves(state, region, answer).Where(m => m.To is OnTable && Battlefield.Placed(state, m.ModelId) is not null)
            .Select(m => (m.ModelId, Check: Movement.Check(Battlefield.Placed(state, m.ModelId)!.Value, ((OnTable)m.To).Placement.Position.Horizontal,
                region.Allowance, [], difficult, cap, null))).ToList();
        var earlier = state.Projection.Stack.FirstOrDefault(f => f.Id == ask.FrameId)?.Steps.Any(s => s.Values?.GetValueOrDefault(GfNames.EnteredDifficultKey) > 0) ?? false;
        if (!earlier && !checks.Any(c => c.Check.EntersDifficult)) return null;
        var limit = Math.Min(region.Allowance, cap);
        return checks.FirstOrDefault(c => c.Check.Distance > limit + 1e-6) is { Check: { } check, ModelId: var over }
            ? new(GfNames.DifficultCapRule, $"the unit moved in or through difficult terrain, so no model may move more than {limit:0.#}\" more (p.11), and {state.Projection.Models[over].Slot.Name} moved {check.Distance:0.##}\"")
            : null;
    }

    /// <summary>
    /// The least this model must move under a minimum (MinimumBreach). Where the table has room for the minimum in some direction, the minimum:
    /// p.13 "Aircraft can't move into the table edges to move less than 30"". Where it has room in none (p.6 allows "smaller areas", p.7 keeps
    /// every base on the table), owner ruling: it moves as far as it can in the direction taken, the room towards `toward` (Movement.RoomAlong);
    /// with no direction taken, as far as the table lets its whole base go (Movement.FarthestOnTable).
    /// </summary>
    public static double Minimum(GameState state, PlacedModel start, double minimum, Point2? toward = null)
    {
        var size = state.Board.Table.Size;
        var farthest = Movement.FarthestOnTable(start, size).Distance;
        if (farthest >= minimum) return minimum;
        return toward is { } t && t.DistanceTo(start.Placement.Position.Horizontal) > 1e-6 ? Movement.RoomAlong(start, t, size) : farthest;
    }

    /// <summary>The table has room for this model's minimum in some direction (Minimum).</summary>
    static bool HasRoomFor(GameState state, PlacedModel start, double minimum) => Movement.FarthestOnTable(start, state.Board.Table.Size).Distance >= minimum;

    /// <summary>
    /// A relocation of a listed model that the move steps do not read (GfAnswers.Unread): an Inside the Ask did not offer, a move out of a container
    /// the Ask is not exiting, a move farther than the table's diagonal, or one to the shelf. Refused under strict instead of applied as nothing.
    /// </summary>
    public static AnswerBreachFound? NotOfferedBreach(GameState state, MoveRegion region, Destinations answer) =>
        GfAnswers.Unread(state, region, answer) is { } m
            ? new(GfNames.MoveNotOfferedRule, $"the move Ask does not offer {state.Projection.Models[m.ModelId].Slot.Name} that relocation")
            : null;

    /// <summary>
    /// The geometry of each move on the table, against the Ask's own parameters, read as the steps apply it (GfAnswers.Moves). In order:
    /// p.7 "models may also never move outside of the battlefield (no part of them, at any point)" (a model already standing off the table, which only a
    /// hand move can do, is not judged for it); p.11 "Units may not move through impassable terrain" (the volumes the Ask lists, less any the model's
    /// base already touches where it stands, which again only a hand move can do, so that it can move out); p.7 "as long as no part of their bases
    /// move further than the total movement distance" (the Ask's Allowance); and p.11 "may not end their move partially overhanging gaps or terrain"
    /// with p.7 "physically impossible locations" (LandsInImpassable, against every impassable volume on the table, whether or not the Ask lists it:
    /// a Flying unit's Ask lists none, p.13). An Aircraft's move, its action's or another team's (the region's IgnoresTerrain), is exempt from the
    /// landing: p.13 "Aircraft ignore all units and terrain when moving and stopping". A model answered where it stands has not moved and is not judged. A disembark is
    /// judged by ExitPlacement instead: p.15 "must stay fully within 6" of it when exiting" and p.7's table edge. p.7's contact
    /// clauses are ContactBreach's; coherency is not judged.
    /// </summary>
    public static AnswerBreachFound? PathBreach(GameState state, MoveRegion region, Destinations answer)
    {
        var terrain = Battlefield.Terrain(state);
        var impassable = terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var landing = region.IgnoresTerrain ? [] : Declared(terrain, TerrainDeclarations.Impassable);
        var size = state.Board.Table.Size;
        foreach (var m in GfAnswers.Moves(state, region, answer))
        {
            if (m.To is not OnTable to) continue;
            var name = state.Projection.Models[m.ModelId].Slot.Name;
            if (m.From is Inside from)
            {
                if (ExitPlacement(state, m.ModelId, from.ContainerId, to.Placement, region.Containment?.ExitWithin ?? GfNames.ExitWithin) is null)
                    return new(GfNames.MoveExitRule, $"{name} must be placed fully within {region.Containment?.ExitWithin ?? GfNames.ExitWithin:0.#}\" of the transport, on the table and clear of impassable terrain (p.15, p.7, p.11)");
                continue;
            }
            if (Battlefield.Placed(state, m.ModelId) is not { } start) continue;
            var destination = to.Placement.Position.Horizontal;
            if (start.Placement.Position.Horizontal.DistanceTo(destination) <= 1e-6) continue;
            var swept = impassable.Where(v => !Movement.Check(start, start.Placement.Position.Horizontal, 0, [v], [], null, null).CrossesImpassable).ToList();
            var check = Movement.Check(start, destination, region.Allowance, swept, [], null, size);
            if (check.LeavesTable && Table.ContainsBase(size, start))
                return new(GfNames.MoveOffTableRule, $"{name} would leave the table: models may never move outside of the battlefield (p.7)");
            if (check.CrossesImpassable)
                return new(GfNames.MoveImpassableRule, $"{name} would move through impassable terrain (p.11)");
            if (LandsInImpassable(state, m.ModelId, to.Placement, landing))
                return new(GfNames.MoveImpassableRule, $"{name} would end its move in impassable terrain (p.11, p.7)");
            if (check.Distance > region.Allowance + 1e-6)
                return new(GfNames.MoveAllowanceRule, $"{name} moved {check.Distance:0.##}\", more than the {region.Allowance:0.#}\" allowed (p.7)");
        }
        return null;
    }

    /// <summary>
    /// p.13 Aircraft: "Aircraft can't move into the table edges to move less than 30"" — the move is mandatory. The region's MinDistance is that 30";
    /// every listed model on the table must move at least that far. Where the table has no room for it in any direction, the model must go as far
    /// as it can in the direction taken (Minimum): p.13 assumes 30" of room, which p.6's 6'x4' table always has, but p.6 allows "smaller areas"
    /// and p.7 keeps every base on the table. The answer is read as the move step applies it (GfAnswers.Moves): a model
    /// it leaves out, or whose relocation that reading drops, has not moved. Null when every model went far enough.
    /// </summary>
    public static AnswerBreachFound? MinimumBreach(GameState state, MoveRegion region, Destinations answer)
    {
        if (region.MinDistance is not { } minimum) return null;
        var moves = GfAnswers.Moves(state, region, answer);
        foreach (var id in region.ModelIds)
        {
            if (Battlefield.Placed(state, id) is not { } start) continue;
            var to = moves.FirstOrDefault(m => m.ModelId == id)?.To is OnTable on ? on.Placement.Position.Horizontal : start.Placement.Position.Horizontal;
            var required = Minimum(state, start, minimum, to);
            var moved = start.Placement.Position.Horizontal.DistanceTo(to);
            if (moved < required - 1e-6)
                return new(GfNames.AircraftMinimumRule, $"an Aircraft must move at least {required:0.#}\" (p.13), and {state.Projection.Models[id].Slot.Name} moved {moved:0.##}\"");
        }
        return null;
    }

    /// <summary>p.11 "is activated in it": the unit's living models whose base centre is inside a Dangerous volume right now.</summary>
    public static IReadOnlyList<Guid> StandingInDangerous(GameState state, Guid unit)
    {
        var dangerous = Declared(Battlefield.Terrain(state), TerrainDeclarations.Dangerous);
        return dangerous.Count == 0 ? [] : Lookup.LivingModels(state, unit)
            .Where(m => Containment.VolumesContaining(Battlefield.Placed(state, m)!.Value, dangerous).Count > 0).ToList();
    }

    /// <summary>p.11's third trigger, the first step of every action sequence: a model activated in dangerous terrain tests before it does anything else.</summary>
    public sealed class ActivationTerrainStep : IStep
    {
        public string Name => "activation-terrain";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            // A choice made at the unit's last activation lasts until this one (Versatile Defense: "until the units' next activation"): cleared first.
            // Instinctive's mark is written as the activation starts, when "it is able to shoot/charge an enemy unit" is read.
            if (ctx.Frame.Value("dangerous_dice") == 0 && ctx.Frame.Value("activation_marked") == 0)
            {
                var marks = GfChoice.Cleared(ctx.State, ctx.Properties, unit).ToList();
                if (GrimdarkFutureRules.InstinctiveAttack(ctx.State, ctx.Properties, ctx.Projection.Units[unit]) is not null)
                    marks.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.InstinctiveCondition, true));
                // Reanimation: "When a unit … is activated, roll as many dice as … it could restore" (GfReanimation).
                marks.AddRange(GfReanimation.Reanimate(ctx, unit));
                if (marks.Count > 0 || ctx.Rolls.Count > 0) return new Complete(marks, new Dictionary<string, int> { ["activation_marked"] = 1 }, Repeat: true);
            }
            var live = Lone(ctx, unit, ctx.Frame.Context.Action);
            var rules = TerrainFor(ctx, unit, live);
            if (rules.Aircraft) return new Complete([]);                                                 // p.13: Aircraft "ignore all units and terrain when moving and stopping"
            // p.13: Flying ignores terrain effects. A granted Flying applies when there was a test to skip, and is kept to the activation's end (ConsumeKeepingFlying).
            if (rules.IgnoresDangerous)
            {
                var tested = CountsAsIn(live, TargetTerrain.Dangerous) || StandingInDangerous(ctx.State, unit).Count > 0;
                if (tested) live.Use(Side.Attacker, GfNames.Flying);
                var (spent, held) = ConsumeKeepingFlying(live);
                return new Complete(spent, held);
            }
            // Write the dice count first, then push: the child reads dangerous_dice off this frame, so it must be settled before the push.
            // The repeat run checks it before re-reading who stands: the first run spent a count_as_terrain grant, so nobody would stand now.
            if (ctx.Frame.Value("dangerous_dice") > 0) return new Push(GfNames.DangerousTerrain, ctx.Frame.Context);
            // p.11 "or is activated in it": a unit that counts as being in dangerous terrain tests every model, as if all stood in it.
            var standing = CountsAsIn(live, TargetTerrain.Dangerous) ? Lookup.LivingModels(ctx.State, unit) : StandingInDangerous(ctx.State, unit);
            if (standing.Count == 0) return new Complete([]);
            return new Complete(RollEffects.Consume(live), new Dictionary<string, int> { ["dangerous_dice"] = DangerousDice(ctx.State, standing) }, Repeat: true);
        }
    }

    /// <summary>The casting checkpoint on its own: Hold has no move to hang it on. p.13 allows more than one spell, so a finished cast reopens the window with a fresh history.</summary>
    public sealed class CastingWindowStep : IStep
    {
        public string Name => "casting-window";
        public StepOutcome Run(StepContext ctx) =>
            Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) ?? new Complete([], null, Repeat: ctx.History.Children.Count > 0);
    }

    /// <summary>
    /// p.13 Aircraft: "May only use Advance actions, moving in a straight line, and adding 30" to its total move (even if Shaken). Aircraft ignore all
    /// units and terrain when moving and stopping, can't seize or contest objectives, can't be charged, and units targeting them get -12" to their range.
    /// Note that since Aircraft are flying high above the ground, only the actual model counts (base doesn't block movement or line of sight, and units
    /// may move and shoot under it). Aircraft can't move into the table edges to move less than 30", and their mandatory move doesn't break the staying
    /// idle requirement to stop being Shaken." The move is the move step's: 30" on the allowance, no terrain volumes and no cap, no dangerous terrain
    /// test, and one segment, so each model's move is one straight line. Advance only is ActionsFor's; "can't be charged" is GfMelee.ChargeTargets'.
    /// </summary>
    public static bool IsAircraft(StepContext ctx, Guid unit) => Lookup.UnitHas(ctx.State, ctx.Properties, unit, GfNames.Aircraft);

    /// <summary>The destinations settled onto the surface, the moved condition, the segment's distance in tenths, the dangerous-terrain dice the models that moved in or
    /// through a Dangerous volume owe (p.11), and whether a model went in or through difficult ground with the cap binding the unit (p.11, not Strider or Flying).</summary>
    public static (List<StateChange> Changes, int SegmentTenths, int DangerousDice, bool EnteredDifficult) ApplyMove(StepContext ctx, Guid unit, LiveSet live, double allowance, IReadOnlyList<ModelRelocation> proposed)
    {
        // p.13: an Aircraft ignores all terrain when moving, so no volume is swept for it.
        var rules = TerrainFor(ctx, unit, live);
        var aircraft = rules.Aircraft;
        var terrain = SweptTerrain(ctx.State, rules);
        var changes = new List<StateChange>(); var used = 0.0; var moves = new List<ModelRelocation>(); var exposed = new List<Guid>(); var crossedDifficult = false;
        var impassable = Declared(terrain, TerrainDeclarations.Impassable);
        var difficult = Declared(terrain, TerrainDeclarations.Difficult);
        var dangerous = Declared(terrain, TerrainDeclarations.Dangerous);
        var strider = rules.IgnoresDifficult;   // p.14: Strider ignores difficult terrain, Flying all terrain effects
        var flying = rules.Flying;              // Strider does *not* skip a dangerous terrain test; only Flying does
        foreach (var m in proposed)
        {
            // p.15: embarking costs no measured distance and sweeps no path — "only one model needs to reach the Transport for
            // everyone to embark", and the 1" restriction is ignored (ContactBreach judges only a landing on the table).
            if (m.To is Inside into)
            {
                if (ctx.Projection.Models.TryGetValue(into.ContainerId, out var container) && container.Location is OnTable) { moves.Add(m); ctx.Applied.Add(GfNames.Transport); }   // never point Inside at something that is not on the table
                continue;
            }
            if (m.To is not OnTable to) continue;
            // p.14: a disembarking model has no start position, so nothing is measured or swept; the radius is the whole constraint.
            if (m.From is Inside from)
            {
                if (!ctx.Projection.Models.ContainsKey(from.ContainerId)) continue;
                if (ExitPlacement(ctx.State, m.ModelId, from.ContainerId, to.Placement, GfNames.ExitWithin) is { } exit)
                { moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(exit))); ctx.Applied.Add(GfNames.Transport); }
                continue;
            }
            if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
            var settled = Battlefield.Settle(ctx.State, m.ModelId, to.Placement);
            used = Math.Max(used, Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, strider ? null : GfNames.DifficultCap, ctx.State.Board.Table.Size).Distance);
            // p.11 is "moves in or through", which the landing position alone does not answer (geometry plan → VolumesCrossed is the in-or-through test).
            if (Movement.VolumesCrossed(start, settled.Position.Horizontal, dangerous).Count > 0) exposed.Add(m.ModelId);
            crossedDifficult |= Movement.VolumesCrossed(start, settled.Position.Horizontal, difficult).Count > 0;
            moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        if (aircraft && moves.Any(m => m.From is OnTable && m.To is OnTable)) ctx.Applied.Add(GfNames.Aircraft);
        if (flying && moves.Count > 0) live.Spend(Side.Attacker, GfNames.Flying);   // used by any applied move, whatever it crosses; a granted one is kept to the activation's end (ConsumeKeepingFlying)
        // Applied, recorded without spending: Flying when it skipped a dangerous test (p.13) or a difficult cap that would have bound, Strider when it
        // skipped that cap (p.14). The cap binds only when the allowance is over it, so an Advance across difficult ground lists neither.
        var capSkipped = crossedDifficult && allowance > GfNames.DifficultCap;
        if (flying && (capSkipped || exposed.Count > 0)) live.Record(Side.Attacker, GfNames.Flying);
        if (capSkipped) live.Record(Side.Attacker, GfNames.Strider);
        if (moves.Count > 0) { changes.Add(new ModelsMoved(moves)); changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Moved, true));
            if (!ctx.Projection.Units[unit].Conditions.Contains(GfNames.MovedThisRound)) changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.MovedThisRound, true)); }
        return (changes, (int)Math.Round(used * 10), rules.IgnoresDangerous ? 0 : DangerousDice(ctx.State, exposed), crossedDifficult && !strider);
    }

    /// <summary>p.7–8: Hold and Advance may shoot; a Rush reaches this step too and is offered only what ValidTargets admits (p.7 "can't shoot", unless Quick Shot). p.10: a Shaken unit spends the activation idle and stops being Shaken.</summary>
    public sealed class ShootOrNotStep : IStep
    {
        public string Name => "shoot-or-not";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return new Finish([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, false)]);
            if (ctx.History.Children.Count > 0) return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete([]) : new OpenAsks([]);
            if (ctx.History.LastClosed() is { } closed) return closed.Single is Chosen { OptionId: "shoot" } ? new Push(GfNames.Shooting, ctx.Frame.Context) : new Complete([]);
            var canShoot = GfShooting.Groups(ctx.State, ctx.Properties, unit, null).Any(g => GfShooting.ValidTargets(ctx, unit, g).Count > 0);
            if (!canShoot) return new Complete([]);
            // Instinctive: "it must immediately attack the closest valid target".
            if (ctx.Projection.Units[unit].Conditions.Contains(GfNames.InstinctiveCondition)) return new Push(GfNames.Shooting, ctx.Frame.Context);
            return new OpenAsks([ctx.Ask("shoot_or_not", "Shoot?", ctx.ActorTeam!.Value, new Options([new AskOption("shoot", "Shoot", [unit]), new AskOption("pass", "Don't shoot", [])]))]);
        }
    }

    /// <summary>p.11: one die per *affected* model (or that model's Tough value in dice); each 1 is a wound. The pushing step settled how many in Values["dangerous_dice"]; the fallback is the models standing in it now. Values["wounds"] feeds deal-wounds with no attacker.</summary>
    public sealed class DangerousTestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var owed = ctx.Frame.Parent?.Value("dangerous_dice") ?? 0;
            var dice = owed > 0 ? owed : DangerousDice(ctx.State, StandingInDangerous(ctx.State, unit));
            if (dice == 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var record = ctx.Roll(new Dice.RollPlan(GfNames.DangerousTerrain, 2, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
            var wounds = Dice.RollOutcome.Failures(record);
            return wounds == 0 ? new Finish([], new Dictionary<string, int> { ["wounds"] = 0 }) : new Complete([], new Dictionary<string, int> { ["wounds"] = wounds });
        }
    }

    /// <summary>A reaction window at a named point (army-rule hooks → Hook points): `after-shooting`, `after-melee`.</summary>
    public sealed class PointWindowStep(string point, string name) : IStep
    {
        public string Name => name;
        /// <summary>A use taken here reopens the window with a fresh history, as the casting window does, so a unit holding several rules at the point is
        /// offered each; a rule's own usage_count keeps it from being offered again.</summary>
        public StepOutcome Run(StepContext ctx) => Interpreter.Windows.Run(ctx, point) ?? new Complete([], null, Repeat: ctx.History.Children.Count > 0);
    }

    public static Sequence Reposition() => new(GfNames.RepositionSeq, [new RepositionStep()]);

    /// <summary>
    /// A move a rule offered at a point (army-rule hooks spec). The context names the unit and, as its Action, the rule — which is how usage_count counts
    /// this use. The row is re-read from the live set with every condition but at_point and usage_count judged, since this frame's own start is now a use.
    /// Phases: ask (answered Passed ends the step and consumes nothing), apply and consume a grant (Repeat), then the dangerous terrain test (Push).
    /// The dice are checked before the row is re-read: the apply spent a granted instance, so nothing is offered on the repeat.
    /// </summary>
    public sealed class RepositionStep : IStep
    {
        public string Name => "reposition";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value; var entry = ctx.Frame.Context.Action!;
            if (ctx.Frame.Value("dangerous_dice") > 0) return ctx.History.Children.Count == 0 ? new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], entry)) : new Complete([]);
            var live = Lone(ctx, unit, ActionOf(ctx.Frame));
            if (Offered(live, entry) is not var (source, row)) return new Finish([]);
            if (row.Kind == EffectKind.Place) return Place(ctx, unit, entry, live, source, row);
            var allowance = row.EffectiveAmount(null);
            if (ctx.History.LastClosed("move") is not { } closed)
            {
                var rules = TerrainFor(ctx, unit, live);
                var (impassable, difficult) = RegionVolumes(ctx.State, rules);
                return new OpenAsks([ctx.Ask("move", $"{entry}: move up to {allowance}\"", ctx.ActorTeam!.Value,
                    new MoveRegion(Lookup.AliveModels(ctx.State, unit), allowance, impassable, difficult, rules.DifficultCap, Separation: rules.Aircraft ? null : GfNames.Separation, PassesModels: rules.PassesImpassable, IgnoresTerrain: rules.IgnoresAllTerrain), allowPass: true)]);   // the cap ApplyMove holds the move to
            }
            var (changes, _, dice, _) = ApplyMove(ctx, unit, live, allowance, GfAnswers.Moves(ctx.State, closed));
            if (changes.Count == 0) return new Complete([]);                  // declined, or every landing refused: the grant waits for the next point
            live.Read(source);
            var (spent, held) = ConsumeKeepingFlying(live);
            changes.AddRange(spent);
            held["dangerous_dice"] = dice;
            return new Complete(changes, held, Repeat: dice > 0);
        }

        /// <summary>Phase 0: roll the distance when the row has dice (p.4's D3: a D6 halved, rounded up), recorded in tenths on the frame; then ask; then apply.
        /// A declined offer or a landing refused everywhere consumes nothing: a grant is spent only by the placement it makes (the dice are rolled once chosen, never at the offer).</summary>
        static StepOutcome Place(StepContext ctx, Guid unit, string entry, LiveSet live, LiveProperty source, Effect row)
        {
            if (row.AmountDice is not null && ctx.Frame.Value("distance_tenths") == 0)
            {
                // RollPlan wants a roll name's convention; a bare D6 pool has none of its own, so it borrows the hit roll's (a plain d6, no threshold).
                var record = ctx.Roll(new Dice.RollPlan(entry, null, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), row.EffectiveAmount(null));
                // "D3+1" (Bounding, Dash): each die's D3 plus one.
                var plus = row.AmountDice == "d3+1" ? record.Dice.Count : 0;
                return new Complete([], new Dictionary<string, int> { ["distance_tenths"] = (record.Dice.Sum(d => (d.Raw + 1) / 2) + plus) * 10 }, Repeat: true);
            }
            var distance = row.AmountDice is null ? row.EffectiveAmount(null) : ctx.Frame.Value("distance_tenths") / 10.0;
            var holders = Lookup.AliveModels(ctx.State, unit).Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == entry)).ToList();
            var models = holders.Count > 0 ? holders : Lookup.AliveModels(ctx.State, unit).ToList();   // held by a grant: the whole unit
            if (ctx.History.LastClosed("move") is not { } placed)
                return new OpenAsks([ctx.Ask("move", $"{entry}: place anywhere fully within {distance:0.#}\"", ctx.ActorTeam!.Value,
                    PlacementRegion(models, distance, IsAircraft(ctx, unit)), allowPass: true)]);
            var changes = ApplyPlacement(ctx, unit, distance, models, GfAnswers.Moves(ctx.State, placed)).ToList();
            if (changes.Count == 0) return new Complete([]);
            live.Read(source);
            changes.AddRange(RollEffects.Consume(live));
            return new Complete(changes);
        }

        /// <summary>A placement has no path (PassesModels); its landing is judged by move_overlap, by move_separation and against impassable terrain,
        /// except an Aircraft's, p.13: "Aircraft ignore all units and terrain when moving and stopping".</summary>
        internal static MoveRegion PlacementRegion(IReadOnlyList<Guid> models, double distance, bool aircraft) =>
            new(models, distance, [], [], null, Separation: aircraft ? null : GfNames.Separation, PassesModels: true, IgnoresTerrain: aircraft);

        /// <summary>The first move or place row of the rule whose conditions other than at_point and usage_count hold.</summary>
        internal static (LiveProperty Source, Effect Row)? Offered(LiveSet live, string entry) =>
            live.On(Side.Attacker).Where(p => p.Entry.Name == entry)
                .SelectMany(p => p.Effects.Where(e => e.Kind is EffectKind.Move or EffectKind.Place).Select(e => (p, e)))
                .Where(x => x.e.Conditions.Where(c => c.Kind is not ConditionKind.AtPoint and not ConditionKind.UsageCount).All(c => Conditions.Holds(live.Interaction, x.p.Holder, c, x.p, null)))
                .Select(x => ((LiveProperty, Effect)?)(x.p, x.e)).FirstOrDefault();

        /// <summary>The action of the activation this frame is inside: its nearest ancestor action sequence's context.</summary>
        static string? ActionOf(FrameView frame)
        {
            for (var f = frame.Parent; f is not null; f = f.Parent)
                if (f.Frame.Sequence is GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq or GfNames.IdleSeq) return f.Frame.Context.Action;
            return null;
        }
    }

    /// <summary>A placement (army-rule hooks → A placement): each listed model's landing, settled on the surface, is at most `distance` from where it
    /// stood and its base touches no impassable volume (LandsInImpassable, as PathBreach judges it), unless the unit is an Aircraft (p.13 "Aircraft ignore
    /// all units and terrain when moving and stopping"). Terrain and models between are not swept. A landing that fails stays where it was. Under strict,
    /// dispatch also refuses a landing off the table (PathBreach, p.7); p.7's contact clauses are ContactBreach's, as for every move.</summary>
    public static IReadOnlyList<StateChange> ApplyPlacement(StepContext ctx, Guid unit, double distance, IReadOnlyList<Guid> models, IReadOnlyList<ModelRelocation> proposed)
    {
        IReadOnlyList<WorldVolume> impassable = IsAircraft(ctx, unit) ? [] : Declared(Battlefield.Terrain(ctx.State), TerrainDeclarations.Impassable);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => models.Contains(m.ModelId) && m.To is OnTable && ctx.Projection.Models[m.ModelId].Location is OnTable))
        {
            var from = ((OnTable)ctx.Projection.Models[m.ModelId].Location).Placement.Position;
            var settled = Battlefield.Settle(ctx.State, m.ModelId, ((OnTable)m.To).Placement);
            var to = settled.Position;
            var displaced = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (displaced > distance + 1e-6) continue;
            if (LandsInImpassable(ctx.State, m.ModelId, settled, impassable)) continue;
            moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return moves.Count == 0 ? [] : [new ModelsMoved(moves)];
    }
}
