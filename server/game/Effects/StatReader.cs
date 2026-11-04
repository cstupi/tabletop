using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

/// <summary>A target number as the live properties leave it, and the printed value and changes it came from.</summary>
public sealed record TargetRead(int Value, ThresholdBasis? Basis);

/// <summary>Stats and distances as the live properties leave them: replace first (ordinal order, last wins), then modify.</summary>
public static class StatReader
{
    /// <summary>
    /// A ruleset may list a target number among a unit's stats yet define the test against the model's own value, so it may
    /// declare the same target number at model level as well, and a slot that carries its own value is a joined model.
    /// Pass the model to read that slot; the unit's text is the fallback for every slot that declares none, so an ordinary
    /// squad reads the same number whichever ref is used. A model-level ref with no model has nothing to resolve against.
    /// </summary>
    public static TargetRead TargetNumber(LiveSet live, Side side, StatRef stat, Guid? modelId = null) => Read(live, side, stat, modelId, account: true);

    /// <summary>The same read with nothing spent or recorded: the basis of a value an earlier step already accounted (a hit pool's Quality).</summary>
    public static TargetRead Peek(LiveSet live, Side side, StatRef stat, Guid? modelId = null) => Read(live, side, stat, modelId, account: false);

    static TargetRead Read(LiveSet live, Side side, StatRef stat, Guid? modelId, bool account)
    {
        var state = live.Interaction.State;
        var unit = state.Projection.Units[live.Interaction.Of(side).UnitId];
        var text = (stat.Level, modelId) switch
        {
            (StatLevel.Unit, _) => unit.Definition.Stats[stat.Name],
            (StatLevel.Model, { } id) => state.Projection.Models[id].Slot.Stats.TryGetValue(stat.Name, out var own) ? own : unit.Definition.Stats[stat.Name],
            _ => throw new NotSupportedException($"a target number is read at unit level, or at model level for a named model; {stat.Level} {stat.Name} has no model to read"),
        };
        var printed = StatValue.Read(StatKind.TargetNumber, text).TargetNumber;
        var value = printed;
        var changes = new List<ThresholdChange>();
        // A target number is one stat with a declaration at each level, and every catalog row names the unit-level ref, so the scan
        // matches on the name across both. That is what puts a rule that sets the number over a joined model's own value.
        bool Acts(Effect e) => e.TargetStat is { Name: var n, Level: StatLevel.Unit or StatLevel.Model } && n == stat.Name;
        foreach (var (src, e) in live.Effects(side, EffectKind.ReplaceStat).Where(x => Acts(x.Effect)))
        {
            // A replacement from the parameter: "Counts as having Defense X+".
            value = e.Replacement is { } literal ? StatValue.Read(StatKind.TargetNumber, literal).TargetNumber : e.EffectiveAmount(src.Parameter);
            changes.Add(new ThresholdChange(src.Entry.Name, value, 0));
            if (account) live.Read(src);
        }
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => Acts(x.Effect)))
        {
            var amount = e.EffectiveAmount(src.Parameter);
            value += amount;
            changes.Add(new ThresholdChange(src.Entry.Name, null, amount));
            if (account) live.Read(src);
        }
        // The stat rows of rules pass one removed that would have acted here, for the log (detailed-log spec): replace first, then modify, as read above.
        var ignored = live.RemovedRows(side, EffectKind.ReplaceStat, Acts).Concat(live.RemovedRows(side, EffectKind.ModifyStat, Acts))
            .Select(x => x.Effect.Kind == EffectKind.ReplaceStat
                ? new IgnoredEffect(x.Removed.Entry.Name, x.By.Entry.Name, 0, x.Effect.Replacement is { } r ? StatValue.Read(StatKind.TargetNumber, r).TargetNumber : x.Effect.EffectiveAmount(x.Removed.Parameter))
                : new IgnoredEffect(x.Removed.Entry.Name, x.By.Entry.Name, x.Effect.EffectiveAmount(x.Removed.Parameter)))
            .Distinct().ToList();
        return new TargetRead(value, new ThresholdBasis(stat.Name, printed, changes, ignored.Count == 0 ? null : ignored));
    }

    public static double? WeaponRange(LiveSet live, Side side, Guid weaponId)
    {
        var weapon = live.Interaction.State.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId);
        if (!weapon.Stats.TryGetValue("Range", out var text)) return null;
        var range = StatValue.Read(StatKind.Distance, text).Inches;
        var rangeRef = new StatRef(StatLevel.Weapon, "Range");
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetStat == rangeRef))
        {
            var amount = e.EffectiveAmount(src.Parameter);
            if (amount < 0 && live.SuppressedModifiers.Contains(TargetModifier.RangePenalties)) { live.ReadSuppressedModifier(TargetModifier.RangePenalties); continue; }
            range = Floored(range, amount, e.Floor);
            live.Read(src);
        }
        return Math.Max(0, range);
    }

    /// <summary>The weapon's range as the live set leaves it, with nothing spent or recorded: what a weapon_range condition compares against.
    /// The range rows' own conditions are judged without the live set, so a range row conditioned on weapon_range reads the printed range.</summary>
    public static double? PeekWeaponRange(LiveSet live, Side side, Guid weaponId)
    {
        var weapon = live.Interaction.State.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == weaponId);
        if (weapon is null || !weapon.Stats.TryGetValue("Range", out var text)) return null;
        var range = StatValue.Read(StatKind.Distance, text).Inches;
        var rangeRef = new StatRef(StatLevel.Weapon, "Range");
        var rows = live.Live.SelectMany(p => p.Effects.Where(e => e.Kind == EffectKind.ModifyStat && e.TargetStat == rangeRef && p.ActedOn(e) == side
            && Conditions.Hold(live.Interaction, p.Holder, e, p, null)).Select(e => (p, e))).OrderBy(x => x.e.Ordinal);
        foreach (var (src, e) in rows)
        {
            var amount = e.EffectiveAmount(src.Parameter);
            if (amount < 0 && live.SuppressedModifiers.Contains(TargetModifier.RangePenalties)) continue;
            range = Floored(range, amount, e.Floor);
        }
        return Math.Max(0, range);
    }

    /// <summary>A change "to a min. of N": it takes the stat no lower than N, or than the stat itself when that is already lower.</summary>
    static double Floored(double value, int amount, int? floor) => floor is { } f ? Math.Max(value + amount, Math.Min(value, f)) : value + amount;

    public static int Attacks(GameState state, Guid weaponId) =>
        StatValue.Read(StatKind.Count, state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId).Stats["Attacks"]).Count;

    public static double ActionDistance(LiveSet live, Side side, string action)
    {
        var distance = live.Interaction.Properties.Action(action).Distance;
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetAction == action)) { distance = Floored(distance, e.EffectiveAmount(src.Parameter), e.Floor); live.Read(src); }
        // A suppressed distance row (Swift's Slow) was read here: the suppressor acted on this distance (LiveSet.ReadRemoved, as RollEffects.Plan reads one).
        live.ReadRemoved(side, EffectKind.ModifyStat, e => e.TargetAction == action);
        return Math.Max(0, distance);
    }
}
