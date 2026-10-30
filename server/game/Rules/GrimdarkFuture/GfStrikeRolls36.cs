using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// 3.6 Ravage ("Once per round, when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued. For each 6+
/// the target takes one wound") and Regenerative Strength ("... roll as many dice as tokens on it before attacking, unless this unit is Fatigued. For each
/// 4+ the target takes one wound"), under GfEdition.StrikeTurnRolls. The `strike-rolls` sequence (actor the striking unit, target the unit it strikes)
/// is pushed at the unit's turn to strike, before its attacks: the charger's by `charger-strike-rolls` before charger-attacks; the defender's by `counter`
/// once it strikes first with Counter, else by `defender-strike-rolls` once `strike-choice` recorded that it strikes back. A Shaken unit striking back
/// counts as Fatigued (3.6.0 p.10). Each rule rolls once per round for the striking unit: its holders' dice in one `rule-wounds` push.
/// </summary>
public static class GfStrikeRolls36
{
    static readonly string[] Rules = [GfNames.Ravage, GfNames.RegenerativeStrength];

    /// <summary>The face a `rule-wounds` die wounds on: Regenerative Strength's 4+, else 6+.</summary>
    public static int WoundOn(string rule) => rule == GfNames.RegenerativeStrength ? 4 : 6;

    public static Sequence StrikeRolls() => new(GfNames.StrikeRollsSeq, [new RollsStep()]);

    /// <summary>The push of the striking unit's rolls, when it is neither Fatigued nor Shaken, both units stand and a rule has dice to roll; else null.</summary>
    public static Push? Due(StepContext ctx, Guid striker, Guid target)
    {
        if (Lookup.LivingModels(ctx.State, striker).Count == 0 || Lookup.LivingModels(ctx.State, target).Count == 0) return null;
        if (ctx.Projection.Units[striker].Conditions is var c && (c.Contains(GfNames.Fatigued) || c.Contains(GfNames.Shaken))) return null;
        return Rules.Any(r => Dice(ctx, striker, r) > 0) ? new Push(GfNames.StrikeRollsSeq, new SequenceContext(striker, [target], [], null)) : null;
    }

    /// <summary>A melee step pushing the strike rolls before a side's strikes: the charger's when it has weapons, the defender's when `strike-choice` recorded
    /// that it strikes back.</summary>
    public sealed class StrikeTurnStep(string name, bool charger) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children is [.., var last]) return last.Finished || last.Abandoned ? new Complete([]) : new OpenAsks([]);
            var (striker, target) = charger ? (ctx.Frame.Context.ActorUnitId!.Value, ctx.Frame.Context.TargetUnitIds[0]) : (ctx.Frame.Context.TargetUnitIds[0], ctx.Frame.Context.ActorUnitId!.Value);
            var strikes = charger ? ctx.Frame.Context.WeaponIds.Count > 0 : ctx.Frame.Value(GfMelee.StrikeBackChosenKey) == 1;
            return strikes && Due(ctx, striker, target) is { } push ? push : new Complete([]);
        }
    }

    /// <summary>`strike-rolls`' one step: each rule in turn, one `rule-wounds` push per run, the next rule's index riding a repeat.</summary>
    sealed class RollsStep : IStep
    {
        const string Next = "next_rule";
        public string Name => "roll";
        public StepOutcome Run(StepContext ctx)
        {
            var (striker, target) = (ctx.Frame.Context.ActorUnitId!.Value, ctx.Frame.Context.TargetUnitIds[0]);
            if (ctx.History.Children is [.., var last])
            {
                if (!(last.Finished || last.Abandoned)) return new OpenAsks([]);
                var done = Array.IndexOf(Rules, GfRuleHits.Parse(last.Context.Action)?.Rule) + 1;
                return new Complete([], new Dictionary<string, int> { [Next] = done }, Repeat: done < Rules.Length);
            }
            if (Lookup.LivingModels(ctx.State, striker).Count == 0 || Lookup.LivingModels(ctx.State, target).Count == 0) return new Complete([]);
            for (var i = ctx.Frame.Value(Next); i < Rules.Length; i++)
                if (Dice(ctx, striker, Rules[i]) is > 0 and var n)
                    return new Push(GfNames.RuleWoundsSeq, new SequenceContext(striker, [target], [], GfRuleHits.Key(Rules[i], n)));
            return new Complete([]);
        }
    }

    /// <summary>The rule's dice for the striking unit: Ravage's X, or Regenerative Strength's tokens, summed over its living holders; none when the entry
    /// is text-only or the unit rolled it already this round.</summary>
    static int Dice(StepContext ctx, Guid striker, string rule)
    {
        if (ctx.Properties.Entry(rule) is not { Played: true } || RolledThisRound(ctx.State, striker, rule)) return 0;
        var holders = Lookup.LivingModels(ctx.State, striker);
        return rule == GfNames.Ravage
            ? holders.Sum(m => Lookup.ModelParameter(ctx.State, m, GfNames.Ravage) ?? 0)
            : holders.Sum(m => GfMarkers.RegenerativeStrength(ctx.State, m));
    }

    /// <summary>"Once per round": a `rule-wounds` frame of the rule by the unit since the round began.</summary>
    static bool RolledThisRound(GameState state, Guid unit, string rule)
    {
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log).Reverse())
        {
            if (e.Payload is RoundStarted) return false;
            if (e.Payload is SequenceStarted { Sequence: GfNames.RuleWoundsSeq } s && s.Context.ActorUnitId == unit && GfRuleHits.Parse(s.Context.Action)?.Rule == rule) return true;
        }
        return false;
    }
}
