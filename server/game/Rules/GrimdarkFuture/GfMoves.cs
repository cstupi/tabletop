using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMoves
{
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new CastingWindowStep(), new ShootOrNotStep()]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Advance, false), new ShootOrNotStep()]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Rush, false)]);
    public static Sequence DangerousTerrain() => new(GfNames.DangerousTerrain, [new DangerousTestStep(), new GfAttack.PushStep("deal", GfNames.DealWounds, ctx => new SequenceContext(null, [ctx.Frame.Context.ActorUnitId!.Value], [], null))]);

    static LiveSet Lone(StepContext ctx, Guid unit, string? action) => LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, action, null, Battlefield.TerrainUnder(ctx.State, unit)));

    static IReadOnlyList<WorldVolume> Declared(IReadOnlyList<WorldVolume> terrain, TerrainDeclarations flag) => terrain.Where(v => v.Volume.Declarations.HasFlag(flag)).ToList();

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

    /// <summary>
    /// p.15: the containers a model of this unit could reach — a friendly model with Transport(X) on the table, within the move's
    /// remaining allowance of one of the unit's models, by a straight path crossing no impassable volume (the same reading as
    /// charge-target's). Only one model has to reach it, so the answer is per container, not per model. Capacity is not checked:
    /// game flow rules it unenforced and Transport's (X) is carried unread.
    /// </summary>
    /// <summary>p.15: a model that can hold others — `Transport` is on it. The (X) is carried and never read; game flow rules capacity unenforced. The name lives here because `Effects/` may not name an OPR rule (constraint 1, Task 18).</summary>
    public static bool IsContainer(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Transport) is not null;

    public static IReadOnlyList<Guid> ContainersInReach(StepContext ctx, Guid unit, double remaining, IReadOnlyList<WorldVolume> impassable)
    {
        var team = ctx.TeamOf(unit);
        var movers = Lookup.LivingModels(ctx.State, unit);
        if (movers.Count == 0) return [];
        return ctx.Projection.Models.Values.Where(c => c.Location is OnTable && c.UnitId != unit && IsContainer(ctx.State, c.Id) && ctx.TeamOf(c.UnitId) == team)
            .Where(c => movers.Any(m => Battlefield.Distance(ctx.State, m, c.Id) <= remaining
                && !Movement.Check(Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, c.Id)!.Value.Placement.Position.Horizontal,
                    remaining, impassable, [], null, ctx.State.Board.Table.Size).CrossesImpassable))
            .Select(c => c.Id).ToList();
    }

    /// <summary>
    /// p.15 and p.21: "must stay fully within 6" of it when exiting". The settled placement when the whole base is inside the
    /// radius of the container's base, null otherwise — the one containment constraint that is geometry, so it is checked on the
    /// server rather than trusted from the client, and a model whose placement is refused simply stays inside.
    /// </summary>
    public static Placement? ExitPlacement(GameState state, Guid modelId, Guid container, Placement proposed, double within)
    {
        var settled = Battlefield.Settle(state, proposed);
        return Battlefield.Placed(state, container) is { } c && Containment.BaseFullyWithin(Battlefield.At(state, modelId, settled), c, within) ? settled : null;
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
            if (Lone(ctx, unit, ctx.Frame.Context.Action).Has(Side.Attacker, "Flying")) return new Complete([]);   // p.13: ignores terrain effects
            var standing = StandingInDangerous(ctx.State, unit);
            if (standing.Count == 0) return new Complete([]);
            // Write the dice count first, then push: the child reads dangerous_dice off this frame, so it must be settled before the push.
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = DangerousDice(ctx.State, standing) }, Repeat: true);
        }
    }

    /// <summary>The casting checkpoint on its own: Hold has no move to hang it on. p.13 allows more than one spell, so a finished cast reopens the window with a fresh history.</summary>
    public sealed class CastingWindowStep : IStep
    {
        public string Name => "casting-window";
        public StepOutcome Run(StepContext ctx) =>
            Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) ?? new Complete([], null, Repeat: ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq));
    }

    /// <summary>
    /// One segment per run of its phase machine, carried in Values["phase"]: 0 ask for a move → apply it (Repeat); 1 dangerous terrain test (Push) → (Repeat);
    /// 2 the casting checkpoint: react-or-pass to the actor's own team; a cast with budget left goes back to phase 0 (Repeat), otherwise the step completes.
    /// </summary>
    public sealed class MoveStep(string action, bool ignoreSeparation) : IStep
    {
        public string Name => "move";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var live = Lone(ctx, unit, action);
            var allowance = StatReader.ActionDistance(live, Side.Attacker, action);
            var used = ctx.Frame.Value("distance_used_tenths") / 10.0;
            var terrain = Battlefield.Terrain(ctx.State);
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                    var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList();
                    var difficult = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Difficult)).Select(v => v.Id).ToList();
                    var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");
                    var containment = Containers(ctx, unit, action, Math.Max(0, allowance - used), Declared(terrain, TerrainDeclarations.Impassable));
                    var prompt = containment?.ExitingId is not null ? $"Disembark: place every model fully within {GfNames.ExitWithin:0.#}\" of the transport"
                        : ignoreSeparation ? "Charge: move into contact" : $"Move up to {allowance - used:0.#}\"";
                    return new OpenAsks([ctx.Ask("move", prompt, ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), Math.Max(0, allowance - used), impassable, difficult, strider ? null : GfNames.DifficultCap, containment), allowPass: true)]);
                case 0:
                {
                    var (changes, segment, dangerousDice) = Apply(ctx, unit, live, allowance, GfAnswers.Moves(ctx.State, ctx.History.LastClosed("move")!), terrain);
                    return new Complete(changes, new Dictionary<string, int>
                        { ["distance_used_tenths"] = ctx.Frame.Value("distance_used_tenths") + segment, ["dangerous_dice"] = dangerousDice, ["phase"] = dangerousDice > 0 ? 1 : 2 }, Repeat: true);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], action));
                case 1: return new Complete([], new Dictionary<string, int> { ["phase"] = 2 }, Repeat: true);
                default:
                    if (Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) is { } window) return window;
                    // p.13: a caster may try more than one spell in an activation, so a finished cast reopens the window — back to the
                    // move when budget remains, otherwise round again on the checkpoint. A pass leaves no casting child and the step ends.
                    var cast = ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq);
                    return new Complete([], new Dictionary<string, int> { ["phase"] = allowance - used > 0 ? 0 : 2 }, Repeat: cast);
            }
        }

        /// <summary>
        /// p.15: what containment this segment allows. Inside a container → the exit, unless the unit embarked earlier in this
        /// activation. Outside → the containers in reach, unless it disembarked earlier in this activation, and never on a Charge —
        /// p.15 lets a Charge disembark, and charging a transport is an attack on it, not a way into it. Null when containment plays
        /// no part in this move, which is every move on open table. This is where "never both in one activation" is enforced.
        /// </summary>
        static MoveContainment? Containers(StepContext ctx, Guid unit, string action, double remaining, IReadOnlyList<WorldVolume> impassable)
        {
            var (embarked, disembarked) = ContainerMovesThisActivation(ctx.State, unit);
            if (Lookup.ContainerOf(ctx.State, unit) is { } inside)
                return embarked ? null : new MoveContainment([], inside, GfNames.ExitWithin);
            if (disembarked || action == GfNames.Charge) return null;
            var enterable = ContainersInReach(ctx, unit, remaining, impassable);
            return enterable.Count > 0 ? new MoveContainment(enterable, null, null) : null;
        }
        // Allowance, DifficultVolumeIds and DifficultCap are unbound whenever Containment.ExitingId is set:
        // an occupant has no position, so nothing is swept and no distance is measured from a start. The
        // region is the container's base grown by ExitWithin. The three are still populated from the action
        // so the record has one shape, and the client ignores them on a disembark (client shell -> MoveRuler).

        /// <summary>The destinations settled onto the surface, the moved condition, the segment's distance in tenths, and the dangerous-terrain dice the models that moved in or through a Dangerous volume owe (p.11).</summary>
        static (List<StateChange> Changes, int SegmentTenths, int DangerousDice) Apply(StepContext ctx, Guid unit, LiveSet live, double allowance, IReadOnlyList<ModelRelocation> proposed, IReadOnlyList<WorldVolume> terrain)
        {
            var changes = new List<StateChange>(); var used = 0.0; var moves = new List<ModelRelocation>(); var exposed = new List<Guid>();
            var impassable = Declared(terrain, TerrainDeclarations.Impassable);
            var difficult = Declared(terrain, TerrainDeclarations.Difficult);
            var dangerous = Declared(terrain, TerrainDeclarations.Dangerous);
            var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");   // p.14: Strider ignores difficult terrain, Flying all terrain effects
            var flying = live.Has(Side.Attacker, "Flying");                                          // Strider does *not* skip a dangerous terrain test; only Flying does
            foreach (var m in proposed)
            {
                // p.15: embarking costs no measured distance and sweeps no path — "only one model needs to reach the Transport for
                // everyone to embark", and the 1" restriction is ignored (it is client-flagged here anyway; architecture.md → Rules enforcement).
                if (m.To is Inside into)
                {
                    if (ctx.Projection.Models.TryGetValue(into.ContainerId, out var container) && container.Location is OnTable) moves.Add(m);          // never point Inside at something that is not on the table
                    continue;
                }
                if (m.To is not OnTable to) continue;
                // p.14: a disembarking model has no start position, so nothing is measured or swept; the radius is the whole constraint.
                if (m.From is Inside from)
                {
                    if (!ctx.Projection.Models.ContainsKey(from.ContainerId)) continue;
                    if (ExitPlacement(ctx.State, m.ModelId, from.ContainerId, to.Placement, GfNames.ExitWithin) is { } exit)
                        moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(exit)));
                    continue;
                }
                if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
                var settled = Battlefield.Settle(ctx.State, to.Placement);
                used = Math.Max(used, Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, strider ? null : GfNames.DifficultCap, ctx.State.Board.Table.Size).Distance);
                // p.11 is "moves in or through", which the landing position alone does not answer (geometry plan → VolumesCrossed is the in-or-through test).
                if (Movement.VolumesCrossed(start, settled.Position.Horizontal, dangerous).Count > 0) exposed.Add(m.ModelId);
                moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
            }
            if (moves.Count > 0) { changes.Add(new ModelsMoved(moves)); changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Moved, true)); }
            return (changes, (int)Math.Round(used * 10), flying ? 0 : DangerousDice(ctx.State, exposed));
        }
    }

    /// <summary>p.7–8: Hold and Advance may shoot. p.10: a Shaken unit spends the activation idle and stops being Shaken.</summary>
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
}
