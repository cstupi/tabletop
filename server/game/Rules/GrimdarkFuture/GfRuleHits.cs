using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book rules that deal hits or wounds outside an attack (army-book rules spec, batch 5): Deathstrike and Self-Destruct (*"the attacking unit
/// takes X hits"*), Retaliate (*"the attacker takes X hits per wound taken"*), Ravage (*"roll X dice. For each 6+ the target takes one wound"*),
/// Crossing Attack and Strafing (*"when this model moves through enemy units, pick one of them"*). The rule and its number ride the frame's
/// Action as "&lt;rule&gt;|&lt;n&gt;" (Key), since SequenceContext holds no number.
/// </summary>
public static class GfRuleHits
{
    /// <summary>`rule-hits`: n hits that land with no roll to hit (a hit the holder's unit deals), then blocks and wounds.</summary>
    public static Sequence RuleHits() => new(GfNames.RuleHitsSeq, [new HitsStep(), new GfAttack.ScoreHitsStep(finishWithoutHits: true), new GfAttack.PushStep("resolve", GfNames.ResolveHits)]);
    /// <summary>`rule-wounds`: n dice, each 6+ one wound on the target with no block.</summary>
    public static Sequence RuleWounds() => new(GfNames.RuleWoundsSeq, [new WoundDiceStep(), new GfAttack.PushStep("deal", GfNames.DealWounds, ctx => new SequenceContext(null, [ctx.Frame.Context.TargetUnitIds[0]], [], null))]);

    /// <summary>The most hits or dice one use deals or rolls. X and its multiples come from imported army data; the books' largest is a Retaliate or Deathstrike of a few
    /// per wound, so 60 bounds every play while no imported X makes the engine roll or deal without end.</summary>
    public const int MaxDealt = 60;
    public static string Key(string rule, int n) => $"{rule}|{Math.Clamp(n, 0, MaxDealt)}";
    public static (string Rule, int N)? Parse(string? key) =>
        key is not null && key.LastIndexOf('|') is var cut and > 0 && int.TryParse(key.AsSpan(cut + 1), out var n) ? (key[..cut], Math.Clamp(n, 0, MaxDealt)) : null;

    sealed class HitsStep : IStep
    {
        public string Name => "hit";
        public StepOutcome Run(StepContext ctx)
        {
            if (Parse(ctx.Frame.Context.Action) is not var (rule, n)) return new Finish([]);
            ctx.Applied.Add(rule);
            return new Complete([], new Dictionary<string, int> { ["hits"] = n, ["hit_multiplier"] = 1 });
        }
    }

    sealed class WoundDiceStep : IStep
    {
        public string Name => "roll";
        public StepOutcome Run(StepContext ctx)
        {
            if (Parse(ctx.Frame.Context.Action) is not var (rule, n) || n <= 0) return new Finish([]);
            var record = ctx.Roll(new Dice.RollPlan(rule, 6, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
            ctx.Applied.Add(rule);
            var wounds = Dice.RollOutcome.Successes(record);
            var values = new Dictionary<string, int> { ["wounds"] = wounds };
            return wounds == 0 ? new Finish([], values) : new Complete([], values);
        }
    }

    /// <summary>The sum of the rule's X over the unit's living models that carry it (a unit property counts for every model).</summary>
    static int Sum(GameState state, Guid unit, string rule, IEnumerable<Guid>? models = null) =>
        (models ?? Lookup.LivingModels(state, unit)).Sum(m => Lookup.ModelParameter(state, m, rule) ?? (state.Projection.Units[unit].Definition.Properties.FirstOrDefault(p => p.Name == rule)?.Parameter ?? 0));

    /// <summary>
    /// deal-wounds' last step: wounds an attack in melee dealt set off the wounded models' rules on the attacking unit. Deathstrike and Self-Destruct:
    /// "If this model is killed in melee, the attacking unit takes X hits"; Retaliate: "When this model takes wounds in melee, the attacker takes X hits
    /// per wound taken". Only a weapon or Impact attack sets them off: the hits they deal set off nothing in turn.
    /// </summary>
    public sealed class RetaliationStep : IStep
    {
        public string Name => "retaliation";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Context.ActorUnitId is not { } attacker || ctx.Frame.Context.TargetUnitIds is not [var target, ..]) return new Complete([]);
            if (!InMeleeAttack(ctx.Frame)) return new Complete([]);
            var due = new List<string>();
            foreach (var (key, n) in ctx.Frame.Values)
            {
                if (key.StartsWith("took_", StringComparison.Ordinal) && Guid.TryParseExact(key["took_".Length..], "N", out var m) && n > 0 && Lookup.ModelParameter(ctx.State, m, GfNames.Retaliate) is { } x)
                    due.Add(Key(GfNames.Retaliate, x * n));
                if (key.StartsWith("killed_", StringComparison.Ordinal) && Guid.TryParseExact(key["killed_".Length..], "N", out var k))
                    foreach (var rule in new[] { GfNames.Deathstrike, GfNames.SelfDestruct })
                        if (Lookup.ModelParameter(ctx.State, k, rule) is { } y) due.Add(Key(rule, y));
            }
            // One push per run; the count rides a repeat, so each push runs in its own history.
            var pushed = ctx.Frame.Value("retaliations");
            if (ctx.History.Children.Count > 0) return new Complete([], new Dictionary<string, int> { ["retaliations"] = pushed + 1 }, Repeat: pushed + 1 < due.Count);
            if (pushed >= due.Count || Lookup.LivingModels(ctx.State, attacker).Count == 0) return new Complete([]);
            return new Push(GfNames.RuleHitsSeq, new SequenceContext(target, [attacker], [], due[pushed]));
        }

        /// <summary>Under a melee frame, and under an attack-sequence or Impact's resolve-hits rather than under hits a rule dealt.</summary>
        static bool InMeleeAttack(FrameView frame)
        {
            var melee = false; var attack = false;
            for (var f = frame.Parent; f is not null; f = f.Parent)
            {
                if (f.Frame.Sequence is GfNames.RuleHitsSeq or GfNames.RuleWoundsSeq or GfNames.SpellHitsSeq) return false;
                if (f.Frame.Sequence == GfNames.AttackSequence || (f.Frame.Sequence == GfNames.ResolveHits && f.Parent?.Frame.Sequence == GfNames.Melee)) attack = true;
                if (f.Frame.Sequence == GfNames.Melee) melee = true;
            }
            return melee && attack;
        }
    }

    /// <summary>Ravage: "When it's this model's turn to attack in melee, roll X dice. For each 6+ the target takes one wound." The striking unit's turn: the
    /// charger's before its strikes, the defender's when it strikes back (or struck first with Counter).</summary>
    public sealed class RavageStep(string name, bool charger) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx)
        {
            var (attacker, target) = charger ? (ctx.Frame.Context.ActorUnitId!.Value, ctx.Frame.Context.TargetUnitIds[0]) : (ctx.Frame.Context.TargetUnitIds[0], ctx.Frame.Context.ActorUnitId!.Value);
            if (ctx.History.Children.Count > 0) return new Complete([]);
            if (!charger && !GfMelee.DefenderStruck(ctx)) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, attacker).Count == 0 || Lookup.LivingModels(ctx.State, target).Count == 0) return new Complete([]);
            var dice = Sum(ctx.State, attacker, GfNames.Ravage, Lookup.LivingModels(ctx.State, attacker).Where(m => Lookup.ModelParameter(ctx.State, m, GfNames.Ravage) is not null));
            return dice > 0 ? new Push(GfNames.RuleWoundsSeq, new SequenceContext(attacker, [target], [], Key(GfNames.Ravage, dice))) : new Complete([]);
        }
    }

    /// <summary>Self-Destruct: "If this model survives melee, after both sides have finished attacking, it is immediately killed, and the enemy unit takes X
    /// hits." Each side's surviving carriers are removed, then their hits are dealt, one push per side.</summary>
    public sealed class SelfDestructStep : IStep
    {
        public string Name => "self-destruct";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (ctx.Frame.Context.ActorUnitId!.Value, ctx.Frame.Context.TargetUnitIds[0]);
            var phase = ctx.Frame.Value("self_destruct_phase");
            if (phase == 0)
            {
                var killed = new[] { charger, defender }.SelectMany(u => Lookup.LivingModels(ctx.State, u).Where(m => Lookup.ModelParameter(ctx.State, m, GfNames.SelfDestruct) is not null)).ToList();
                if (killed.Count == 0) return new Complete([]);
                ctx.Applied.Add(GfNames.SelfDestruct);
                var values = new Dictionary<string, int> { ["self_destruct_phase"] = 1, ["self_destruct_charger"] = Sum(ctx.State, charger, GfNames.SelfDestruct, killed.Where(m => ctx.Projection.Models[m].UnitId == charger)),
                    ["self_destruct_defender"] = Sum(ctx.State, defender, GfNames.SelfDestruct, killed.Where(m => ctx.Projection.Models[m].UnitId == defender)) };
                return new Complete([new ModelsMoved(killed.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())], values, Repeat: true);
            }
            // phase 1: the charger's carriers' hits on the defender; phase 2: the defender's on the charger.
            if (ctx.History.Children.Count > 0) return new Complete([], new Dictionary<string, int> { ["self_destruct_phase"] = phase + 1 }, Repeat: phase < 2);
            var (by, on, hits) = phase == 1 ? (charger, defender, ctx.Frame.Value("self_destruct_charger")) : (defender, charger, ctx.Frame.Value("self_destruct_defender"));
            if (hits <= 0 || Lookup.LivingModels(ctx.State, on).Count == 0) return new Complete([], new Dictionary<string, int> { ["self_destruct_phase"] = phase + 1 }, Repeat: phase < 2);
            return new Push(GfNames.RuleHitsSeq, new SequenceContext(by, [on], [], Key(GfNames.SelfDestruct, hits)));
        }
    }

    /// <summary>
    /// Crossing Attack and Strafing, after an action's move: "Once per activation, when this model moves through enemy units, pick one of them and roll X
    /// dice. For each 6+ the target takes one wound" / "… and attack it with this weapon as if it was shooting". The enemy units a carrier's move segments
    /// crossed (Collision.Crosses over their models where they stand) are offered; the pick rolls the dice or makes the attack.
    /// </summary>
    public sealed class CrossingStep : IStep
    {
        public string Name => "crossing";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var (rule, weapon, x) = Carried(ctx, unit);
            if (ctx.History.Children.Count > 0)
            {
                if (rule == GfNames.Strafing) ctx.Applied.Add(GfNames.Strafing);   // the weapon's attack is the rule acting; Crossing Attack's dice name it themselves
                return new Complete([]);
            }
            if (rule is null) return new Complete([]);
            var crossed = Crossed(ctx, unit);
            if (crossed.Count == 0) return new Complete([]);
            if (ctx.History.LastClosed("crossing_target") is { } closed)
            {
                if (GfAnswers.Pieces(closed.Single, closed.Ask) is not [var target, ..]) return new Complete([]);
                return rule == GfNames.Strafing
                    ? new Push(GfNames.AttackSequence, new SequenceContext(unit, [target], [weapon!.Value], ctx.Frame.Context.Action))
                    : new Push(GfNames.RuleWoundsSeq, new SequenceContext(unit, [target], [], Key(rule, x)));
            }
            return new OpenAsks([ctx.Ask("crossing_target", $"{rule}: pick an enemy unit it moved through", ctx.ActorTeam!.Value, new PieceSelection(crossed, 1, 1), allowPass: true)]);
        }

        /// <summary>The unit's crossing rule: a living model's Crossing Attack (with its X), else a Strafing weapon of a living model.</summary>
        static (string? Rule, Guid? Weapon, int X) Carried(StepContext ctx, Guid unit)
        {
            foreach (var m in Lookup.LivingModels(ctx.State, unit))
                if (Lookup.ModelParameter(ctx.State, m, GfNames.CrossingAttack) is { } x) return (GfNames.CrossingAttack, null, x);
            foreach (var m in Lookup.LivingModels(ctx.State, unit))
                if (ctx.Projection.Models[m].Slot.Weapons.FirstOrDefault(w => w.Properties.Any(p => p.Name == GfNames.Strafing)) is { } w) return (GfNames.Strafing, w.Id, 0);
            return (null, null, 0);
        }

        /// <summary>Enemy units one of this frame's move segments carried a model across: the segment from where the model stood to where it landed.</summary>
        static IReadOnlyList<Guid> Crossed(StepContext ctx, Guid unit)
        {
            var team = ctx.ActorTeam;
            var enemies = ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team).Select(u => u.Id).ToList();
            var bodies = enemies.ToDictionary(e => e, e => Lookup.LivingModels(ctx.State, e).Select(m => Battlefield.Placed(ctx.State, m)).OfType<PlacedModel>().ToList());
            var crossed = new HashSet<Guid>();
            foreach (var step in ctx.Frame.Frame.Steps.Where(s => s.Step == "move"))
                foreach (var moved in step.Changes.OfType<ModelsMoved>())
                    foreach (var mv in moved.Moves.Where(mv => mv.From is OnTable && mv.To is OnTable && ctx.Projection.Models.TryGetValue(mv.ModelId, out var mm) && mm.UnitId == unit))
                    {
                        var from = ((OnTable)mv.From).Placement;
                        var body = new PlacedModel(new ModelBody(ctx.Projection.Models[mv.ModelId].Slot.Base, ctx.Projection.Models[mv.ModelId].Slot.Height), from);
                        foreach (var (enemy, models) in bodies)
                            if (models.Count > 0 && Collision.Crosses(body, ((OnTable)mv.To).Placement.Position.Horizontal, models)) crossed.Add(enemy);
                    }
            return [.. enemies.Where(crossed.Contains)];
        }
    }
}
