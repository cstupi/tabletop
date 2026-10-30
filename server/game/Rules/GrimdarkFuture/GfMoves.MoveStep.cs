using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// One segment per run of its phase machine, carried in Values["phase"]: 0 ask for a move → apply it (Repeat); 1 dangerous terrain test (Push) → (Repeat);
    /// 2 the casting checkpoint: react-or-pass to the actor's own team; a cast, or a stop (Destinations.Continues), with budget left goes back to phase 0
    /// (Repeat), otherwise the step completes. p.13 Caster(X) is "at any point before attacking", so a stop with the models where they stand is how a
    /// unit casts before it moves; it is not a move. An Aircraft moves in one segment (IsAircraft) and gets the checkpoint before it as well, in
    /// Values["before_move"].
    /// </summary>
    public sealed class MoveStep(string action, bool ignoreSeparation) : IStep
    {
        public string Name => "move";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var live = Lone(ctx, unit, action);
            var rules = TerrainFor(ctx, unit, live);
            var aircraft = rules.Aircraft;
            // A charge's distance at its target: the target's rules may take from it (Melee Shrouding).
            var target = action == GfNames.Charge ? GfAnswers.Pieces(ctx.State, ctx.Frame, "charge-target", "charge_target").FirstOrDefault() : Guid.Empty;
            var own = StatReader.ActionDistance(live, Side.Attacker, action);   // read on the unit's own set too, which spends what it read
            var allowance = (target != Guid.Empty && ctx.Projection.Units.ContainsKey(target)
                ? StatReader.ActionDistance(ctx.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), Party.Whole(ctx.State, target), AttackKind.Melee, GfNames.Charge, null, [])), Side.Attacker, action)
                : own) + (aircraft ? GfNames.AircraftMove : 0);
            // A once grant read here acts when the unit moves, where the segment spends it: listed as applied there, not on every run of the step
            // (a window, a dangerous test, a segment that went nowhere and keeps it).
            var granted = live.GrantsRead.Select(r => ctx.Projection.Units.GetValueOrDefault(r.UnitId)?.Grants.FirstOrDefault(g => g.Id == r.GrantId)?.EntryName)
                .OfType<string>().Distinct().ToList();
            foreach (var name in granted) ctx.Applied.Remove(name);
            // p.11: "all models in the unit may not move more than 6" for that movement", for a unit that counts as being in difficult terrain.
            var strider = rules.IgnoresDifficult;
            if (!strider && CountsAsIn(live, TargetTerrain.Difficult)) allowance = Math.Min(allowance, GfNames.DifficultCap);
            var used = ctx.Frame.Value("distance_used_tenths") / 10.0;
            // p.11 "for that movement": the cap spans every segment of the move a cast splits. What is left of it is this segment's cap, and once an
            // earlier segment went in or through difficult ground it bounds the whole of what is left.
            double? cap = strider ? null : Math.Max(0, GfNames.DifficultCap - used);
            var remaining = Math.Max(0, allowance - used);
            if (cap is { } left && ctx.Frame.Value(GfNames.EnteredDifficultKey) > 0) remaining = Math.Min(remaining, left);
            var terrain = Battlefield.Terrain(ctx.State);
            if (aircraft && ctx.Frame.Value("before_move") == 0)
            {
                if (Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) is { } before) return before;
                // A cast reopens the window with a fresh history; a pass, or nothing offered, goes on to the move.
                return ctx.History.Children.Count > 0 ? new Complete([], null, Repeat: true) : new Complete([], new Dictionary<string, int> { ["before_move"] = 1 }, Repeat: true);
            }
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                    // p.13: an Aircraft ignores all terrain, and a Flying unit moves through it (TerrainFor); PathBreach still judges where Flying ends.
                    var (impassable, difficult) = RegionVolumes(ctx.State, rules);
                    var containment = Containers(ctx, unit, action, remaining, rules.PassesImpassable ? [] : Declared(terrain, TerrainDeclarations.Impassable));
                    var prompt = containment?.ExitingId is not null ? $"Disembark: place every model fully within {GfNames.ExitWithin:0.#}\" of the transport"
                        : ignoreSeparation ? "Charge: move into contact" : aircraft ? AircraftPrompt(ctx, unit, remaining) : $"Move up to {remaining:0.#}\"";
                    // p.13: an Aircraft's move is mandatory, so it may not pass and must go at least 30" (MinimumBreach judges the answer).
                    // A stop is offered when the checkpoint after it has something for the mover now; an Aircraft's one segment never stops.
                    var contact = ContactWith(ctx, target);
                    var stoppable = !aircraft &&ctx.Rules.ReactionsAt(ctx.State, ctx.Properties, GfNames.BeforeAttacking, ctx.Frame).Any(r => r.TeamId == ctx.ActorTeam);
                    return new OpenAsks([ctx.Ask("move", prompt, ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), remaining, impassable, difficult, cap, containment, aircraft ? GfNames.AircraftMove : null, stoppable,
                            ignoreSeparation || aircraft ? null : GfNames.Separation, rules.PassesImpassable, rules.IgnoresAllTerrain,
                            contact, contact is null ? null : GfNames.ChargeContactGrace), allowPass: !aircraft)]);
                case 0:
                {
                    var answered = ctx.History.LastClosed("move")!;
                    var continues = !aircraft && answered.Single is Destinations { Continues: true };
                    var proposed = GfAnswers.Moves(ctx.State, answered);
                    if (continues) proposed = proposed.Where(m => !Unmoved(ctx, m)).ToList();
                    var (changes, segment, dangerousDice, entered, tested) = ApplyMove(ctx, unit, live, allowance, proposed, action, remaining);
                    var values = new Dictionary<string, int>
                    {
                        ["distance_used_tenths"] = ctx.Frame.Value("distance_used_tenths") + segment, ["dangerous_dice"] = dangerousDice, ["phase"] = dangerousDice > 0 ? 1 : 2,
                        [GfNames.EnteredDifficultKey] = entered || ctx.Frame.Value(GfNames.EnteredDifficultKey) > 0 ? 1 : 0, ["continues"] = continues ? 1 : 0,
                    };
                    MarkTested(ctx.State, unit, values, tested);
                    if (segment > 0)
                    {
                        var (spent, held) = ConsumeKeepingFlying(live);
                        changes.AddRange(spent);
                        foreach (var name in granted) ctx.Applied.Add(name);
                        foreach (var (key, value) in held) values[key] = value;
                    }
                    return new Complete(changes, values, Repeat: true);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(DangerousSeq(ctx.State), new SequenceContext(unit, [], [], action));
                case 1: return new Complete([], new Dictionary<string, int> { ["phase"] = 2 }, Repeat: true);
                default:
                    if (Interpreter.Windows.Run(ctx, GfNames.BeforeAttacking) is { } window) return window;
                    // p.13: a caster may try more than one spell in an activation, so a finished cast reopens the window — back to the
                    // move when budget remains, otherwise round again on the checkpoint. After a stop the move goes on whether the window
                    // was answered or offered nothing. A pass after a whole move leaves no casting child and the step ends.
                    var reacted = ctx.History.Children.Count > 0;
                    var resumes = remaining > 0 && !aircraft;
                    var stopped = resumes && ctx.Frame.Value("continues") > 0;
                    return new Complete([], new Dictionary<string, int> { ["phase"] = resumes ? 0 : 2, ["continues"] = 0 }, Repeat: reacted || stopped);
            }
        }

        /// <summary>p.9 Charge moves: the charge target's models on the table, which the charge reaches (GfMoves.ChargeLanding, ChargeContactBreach); null for every other move.</summary>
        static IReadOnlyList<Guid>? ContactWith(StepContext ctx, Guid target) =>
            target != Guid.Empty && ctx.Projection.Units.ContainsKey(target)
                ? Lookup.LivingModels(ctx.State, target).Where(m => ctx.Projection.Models[m].Location is OnTable).ToList()
                : null;

        /// <summary>A stop's relocation that leaves a model on the table where it stands, facing as it faces: no part of a move.</summary>
        static bool Unmoved(StepContext ctx, ModelRelocation m) =>
            m.To is OnTable to && ctx.Projection.Models.GetValueOrDefault(m.ModelId)?.Location is OnTable at
            && to.Placement.Position.Horizontal.DistanceTo(at.Placement.Position.Horizontal) < 1e-6 && to.Placement.Yaw == at.Placement.Yaw;

        /// <summary>The prompt states what MinimumBreach will require: 30", or, where the table has no room for 30" for some model, as far as the table allows in the direction taken.</summary>
        static string AircraftPrompt(StepContext ctx, Guid unit, double remaining) =>
            Lookup.AliveModels(ctx.State, unit).Select(m => Battlefield.Placed(ctx.State, m)).OfType<PlacedModel>().All(p => HasRoomFor(ctx.State, p, GfNames.AircraftMove))
                ? $"Move {GfNames.AircraftMove:0.#}\" to {remaining:0.#}\" in a straight line" : $"Move as far as the table allows, up to {remaining:0.#}\", in a straight line";

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
}
