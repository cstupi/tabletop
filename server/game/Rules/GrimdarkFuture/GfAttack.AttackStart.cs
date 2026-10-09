using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>The attack-start interactions of a unit: one per opponent it attacks (so a grant against that opponent is live). An attack with no opponent has no attack-start.</summary>
    static IEnumerable<Interaction> AttackStartInteractions(StepContext ctx, Guid unit, IReadOnlyList<Guid> opponents, AttackKind kind, bool withFaces) =>
        opponents.Select(o => AttackInteraction(ctx, unit, o, [], kind, ctx.Frame.Context.Action) with { Point = GfNames.AttackStart, Faces = withFaces ? FacesFor(ctx.Frame, unit, o) : null });

    /// <summary>Each live roll row of the unit asked at attack-start (its other conditions holding for this attack kind, against each known opponent) rolls
    /// its row's dice once, as a RollRecord named for its entry, and its first die is recorded as face_&lt;unit&gt;_&lt;entry&gt;.</summary>
    public static void RollAttackStart(StepContext ctx, Guid unit, IReadOnlyList<Guid> opponents, AttackKind kind, Dictionary<string, int> values)
    {
        if (Lookup.LivingModels(ctx.State, unit).Count == 0) return;
        var rolled = new HashSet<string>();
        foreach (var ix in AttackStartInteractions(ctx, unit, opponents, kind, withFaces: false))
        {
            var live = LiveProperties.Settle(ix);
            foreach (var (source, row) in live.AtPoint(Side.Attacker, GfNames.AttackStart).Where(x => x.Effect.Kind == EffectKind.Roll))
            {
                if (!rolled.Add(source.Entry.Name)) continue;
                var record = ctx.Roll(new Dice.RollPlan(source.Entry.Name, row.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), Math.Max(1, row.Amount ?? 1));
                values[$"face_{unit:N}_{source.Entry.Name}"] = record.Dice[0].Raw;
            }
        }
    }

    /// <summary>A rule rolled at attack-start is applied by the attack that follows, so a granted instance of it is read and spent where that attack ends.</summary>
    public static IReadOnlyList<StateChange> SpendAttackStart(StepContext ctx, Guid unit, IReadOnlyList<Guid> opponents, AttackKind kind)
    {
        var spent = new List<StateChange>();
        foreach (var ix in AttackStartInteractions(ctx, unit, opponents, kind, withFaces: true))
        {
            if (ix.Faces is null || !ix.Faces.TryGetValue(Side.Attacker, out var faces) || faces.Count == 0) continue;
            var live = LiveProperties.Settle(ix with { Point = null });
            foreach (var p in live.On(Side.Attacker).Where(p => p.GrantId is not null && faces.ContainsKey(p.Entry.Name))) live.Read(p);
            spent.AddRange(RollEffects.Consume(live));
        }
        return spent.Distinct().ToList();
    }

    public sealed class AttackStartStep : IStep
    {
        public string Name => "attack-start";
        public StepOutcome Run(StepContext ctx)
        {
            var values = new Dictionary<string, int>();
            // Nothing fires without a declared target the Ask offered, so nothing is rolled for.
            var targets = GfShooting.Declared(ctx)?.Targets.Values.Distinct().ToList() ?? [];
            if (targets.Count == 0) return new Complete([]);
            RollAttackStart(ctx, ctx.Frame.Context.ActorUnitId!.Value, targets, AttackKind.Shooting, values);
            return new Complete([], values);
        }
    }

    /// <summary>The faces the nearest shooting or melee frame recorded, by side of this interaction.</summary>
    static IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? FacesFor(FrameView? frame, Guid attacker, Guid target)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence is GfNames.Shooting or GfNames.Melee)
            {
                IReadOnlyDictionary<string, int> Of(Guid unit) => f.Values.Where(kv => kv.Key.StartsWith($"face_{unit:N}_")).ToDictionary(kv => kv.Key[$"face_{unit:N}_".Length..], kv => kv.Value);
                return new Dictionary<Side, IReadOnlyDictionary<string, int>> { [Side.Attacker] = Of(attacker), [Side.Defender] = Of(target) };
            }
        return null;
    }

    /// <summary>Wounds the attacking unit dealt itself (Hazardous: "this model's unit takes one wound on unmodified rolls of 1 to hit"), after
    /// its hits resolved: a deal-wounds with no attacker, at its own unit, reading `wounds` off this frame.</summary>
    public sealed class SelfWoundsStep : IStep
    {
        public string Name => "self-wounds";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Value("self_wounds") <= 0 || ctx.Frame.Context.ActorUnitId is not { } self) return new Complete([]);
            if (ctx.History.Children.Any(c => c.Finished || c.Abandoned)) return new Complete([]);
            return new Push(GfNames.DealWounds, new SequenceContext(null, [self], [], null));
        }
    }
}
