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
    public static Sequence RuleHits() => new(GfNames.RuleHitsSeq, [new HitsStep(), new GfAttack.ScoreHitsStep(finishWithoutHits: true), new GfAttack.PushStep("resolve", GfNames.ResolveHits), new GfAttack.AttackEndStep()]);
    /// <summary>`rule-wounds`: n dice, each 6+ (3.6 Regenerative Strength: 4+) one wound on the target with no block.</summary>
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
            var record = ctx.Roll(new Dice.RollPlan(rule, GfStrikeRolls36.WoundOn(rule), [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
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
    /// per wound taken". A weapon or Impact attack sets them off, and so do the wounds a unit's strike rolls deal at its turn to strike (Ravage, Regenerative
    /// Strength: "the target takes one wound", taken in melee; MeleeStriker). The hits these rules deal set off nothing in turn: a Retaliate wound taken
    /// from another unit's Retaliate hits deals none back.
    /// </summary>
    public sealed class RetaliationStep : IStep
    {
        public string Name => "retaliation";
        public StepOutcome Run(StepContext ctx)
        {
            if (MeleeStriker(ctx.Frame) is not { } attacker || ctx.Frame.Context.TargetUnitIds is not [var target, ..]) return new Complete([]);
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
    }

    /// <summary>The unit whose strike in melee dealt this deal-wounds frame's wounds: the frame's actor under a melee's attack-sequence or Impact's
    /// resolve-hits, or the striking unit of a Ravage or Regenerative Strength `rule-wounds` frame under a melee. Null under hits a rule or a spell dealt,
    /// outside melee, and for any other rule's wounds.</summary>
    public static Guid? MeleeStriker(FrameView frame)
    {
        Guid? striker = frame.Context.ActorUnitId;
        var melee = false; var attack = false;
        for (var f = frame.Parent; f is not null; f = f.Parent)
        {
            if (f.Frame.Sequence is GfNames.RuleHitsSeq or GfNames.SpellHitsSeq) return null;
            if (f.Frame.Sequence == GfNames.RuleWoundsSeq)
            {
                if (Parse(f.Context.Action)?.Rule is not (GfNames.Ravage or GfNames.RegenerativeStrength)) return null;
                striker ??= f.Context.ActorUnitId;
                attack = true;
            }
            if (f.Frame.Sequence == GfNames.AttackSequence || (f.Frame.Sequence == GfNames.ResolveHits && f.Parent?.Frame.Sequence == GfNames.Melee)) attack = true;
            if (f.Frame.Sequence == GfNames.Melee) melee = true;
        }
        return melee && attack ? striker : null;
    }

    /// <summary>Ravage: "When it's this model's turn to attack in melee, roll X dice. For each 6+ the target takes one wound." The striking unit's turn: the
    /// charger's before its strikes, the defender's when it strikes back (or struck first with Counter). A text-only entry (3.6.0's "Once per round ...
    /// unless this unit is Fatigued", which no step plays yet) rolls nothing, as the marker rules' Played gate (GfMarkers).</summary>
    public sealed class RavageStep(string name, bool charger) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Properties.Entry(GfNames.Ravage) is not { Played: true }) return new Complete([]);
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
    /// dice. For each 6+ the target takes one wound" / "… and attack it with this weapon as if it was shooting". Each carrier, in the unit's model order,
    /// is asked in turn about the enemy units its own move segments crossed (Collision.Crosses over their models where they stand), and rolls its own X
    /// or attacks with its own weapon; a model without the rule crossing sets nothing off. The carrier reached rides `crossings` across repeats.
    /// </summary>
    public sealed class CrossingStep : IStep
    {
        public string Name => "crossing";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var carriers = Carriers(ctx, unit);
            var done = ctx.Frame.Value("crossings");
            if (done >= carriers.Count) return new Complete([]);
            var (model, rule, weapon, x) = carriers[done];
            StepOutcome Next() => new Complete([], new Dictionary<string, int> { ["crossings"] = done + 1 }, Repeat: done + 1 < carriers.Count);
            if (ctx.History.Children.Count > 0)
            {
                if (GfNames.IsStrafing(rule)) ctx.Applied.Add(rule);   // the weapon's attack is the rule acting; Crossing Attack's dice name it themselves
                return Next();
            }
            var crossed = ctx.Projection.Models[model].Location is OnTable ? Crossed(ctx, unit, model) : [];
            if (crossed.Count == 0) return Next();
            if (ctx.History.LastClosed("crossing_target") is { } closed)
            {
                if (GfAnswers.Pieces(closed.Single, closed.Ask) is not [var target, ..]) return Next();
                return GfNames.IsStrafing(rule)
                    ? new Push(GfNames.AttackSequence, new SequenceContext(unit, [target], [weapon!.Value], ctx.Frame.Context.Action))
                    : new Push(GfNames.RuleWoundsSeq, new SequenceContext(unit, [target], [], Key(rule, x)));
            }
            return new OpenAsks([ctx.Ask("crossing_target", $"{rule} ({ctx.Projection.Models[model].Slot.Name}): pick an enemy unit it moved through", ctx.ActorTeam!.Value,
                new PieceSelection(crossed, 1, 1), allowPass: true)]);
        }

        /// <summary>The unit's carriers in its definition's model order, a fixed list across the step's repeats: each model's Crossing Attack (with its X),
        /// then each model's Strafing weapons. A unit property counts for every model.</summary>
        static IReadOnlyList<(Guid Model, string Rule, Guid? Weapon, int X)> Carriers(StepContext ctx, Guid unit)
        {
            var definition = ctx.Projection.Units[unit].Definition;
            var own = definition.Properties.FirstOrDefault(p => p.Name == GfNames.CrossingAttack)?.Parameter;
            var list = new List<(Guid, string, Guid?, int)>();
            foreach (var m in definition.Models)
                if ((Lookup.ModelParameter(ctx.State, m.Id, GfNames.CrossingAttack) ?? own) is { } x) list.Add((m.Id, GfNames.CrossingAttack, null, x));
            foreach (var m in definition.Models)
                foreach (var (w, name) in ctx.Projection.Models[m.Id].Slot.Weapons.SelectMany(w => w.Properties.Where(p => GfNames.IsStrafing(p.Name)).Select(p => (w, p.Name))))
                    list.Add((m.Id, name, w.Id, 0));
            return list;
        }

        /// <summary>Enemy units one of this frame's move segments carried the model across: the segment from where it stood to where it landed.</summary>
        static IReadOnlyList<Guid> Crossed(StepContext ctx, Guid unit, Guid model)
        {
            var team = ctx.ActorTeam;
            var enemies = ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team).Select(u => u.Id).ToList();
            var bodies = enemies.ToDictionary(e => e, e => Lookup.LivingModels(ctx.State, e).Select(m => Battlefield.Placed(ctx.State, m)).OfType<PlacedModel>().ToList());
            var crossed = new HashSet<Guid>();
            var terrain = Battlefield.Terrain(ctx.State);
            foreach (var step in ctx.Frame.Frame.Steps.Where(s => s.Step == "move"))
                foreach (var moved in step.Changes.OfType<ModelsMoved>())
                    foreach (var mv in moved.Moves.Where(mv => mv.ModelId == model && mv.From is OnTable && mv.To is OnTable && ctx.Projection.Models.TryGetValue(mv.ModelId, out var mm) && mm.UnitId == unit))
                    {
                        var from = ((OnTable)mv.From).Placement;
                        var body = new PlacedModel(new ModelBody(ctx.Projection.Models[mv.ModelId].Slot.Base, ctx.Projection.Models[mv.ModelId].Slot.Height), from);
                        foreach (var (enemy, models) in bodies)
                            if (models.Count > 0 && Collision.Crosses(body, ((OnTable)mv.To).Placement.Position.Horizontal, models, terrain)) crossed.Add(enemy);
                    }
            return [.. enemies.Where(crossed.Contains)];
        }
    }
}
