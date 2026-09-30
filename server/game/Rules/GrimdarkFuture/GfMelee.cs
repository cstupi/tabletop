using System.Diagnostics;
using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMelee
{
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new ChargeTargetStep(), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true), new DefenderClosesStep(), new FightStep()]);
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep(), new StrikeBackStep(), new CompareStep(), new ConsolidateStep()]);

    public static IReadOnlyList<Guid> MeleeWeapons(GameState state, Guid unit, Func<Weapon, bool>? filter = null) =>
        Lookup.LivingModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => !w.Stats.ContainsKey(GfNames.Range) && (filter?.Invoke(w) ?? true)).Select(w => w.Id).ToList();

    const double Touching = 1e-6;   // "not in base contact": farther apart than float noise
    static bool IsCounter(Weapon w) => w.Properties.Any(p => p.Name == "Counter");
    static bool CounterStruck(StepContext ctx) => ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(StruckKey) == 1);
    static Guid Charger(StepContext ctx) => ctx.Frame.Context.ActorUnitId!.Value;
    static Guid Defender(StepContext ctx) => ctx.Frame.Context.TargetUnitIds[0];
    /// <summary>By step name, not by index: charge's first step is the activation's dangerous terrain test (p.11).</summary>
    static Guid Target(StepContext ctx) => ((SelectedPieces)ctx.Frame.Frame.Steps.Last(s => s.Step == "charge-target").Answer!).PieceIds[0];
    static bool InReach(GameState state, Guid a, Guid b) => Lookup.LivingModels(state, a).Any(m => Lookup.LivingModels(state, b).Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical));
    static StepOutcome AfterChild(StepContext ctx, Func<StepOutcome> push, IReadOnlyList<StateChange>? onDone = null) =>
        ctx.History.Children.Count == 0 ? push() : ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(onDone ?? []) : new OpenAsks([]);
    static IReadOnlyList<StateChange> Fatigue(Guid unit) => [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Fatigued, true)];

    /// <summary>The settled move answer: every model that went on the table lands on the surface. Shared by the defender's close-in and consolidation.</summary>
    static StepOutcome Settled(StepContext ctx, AskRecord closed)
    {
        var moves = (closed.Single as Destinations)?.Moves.Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList() ?? [];
        return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
    }

    /// <summary>Wounds the target took since melee-start, plus Fear(X) over the attacker's models when it attacked (p.13).</summary>
    public static int WoundsCaused(StepContext ctx, Guid by, Guid to, string beforeKey, bool attacked)
    {
        var wounds = ctx.Projection.Units[to].WoundsThisActivation - ctx.Frame.Value(beforeKey);
        var fear = attacked ? Lookup.LivingModels(ctx.State, by).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0) : 0;
        return wounds + fear;
    }

    sealed class ChargeTargetStep : IStep
    {
        public string Name => "charge-target";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var unit = Charger(ctx);
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
            var distance = StatReader.ActionDistance(live, Side.Attacker, GfNames.Charge);
            var terrain = Battlefield.Terrain(ctx.State);
            var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
            var team = ctx.ActorTeam;
            // p.15: "Units may use Charge actions to disembark". An occupant has no position, so its reach is the exit radius from
            // the container it is inside — anything it could be placed fully within 6" of is a unit it could then be in contact with.
            var inside = Lookup.ContainerOf(ctx.State, unit);
            var targets = ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team).Select(u => u.Id).Where(t => inside is { } container
                ? Lookup.LivingModels(ctx.State, t).Any(e => Battlefield.Distance(ctx.State, container, e) <= GfNames.ExitWithin)
                : Lookup.LivingModels(ctx.State, unit).Any(m => Lookup.LivingModels(ctx.State, t).Any(e =>
                {
                    var (from, to) = (Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, e)!.Value);
                    var check = Movement.Check(from, to.Placement.Position.Horizontal, distance, impassable, [], null, ctx.State.Board.Table.Size);
                    return Battlefield.Distance(ctx.State, m, e) <= distance && !check.CrossesImpassable;
                }))).ToList();
            if (targets.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("charge_target", "Charge which unit?", team!.Value, new PieceSelection(targets, 1, 1))]);
        }
    }

    /// <summary>p.9: the target's models not in base contact move up to 3" into contact — their models, their Ask.</summary>
    sealed class DefenderClosesStep : IStep
    {
        public string Name => "defender-closes";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var free = Lookup.LivingModels(ctx.State, target).Where(t => Lookup.LivingModels(ctx.State, charger).All(m => Battlefield.Distance(ctx.State, m, t) > Touching)).ToList();
            if (free.Count == 0 || ctx.TeamOf(target) is not { } team) return new Complete([]);
            return new OpenAsks([ctx.Ask("move", $"Move up to {GfNames.ChargeCloseIn}\" into contact", team, new MoveRegion(free, GfNames.ChargeCloseIn, [], [], null), allowPass: true)]);   // a close-in: no terrain parameters, no cap
        }
    }

    sealed class FightStep : IStep
    {
        public string Name => "fight";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.Children.Count == 0 && !InReach(ctx.State, charger, target)) return new Finish([]);
            return AfterChild(ctx, () => new Push(GfNames.Melee, new SequenceContext(charger, [target], MeleeWeapons(ctx.State, charger), GfNames.Charge)));
        }
    }

    sealed class MeleeStartStep : IStep
    {
        public string Name => "melee-start";
        public StepOutcome Run(StepContext ctx) => new Complete([], new Dictionary<string, int>
            { ["charger_before"] = ctx.Projection.Units[Charger(ctx)].WoundsThisActivation, ["defender_before"] = ctx.Projection.Units[Defender(ctx)].WoundsThisActivation });
    }

    const string StruckKey = "struck";

    sealed class CounterStep : IStep
    {
        public string Name => "counter";
        public StepOutcome Run(StepContext ctx)
        {
            var counters = MeleeWeapons(ctx.State, Defender(ctx), IsCounter);
            if (counters.Count == 0) return new Complete([]);
            // p.13: Counter moves the weapon's strike first; the defender's fatigue is set once, at the end of strike-back.
            if (ctx.History.Children.Count == 0) return new Push(GfNames.AttackSequence, new SequenceContext(Defender(ctx), [Charger(ctx)], counters, null));
            return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete([], new Dictionary<string, int> { [StruckKey] = 1 }) : new OpenAsks([]);
        }
    }

    /// <summary>p.14's `Impact(X)` and p.13's `Counter`: X dice per Impact(X) model, unless fatigued, less one per defending model with Counter; hits at the effect's threshold. Two phases: roll (Repeat), then push resolve-hits.</summary>
    sealed class ImpactStep : IStep
    {
        public string Name => "impact";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("impact_phase") == 1)
                return AfterChild(ctx, () => new Push(GfNames.ResolveHits, new SequenceContext(charger, [defender], [], GfNames.Charge)));
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, charger, defender, [], AttackKind.Melee, GfNames.Charge));
            var impacts = RollEffects.OwnRolls(live, Side.Attacker, c => c.Kind == EffectKind.AddHit);   // Impact's conditions (charging, not fatigued) were judged in pass one's read
            if (impacts.Count == 0) return new Complete([], new Dictionary<string, int> { ["hits"] = 0 });
            var counterModels = Lookup.LivingModels(ctx.State, defender).Count(m => ctx.Projection.Models[m].Slot.Weapons.Any(w => w.Properties.Any(p => p.Name == "Counter")));
            var dice = Math.Max(0, impacts.Sum(x => x.Roll.EffectiveAmount(x.Source.Parameter)) - counterModels);
            var hits = dice == 0 ? 0 : Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(impacts[0].Source.Entry.Name, impacts[0].Roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice));
            return new Complete([], new Dictionary<string, int> { ["hits"] = hits, ["impact_phase"] = 1 }, Repeat: hits > 0);
        }
    }

    sealed class ChargerAttacksStep : IStep
    {
        public string Name => "charger-attacks";
        public StepOutcome Run(StepContext ctx) =>
            AfterChild(ctx, () => new Push(GfNames.AttackSequence, new SequenceContext(Charger(ctx), [Defender(ctx)], ctx.Frame.Context.WeaponIds, GfNames.Charge)), Fatigue(Charger(ctx)));
    }

    /// <summary>
    /// p.9: the defender may strike back (its Ask), and doing so fatigues it; a destroyed defender cannot. A **Shaken** defender may strike
    /// back too, and it is fatigued for doing so: 3.5.1 p.10 reads "must stay idle, but may strike back counting as fatigued"
    /// (GfRolls.Fatigued reads the Shaken condition as fatigue). No Shaken guard belongs here.
    /// </summary>
    sealed class StrikeBackStep : IStep
    {
        public string Name => "strike-back";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            var counterStruck = CounterStruck(ctx);
            if (Lookup.LivingModels(ctx.State, defender).Count == 0) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, charger).Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
            var weapons = MeleeWeapons(ctx.State, defender, w => !IsCounter(w));   // p.13: the Counter weapon's strike already happened in `counter`
            if (ctx.History.Children.Count > 0) return AfterChild(ctx, () => throw new UnreachableException(), Fatigue(defender));
            if (ctx.History.LastClosed() is { } closed)
                return closed.Single is Chosen { OptionId: "strike" } ? new Push(GfNames.AttackSequence, new SequenceContext(defender, [charger], weapons, null)) : new Complete(counterStruck ? Fatigue(defender) : []);
            if (weapons.Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
            return new OpenAsks([ctx.Ask("strike_back", "Strike back?", ctx.TeamOf(defender)!.Value, new Options([new AskOption("strike", "Strike back", [defender]), new AskOption("pass", "Don't", [])]))]);
        }
    }

    /// <summary>p.10: sum wounds caused each way; a destroyed unit always loses; the loser tests morale; a tie tests nobody. Values: charger_wounds, defender_wounds, loser (0 none, 1 charger, 2 defender).</summary>
    sealed class CompareStep : IStep
    {
        public string Name => "compare";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("compared") == 1)   // second run: the loser tests morale on the melee path
            {
                var loserUnit = ctx.Frame.Value("loser") == 1 ? charger : defender;
                return AfterChild(ctx, () => new Push(GfNames.MoraleTest, new SequenceContext(loserUnit, [], [], "melee")));
            }
            var struck = ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && s.Answer is Chosen { OptionId: "strike" }) || CounterStruck(ctx);
            var chargerWounds = WoundsCaused(ctx, charger, defender, "defender_before", attacked: true);
            var defenderWounds = WoundsCaused(ctx, defender, charger, "charger_before", attacked: struck);
            var chargerDead = Lookup.LivingModels(ctx.State, charger).Count == 0; var defenderDead = Lookup.LivingModels(ctx.State, defender).Count == 0;
            var loser = defenderDead ? 2 : chargerDead ? 1 : chargerWounds > defenderWounds ? 2 : defenderWounds > chargerWounds ? 1 : 0;
            var values = new Dictionary<string, int> { ["charger_wounds"] = chargerWounds, ["defender_wounds"] = defenderWounds, ["loser"] = loser, ["compared"] = 1 };
            var testing = loser == 1 ? charger : loser == 2 ? defender : (Guid?)null;
            return new Complete([], values, Repeat: testing is not null && Lookup.LivingModels(ctx.State, testing.Value).Count > 0);
        }
    }

    /// <summary>p.10: a destroyed or routed unit lets the other move 3"; otherwise the charger moves back 1".</summary>
    sealed class ConsolidateStep : IStep
    {
        public string Name => "consolidate";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var (chargerAlive, defenderAlive) = (Lookup.LivingModels(ctx.State, charger), Lookup.LivingModels(ctx.State, defender));
            if (chargerAlive.Count == 0 && defenderAlive.Count == 0) return new Complete([]);
            var (mover, distance) = chargerAlive.Count == 0 ? (defender, GfNames.Consolidate) : defenderAlive.Count == 0 ? (charger, GfNames.Consolidate) : (charger, GfNames.FallBack);
            return new OpenAsks([ctx.Ask("move", $"Move up to {distance}\"", ctx.TeamOf(mover)!.Value, new MoveRegion(Lookup.LivingModels(ctx.State, mover), distance, [], [], null), allowPass: true)]);   // fall back / consolidate: no terrain parameters, no cap
        }
    }
}
