using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieEffects(int ExtraHits, IReadOnlyList<(string Entry, int? Parameter)> Grants);

public static class RollEffects
{
    /// <summary>The roll as the live set leaves it. Summing is generic; the convention clamps. Suppressed modifier classes are removed before summing.</summary>
    public static RollPlan Plan(LiveSet live, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention convention, int? requiredRaw, string hitRollName)
    {
        var modifiers = baseModifiers.ToList();
        foreach (var (src, e) in live.Effects(roller, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll))
            modifiers.Add(new RollModifier(src.Entry.Name, e.EffectiveAmount(src.Parameter)));
        if (live.SuppressedModifiers.Contains(TargetModifier.Cover)) modifiers.RemoveAll(m => m.Source == "cover");
        if (roll == hitRollName && live.SuppressedModifiers.Contains(TargetModifier.ToHitPenalties)) modifiers.RemoveAll(m => m.Amount < 0);
        // ForceReroll: its roll_result condition names the face; every other condition must hold now.
        var reroll = new HashSet<int>();
        foreach (var p in live.Live)
            foreach (var e in p.Effects.Where(e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == roll && p.ActedOn(e) == roller))
            {
                var face = e.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == roll && c.Values[1] == "raw");
                if (face?.Number is { } n && e.Conditions.Where(c => c != face).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null))) reroll.Add(n);
            }
        return new RollPlan(roll, threshold, modifiers, convention, requiredRaw, reroll);
    }

    /// <summary>What one die does beyond its success: extra hits and per-hit grants of a rule.</summary>
    public static DieEffects PerDie(LiveSet live, Side roller, Die die, string roll)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified);
        var extra = live.Effects(roller, EffectKind.AddHit, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        var grants = live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .Select(x => (x.Effect.TargetEntry!, x.Effect.Amount)).ToList();
        return new DieEffects(extra, grants);
    }

    public static int HitMultiplier(LiveSet live, Side attacker, int defenderModels) => Multiplier(live, attacker, TargetQuantity.Hits, defenderModels);
    public static int WoundMultiplier(LiveSet live, Side attacker) => Multiplier(live, attacker, TargetQuantity.Wounds, int.MaxValue);

    static int Multiplier(LiveSet live, Side side, TargetQuantity quantity, int defenderModels)
    {
        var product = 1;
        foreach (var (src, e) in live.Effects(side, EffectKind.Multiply).Where(x => x.Effect.TargetQuantity == quantity))
        {
            var x = Math.Max(1, e.EffectiveAmount(src.Parameter));
            if (e.CapKind == CapKind.ModelsInTargetUnit) x = Math.Min(x, Math.Max(1, defenderModels));
            product *= x;
        }
        return product;
    }

    /// <summary>Kind-5 rolls a property owns: the property supplies its own dice and threshold, and its child effect is applied once per success.</summary>
    public static IReadOnlyList<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet live, Side side, Func<Effect, bool> childFilter) =>
        live.Effects(side, EffectKind.Roll).Where(x => x.Effect.Children.Any(childFilter)).ToList();

    /// <summary>A once-grant is consumed the first time its effect applies: every granted instance whose entry contributed a modifier to this plan, with one use left.</summary>
    // A grant is matched to a modifier by entry name: the grant's entry and the modifier's source are the same string.
    public static IReadOnlyList<StateChange> ConsumeGrants(LiveSet live, Side side, RollPlan plan) =>
        live.On(side).Where(p => p.GrantId is not null && plan.Modifiers.Any(m => m.Source == p.Entry.Name))
            .Select(p => (StateChange)new GrantRemoved(live.Interaction.Of(side).UnitId, p.GrantId!.Value)).ToList();
}
