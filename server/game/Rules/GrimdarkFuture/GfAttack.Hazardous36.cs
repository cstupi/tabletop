using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// 3.6 Hazardous (GfEdition.HazardousOnModel): "Gets AP(4), but on unmodified results of 1 to hit this model takes one wound, or X wounds if this weapon has the
/// Blast(X)/Deadly(X) rule." While Hazardous is in the attack, `roll-to-hit` rolls one pool per eligible model (its own attacks at its own Quality) and names the
/// model behind each record (`hit_model_&lt;i&gt;_&lt;model&gt;`); `score-hits` adds each die's self wounds, times X, to `self_wounds_&lt;model&gt;`; the self-wounds
/// deal-wounds puts them on those models without an allocation. 3.5.1 ("this model's unit takes one wound") keeps one pool per Quality and the unit's allocation.
/// </summary>
public static partial class GfAttack
{
    const string HitModelPrefix = "hit_model_", SelfWoundsPrefix = "self_wounds_";

    /// <summary>The eligible models with their Quality and attacks, in model order, when the edition attributes Hazardous's 1s and the attack holds Hazardous; else null.</summary>
    static IReadOnlyList<(Guid Model, int Quality, int Attacks)>? HazardRollers(StepContext ctx, LiveSet live, Interaction ix, AttackKind kind)
    {
        if (!GfEdition.Of(ctx.State).HazardousOnModel || !live.Has(Side.Attacker, GfNames.Hazardous)) return null;
        var weapons = ctx.Frame.Context.WeaponIds;
        return PerModelAttacks(ctx, live, EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, weapons, kind), weapons, kind);
    }

    /// <summary>A Quality pool's dice split among its models: each its own attacks in order; dice the settled count holds beyond them (a model fell after the
    /// count was settled) are one model-less share, whose 1s wound nobody. One model-less share without rollers.</summary>
    public static IReadOnlyList<(Guid? Model, int Dice)> Shares(IReadOnlyList<(Guid Model, int Quality, int Attacks)>? rollers, int quality, int attacks)
    {
        if (rollers is null) return [(null, attacks)];
        var mine = rollers.Where(r => r.Quality == quality).ToList();
        var shares = new List<(Guid?, int)>();
        var left = attacks;
        for (var i = 0; i < mine.Count && left > 0; i++)
        {
            var n = Math.Min(mine[i].Attacks, left);
            shares.Add((mine[i].Model, n));
            left -= n;
        }
        if (left > 0) shares.Add((null, left));
        return shares;
    }

    static void MarkRoller(StepContext ctx, Dictionary<string, int> values, Guid? model)
    {
        if (model is { } m) values[$"{HitModelPrefix}{ctx.Rolls.Count - 1}_{m:N}"] = 1;
    }

    /// <summary>The model behind each hit record of the frame, in the order the records stand; null for a pooled record.</summary>
    static IEnumerable<Guid?> HitRollers(FrameView frame) =>
        frame.Frame.Steps.SelectMany(s => s.Rolls.Select((r, i) => (Roll: r, Model: Roller(s.Values, i)))).Where(x => x.Roll.Roll == GfNames.Hit).Select(x => x.Model);

    static Guid? Roller(IReadOnlyDictionary<string, int>? values, int index)
    {
        var prefix = $"{HitModelPrefix}{index}_";
        return values?.Keys.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.Ordinal)) is { } key && Guid.TryParseExact(key[prefix.Length..], "N", out var m) ? m : null;
    }

    /// <summary>Whether this attack-sequence names the model behind each hit die (3.6 with Hazardous in the attack).</summary>
    static bool HazardAttributed(StepContext ctx, LiveSet live) =>
        ctx.Frame.Frame.Sequence == GfNames.AttackSequence && GfEdition.Of(ctx.State).HazardousOnModel && live.Has(Side.Attacker, GfNames.Hazardous);

    /// <summary>"or X wounds if this weapon has the Blast(X)/Deadly(X) rule": the X of the model's weapon in this attack, else 1.</summary>
    static int HazardX(StepContext ctx, Guid model) =>
        ctx.Projection.Models[model].Slot.Weapons.Where(w => ctx.Frame.Context.WeaponIds.Contains(w.Id))
            .SelectMany(w => w.Properties).FirstOrDefault(p => p.Name is "Blast" or "Deadly" && p.Parameter is > 1)?.Parameter ?? 1;

    /// <summary>A die's trigger labels with Hazardous's wound stated at the model's X.</summary>
    static IReadOnlyList<string>? HazardLabels(StepContext ctx, IReadOnlyList<string>? labels, Guid? model)
    {
        if (labels is null || model is not { } m || HazardX(ctx, m) is var x && x <= 1) return labels;
        return labels.Select(l => l == $"{GfNames.Hazardous} +1 wound" ? $"{GfNames.Hazardous} +{x} wounds" : l).ToList();
    }

    /// <summary>A die's self wounds: on its model, times the X of that model's Blast(X)/Deadly(X) weapon in this attack. A model-less die of an attributed
    /// attack (a surplus share) wounds nobody; without attribution the wounds are the unit's (3.5.1) or the caster's (a spell, SelfWoundModels).</summary>
    static void AddSelfWounds(StepContext ctx, Dictionary<string, int> values, Guid? model, int wounds, bool attributed)
    {
        if (wounds <= 0) return;
        if (model is not { } m) { if (!attributed) values["self_wounds"] += wounds; return; }
        var x = HazardX(ctx, m);
        values["self_wounds"] += wounds * x;
        values[$"{SelfWoundsPrefix}{m:N}"] = values.GetValueOrDefault($"{SelfWoundsPrefix}{m:N}") + wounds * x;
    }

    /// <summary>The self-wounds deal-wounds of an attack-sequence that named its models (3.6 Hazardous): each living model and its wounds; of a spell's hits
    /// under 3.6, the caster ("this model"), all of them. Null otherwise.</summary>
    static IReadOnlyList<(Guid Model, int Count)>? SelfWoundModels(StepContext ctx, Guid target)
    {
        if (ctx.Frame.Context.ActorUnitId is not null || ctx.Frame.Parent is not { } parent || parent.Frame.Context.ActorUnitId != target) return null;
        var alive = Lookup.AliveModels(ctx.State, target);
        if (parent.Frame.Sequence == GfNames.SpellHitsSeq)
            return GfEdition.Of(ctx.State).HazardousOnModel && GfCasting.CasterModel(parent) is { } caster && alive.Contains(caster) && parent.Value("self_wounds") is > 0 and var n
                ? [(caster, n)] : null;
        if (parent.Frame.Sequence != GfNames.AttackSequence) return null;
        var own = parent.Values.Where(kv => kv.Key.StartsWith(SelfWoundsPrefix, StringComparison.Ordinal) && kv.Value > 0
                && Guid.TryParseExact(kv.Key[SelfWoundsPrefix.Length..], "N", out var m) && alive.Contains(m))
            .Select(kv => (Guid.ParseExact(kv.Key[SelfWoundsPrefix.Length..], "N"), kv.Value)).ToList();
        return own.Count > 0 ? own : null;
    }
}
