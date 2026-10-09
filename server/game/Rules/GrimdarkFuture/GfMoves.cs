using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMoves
{
    /// <summary>Outline labels: p.7's words for the action ("Hold - Doesn't move, can shoot"; "Advance - Moves 6” and can shoot after moving";
    /// "Rush - Moves 12” but can’t shoot", so Rush's shoot-or-not, reached only through Quick Shot, is not a player step). The dangerous terrain
    /// test, the reaction window and Hold's casting checkpoint are internal.</summary>
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new CastingWindowStep(), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Advance, false).Labelled("Move"), new ShootOrNotStep().Labelled("Shoot")]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Rush, false).Labelled("Move"), new ShootOrNotStep()]);
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

    /// <summary>p.15: a model that can hold others — `Transport` is on it. The (X) is carried and never read; game flow rules capacity unenforced. The name lives here because `Effects/` may not name an OPR rule (constraint 1, Task 18).</summary>
    public static bool IsContainer(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Transport) is not null;

    /// <summary>p.15: a container this unit may be inside — a Transport on the table belonging to another unit of the unit's team. Capacity is not checked (game flow).</summary>
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
            var live = Lone(ctx, unit, ctx.Frame.Context.Action);
            // p.13: Flying ignores terrain effects. A granted Flying is spent when there was a test to skip.
            if (live.Has(Side.Attacker, "Flying"))
            {
                var tested = CountsAsIn(live, TargetTerrain.Dangerous) || StandingInDangerous(ctx.State, unit).Count > 0;
                if (tested) live.Use(Side.Attacker, "Flying");
                return new Complete(RollEffects.Consume(live));
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
            // p.11: "all models in the unit may not move more than 6" for that movement", for a unit that counts as being in difficult terrain.
            var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");
            if (!strider && CountsAsIn(live, TargetTerrain.Difficult)) allowance = Math.Min(allowance, GfNames.DifficultCap);
            var used = ctx.Frame.Value("distance_used_tenths") / 10.0;
            var terrain = Battlefield.Terrain(ctx.State);
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                    var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList();
                    var difficult = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Difficult)).Select(v => v.Id).ToList();
                    var containment = Containers(ctx, unit, action, Math.Max(0, allowance - used), Declared(terrain, TerrainDeclarations.Impassable));
                    var prompt = containment?.ExitingId is not null ? $"Disembark: place every model fully within {GfNames.ExitWithin:0.#}\" of the transport"
                        : ignoreSeparation ? "Charge: move into contact" : $"Move up to {allowance - used:0.#}\"";
                    return new OpenAsks([ctx.Ask("move", prompt, ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), Math.Max(0, allowance - used), impassable, difficult, strider ? null : GfNames.DifficultCap, containment), allowPass: true)]);
                case 0:
                {
                    var (changes, segment, dangerousDice) = ApplyMove(ctx, unit, live, allowance, GfAnswers.Moves(ctx.State, ctx.History.LastClosed("move")!));
                    if (segment > 0) changes.AddRange(RollEffects.Consume(live));
                    return new Complete(changes, new Dictionary<string, int>
                        { ["distance_used_tenths"] = ctx.Frame.Value("distance_used_tenths") + segment, ["dangerous_dice"] = dangerousDice, ["phase"] = dangerousDice > 0 ? 1 : 2 }, Repeat: true);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], action));
                case 1: return new Complete([], new Dictionary<string, int> { ["phase"] = 2 }, Repeat: true);
                default:
                    if (Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) is { } window) return window;
                    // p.13: a caster may try more than one spell in an activation, so a finished cast reopens the window — back to the
                    // move when budget remains, otherwise round again on the checkpoint. A pass leaves no casting child and the step ends.
                    var reacted = ctx.History.Children.Count > 0;
                    return new Complete([], new Dictionary<string, int> { ["phase"] = allowance - used > 0 ? 0 : 2 }, Repeat: reacted);
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

    }


    /// <summary>The destinations settled onto the surface, the moved condition, the segment's distance in tenths, and the dangerous-terrain dice the models that moved in or through a Dangerous volume owe (p.11).</summary>
    public static (List<StateChange> Changes, int SegmentTenths, int DangerousDice) ApplyMove(StepContext ctx, Guid unit, LiveSet live, double allowance, IReadOnlyList<ModelRelocation> proposed)
    {
        var terrain = Battlefield.Terrain(ctx.State);
        var changes = new List<StateChange>(); var used = 0.0; var moves = new List<ModelRelocation>(); var exposed = new List<Guid>(); var crossedDifficult = false;
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
            var settled = Battlefield.Settle(ctx.State, to.Placement);
            used = Math.Max(used, Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, strider ? null : GfNames.DifficultCap, ctx.State.Board.Table.Size).Distance);
            // p.11 is "moves in or through", which the landing position alone does not answer (geometry plan → VolumesCrossed is the in-or-through test).
            if (Movement.VolumesCrossed(start, settled.Position.Horizontal, dangerous).Count > 0) exposed.Add(m.ModelId);
            crossedDifficult |= Movement.VolumesCrossed(start, settled.Position.Horizontal, difficult).Count > 0;
            moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        if (flying && moves.Count > 0) live.Spend(Side.Attacker, "Flying");   // spent by the holder's next applied move, whatever it crosses
        // Applied: Flying when it skipped a difficult cap or a dangerous test (p.13), Strider when it skipped a difficult cap (p.14). A unit holding both crossing difficult ground lists both.
        if (flying && (crossedDifficult || exposed.Count > 0)) live.Use(Side.Attacker, "Flying");
        if (crossedDifficult) live.Use(Side.Attacker, "Strider");
        if (moves.Count > 0) { changes.Add(new ModelsMoved(moves)); changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Moved, true)); }
        return (changes, (int)Math.Round(used * 10), flying ? 0 : DangerousDice(ctx.State, exposed));
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
        public StepOutcome Run(StepContext ctx) => Interpreter.Windows.Run(ctx, point) ?? new Complete([]);
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
                var terrain = Battlefield.Terrain(ctx.State);
                return new OpenAsks([ctx.Ask("move", $"{entry}: move up to {allowance}\"", ctx.ActorTeam!.Value,
                    new MoveRegion(Lookup.AliveModels(ctx.State, unit), allowance, Declared(terrain, TerrainDeclarations.Impassable).Select(v => v.Id).ToList(),
                        Declared(terrain, TerrainDeclarations.Difficult).Select(v => v.Id).ToList(),
                        live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying") ? null : GfNames.DifficultCap), allowPass: true)]);   // the cap ApplyMove holds the move to
            }
            var (changes, _, dice) = ApplyMove(ctx, unit, live, allowance, GfAnswers.Moves(ctx.State, closed));
            if (changes.Count == 0) return new Complete([]);                  // declined, or every landing refused: the grant waits for the next point
            live.Read(source);
            changes.AddRange(RollEffects.Consume(live));
            return new Complete(changes, new Dictionary<string, int> { ["dangerous_dice"] = dice }, Repeat: dice > 0);
        }

        /// <summary>Phase 0: roll the distance when the row has dice (p.4's D3: a D6 halved, rounded up), recorded in tenths on the frame; then ask; then apply.
        /// A declined offer or a landing refused everywhere consumes nothing: a grant is spent only by the placement it makes (the dice are rolled once chosen, never at the offer).</summary>
        static StepOutcome Place(StepContext ctx, Guid unit, string entry, LiveSet live, LiveProperty source, Effect row)
        {
            if (row.AmountDice is not null && ctx.Frame.Value("distance_tenths") == 0)
            {
                // RollPlan wants a roll name's convention; a bare D6 pool has none of its own, so it borrows the hit roll's (a plain d6, no threshold).
                var record = ctx.Roll(new Dice.RollPlan(entry, null, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), row.EffectiveAmount(null));
                return new Complete([], new Dictionary<string, int> { ["distance_tenths"] = record.Dice.Sum(d => (d.Raw + 1) / 2) * 10 }, Repeat: true);
            }
            var distance = row.AmountDice is null ? row.EffectiveAmount(null) : ctx.Frame.Value("distance_tenths") / 10.0;
            var holders = Lookup.AliveModels(ctx.State, unit).Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == entry)).ToList();
            var models = holders.Count > 0 ? holders : Lookup.AliveModels(ctx.State, unit).ToList();   // held by a grant: the whole unit
            if (ctx.History.LastClosed("move") is not { } placed)
                return new OpenAsks([ctx.Ask("move", $"{entry}: place anywhere fully within {distance:0.#}\"", ctx.ActorTeam!.Value,
                    new MoveRegion(models, distance, [], [], null), allowPass: true)]);
            var changes = ApplyPlacement(ctx, unit, distance, models, GfAnswers.Moves(ctx.State, placed)).ToList();
            if (changes.Count == 0) return new Complete([]);
            live.Read(source);
            changes.AddRange(RollEffects.Consume(live));
            return new Complete(changes);
        }

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
    /// stood and its centre is outside every impassable volume. Nothing between is swept. A landing that fails stays where it was. The table edge
    /// and p.7's 1" are client-flagged, as for every move (MoveStep reports Movement.Check's LeavesTable and does not refuse it).</summary>
    public static IReadOnlyList<StateChange> ApplyPlacement(StepContext ctx, Guid unit, double distance, IReadOnlyList<Guid> models, IReadOnlyList<ModelRelocation> proposed)
    {
        var impassable = Declared(Battlefield.Terrain(ctx.State), TerrainDeclarations.Impassable);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => models.Contains(m.ModelId) && m.To is OnTable && ctx.Projection.Models[m.ModelId].Location is OnTable))
        {
            var from = ((OnTable)ctx.Projection.Models[m.ModelId].Location).Placement.Position;
            var settled = Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement);
            var to = settled.Position;
            var displaced = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (displaced > distance + 1e-6) continue;
            if (Containment.VolumesContaining(Battlefield.At(ctx.State, m.ModelId, settled), impassable).Count > 0) continue;
            moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return moves.Count == 0 ? [] : [new ModelsMoved(moves)];
    }

    /// <summary>A move another team makes for this unit (a spell's move kind): the same settling, impassable and difficult checks as an action's
    /// move, measured against the allowance; one chosen by the opponent may not carry a model off the table; dangerous terrain crossed is not tested, since no rule asks it of a forced move.</summary>
    public static IReadOnlyList<StateChange> ApplyForcedMove(StepContext ctx, Guid unit, double allowance, IReadOnlyList<ModelRelocation> proposed)
    {
        var terrain = Battlefield.Terrain(ctx.State);
        var impassable = Declared(terrain, TerrainDeclarations.Impassable); var difficult = Declared(terrain, TerrainDeclarations.Difficult);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => m.To is OnTable && ctx.Projection.Models.TryGetValue(m.ModelId, out var mm) && mm.UnitId == unit))
        {
            if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
            var settled = Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement);
            var check = Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, GfNames.DifficultCap, ctx.State.Board.Table.Size);
            if (check.WithinAllowance && !check.CrossesImpassable && !check.LeavesTable) moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return moves.Count == 0 ? [] : [new ModelsMoved(moves)];
    }
}
