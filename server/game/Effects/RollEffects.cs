using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

/// <summary>What one die does beyond its success, for the side that rolled it: extra hits it scores, extra wounds that side's unit takes, per-hit grants.</summary>
public sealed record DieEffects(int ExtraHits, int ExtraWounds, IReadOnlyList<(string Entry, int? Parameter)> Grants, int ExtraAttacks = 0);

public static class RollEffects
{
    /// <summary>The roll as the live set leaves it. Summing is generic; the convention clamps. Suppressed modifier classes are removed before summing.</summary>
    public static RollPlan Plan(LiveSet live, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention convention, int? requiredRaw, string hitRollName) =>
        Attributed(live, roller, roll, threshold, baseModifiers, convention, requiredRaw, hitRollName).Plan;

    /// <summary>
    /// Plan, with the sources behind its forced rerolls by the face each rerolls. A modifier that survives the suppressions is recorded as applied; one a
    /// suppression removed is only spent, and the suppressor is what is recorded. A forced reroll is only spent here: whether it acts depends on the dice,
    /// so the caller records its source when the roller rerolled that face (StepContext.Roll).
    /// </summary>
    public static (RollPlan Plan, IReadOnlyList<(int Face, bool SuccessOnly, LiveProperty Source)> Rerolls) Attributed(LiveSet live, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers,
        RollConvention convention, int? requiredRaw, string hitRollName)
    {
        var modifiers = baseModifiers.ToList();
        var added = new List<(RollModifier Modifier, LiveProperty Source)>();
        foreach (var (src, e) in live.Effects(roller, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll))
        {
            var modifier = new RollModifier(src.Entry.Name, e.EffectiveAmount(src.Parameter), live.GrantorOf(src));
            modifiers.Add(modifier);
            added.Add((modifier, src));
        }
        // What a suppression took away, for the log (detailed-log spec): the modify_roll rows of removed properties that would have applied, and the
        // negative hit modifiers a to_hit_penalties or foreign_to_hit_penalties suppression drops. Cover's is the CoverVerdict's to say.
        var ignored = live.RemovedRows(roller, EffectKind.ModifyRoll, e => e.TargetRoll == roll)
            .Select(x => new IgnoredEffect(x.Removed.Entry.Name, x.By.Entry.Name, x.Effect.EffectiveAmount(x.Removed.Parameter))).ToList();
        live.ReadRemoved(roller, EffectKind.ModifyRoll, e => e.TargetRoll == roll);
        if (live.SuppressedModifiers.Contains(TargetModifier.Cover) && modifiers.RemoveAll(m => m.Source == "cover") > 0) live.ReadSuppressedModifier(TargetModifier.Cover);
        if (roll == hitRollName)
        {
            // ToHitPenalties drops every negative modifier; ForeignToHitPenalties keeps those from the roller's own rules (LiveSet.OwnedBy). A base modifier has no
            // rule behind it, so it is foreign.
            var sources = new Dictionary<RollModifier, LiveProperty>(ReferenceEqualityComparer.Instance);
            foreach (var (modifier, src) in added) sources.Add(modifier, src);
            bool Own(RollModifier m) => sources.TryGetValue(m, out var src) && live.OwnedBy(src, roller);
            foreach (var kind in new[] { TargetModifier.ToHitPenalties, TargetModifier.ForeignToHitPenalties })
            {
                if (!live.SuppressedModifiers.Contains(kind)) continue;
                bool Drops(RollModifier m) => m.Amount < 0 && (kind == TargetModifier.ToHitPenalties || !Own(m));
                var by = string.Join(", ", live.Suppressors(kind));
                ignored.AddRange(modifiers.Where(Drops).Select(m => new IgnoredEffect(m.Via is { } via && m.Source == "AP" ? $"{via} {m.Source}" : m.Source, by, m.Amount)));
                if (modifiers.RemoveAll(Drops) > 0) live.ReadSuppressedModifier(kind);
            }
        }
        foreach (var (modifier, src) in added)
            if (modifiers.Any(m => ReferenceEquals(m, modifier))) live.Read(src); else live.Spend(src);
        // ForceReroll: its roll_result condition names the face, and the die's success when it has a third value; every other condition must hold now.
        var reroll = new HashSet<int>(); var successful = new HashSet<int>();
        var rerolls = new List<(int Face, bool SuccessOnly, LiveProperty Source)>();
        foreach (var p in live.Live)
            foreach (var e in p.Effects.Where(e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == roll && p.ActedOn(e) == roller))
            {
                var face = e.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.RollResult && c.Values.Count is 2 or 3 && c.Values[0] == roll && c.Values[1] == "raw");
                if (face?.Number is not { } n || !e.Conditions.Where(c => c != face).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null, live))) continue;
                var successOnly = face.Values.Count == 3;
                (successOnly ? successful : reroll).Add(n); rerolls.Add((n, successOnly, p)); live.Spend(p);
            }
        return (new RollPlan(roll, threshold, modifiers, convention, requiredRaw, reroll, successful, Ignored: ignored.Count == 0 ? null : ignored.Distinct().ToList()), rerolls);
    }

    /// <summary>What one die does beyond its success: extra hits and per-hit grants of a rule. A row that adds wounds acts on the die whatever it scored and is
    /// read; one that adds hits, attacks or a grant acts only on a success, so its source is recorded only then (ReadPerDie spends a granted one).</summary>
    public static DieEffects PerDie(LiveSet live, Side roller, Die die, string roll)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified, die.Success);
        var adds = live.Effects(roller, EffectKind.AddHit, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).ToList();
        var extra = adds.Where(x => x.Effect.TargetQuantity is null or TargetQuantity.Hits).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        var wounds = adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Wounds).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        foreach (var x in adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Wounds)) live.Read(x.Source);
        var grants = live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .ToList();
        if (die.Success)
            foreach (var x in adds.Where(x => x.Effect.TargetQuantity != TargetQuantity.Wounds).Concat(grants)) live.Record(x.Source);
        var attacks = adds.Where(x => x.Effect.TargetQuantity == TargetQuantity.Attacks).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        return new DieEffects(extra, wounds, grants.Select(x => (x.Effect.TargetEntry!, x.Effect.Amount)).ToList(), attacks);
    }

    /// <summary>The wounds a die showing this face adds (a roll-result add_hit row on wounds, which acts whatever the die scored). Pure: PerDie reads
    /// the sources of the dice rolled, and this maps every face, so a die an edit changes later is read on the face it then shows.</summary>
    public static int ExtraWounds(LiveSet live, Side roller, Die die, string roll) =>
        live.Effects(roller, EffectKind.AddHit, new DieContext(roll, die.Raw, die.Modified, die.Success))
            .Where(x => x.Effect.TargetQuantity == TargetQuantity.Wounds && x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    /// <summary>
    /// What one die set off, for the log (combat-log spec → Die.Triggered): each roll-result add_hit row as `Rule +N hit(s)` (or attack(s),
    /// or wound(s), which act on any die), and each roll-result grant_property row as the granting rule's name. Pure: it records and spends
    /// nothing, since score-hits is where the face's decision is read. Null when the die set off nothing. `attacks` false leaves out the
    /// extra-attacks rows, for dice those rows added (they apply to no newly generated attack).
    /// </summary>
    public static IReadOnlyList<string>? Triggers(LiveSet live, Side roller, Die die, string roll, bool attacks = true)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified, die.Success);
        static bool PerDie(Effect e) => e.Conditions.Any(c => c.Kind == ConditionKind.RollResult);
        var named = new List<string>();
        foreach (var (src, e) in live.Effects(roller, EffectKind.AddHit, ctx).Where(x => PerDie(x.Effect)))
        {
            if (e.TargetQuantity != TargetQuantity.Wounds && !die.Success) continue;
            if (!attacks && e.TargetQuantity == TargetQuantity.Attacks) continue;
            var n = e.EffectiveAmount(src.Parameter);
            var what = (e.TargetQuantity ?? TargetQuantity.Hits).ToString().ToLowerInvariant();   // the quantity's own name: hits, attacks, wounds
            named.Add($"{src.Entry.Name} +{n} {(n == 1 ? what.TrimEnd('s') : what)}");
        }
        if (die.Success) named.AddRange(live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => PerDie(x.Effect)).Select(x => x.Source.Entry.Name));
        return named.Count == 0 ? null : named.Distinct().ToList();
    }

    public static int HitMultiplier(LiveSet live, Side attacker, int defenderModels) => Multiplier(live, attacker, TargetQuantity.Hits, defenderModels);
    public static int WoundMultiplier(LiveSet live, Side attacker) => Multiplier(live, attacker, TargetQuantity.Wounds, int.MaxValue);

    static int Multiplier(LiveSet live, Side side, TargetQuantity quantity, int defenderModels)
    {
        var product = 1;
        live.ReadRemoved(side, EffectKind.Multiply, e => e.TargetQuantity == quantity);
        foreach (var (src, e) in live.Effects(side, EffectKind.Multiply).Where(x => x.Effect.TargetQuantity == quantity))
        {
            var x = Math.Max(1, e.EffectiveAmount(src.Parameter));
            if (e.CapKind == CapKind.ModelsInTargetUnit) x = Math.Min(x, Math.Max(1, defenderModels));
            product *= x;
            if (x > 1) live.Read(src); else live.Spend(src);   // capped to one, it changed nothing
        }
        return product;
    }

    /// <summary>Kind-5 rolls a property owns: the property supplies its own dice and threshold, and its child effect is applied once per success.</summary>
    public static IReadOnlyList<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet live, Side side, Func<Effect, bool> childFilter, Func<LiveProperty, Effect, bool>? keep = null)
    {
        live.ReadRemoved(side, EffectKind.Roll, e => e.Children.Any(childFilter));
        var result = live.Effects(side, EffectKind.Roll).Where(x => x.Effect.Children.Any(childFilter) && (keep?.Invoke(x.Source, x.Effect) ?? true)).ToList();
        foreach (var x in result) live.Read(x.Source);
        return result;
    }

    /// <summary>"Next time the effect would apply" for a per-die rule: the attack whose roll it reads, whatever the dice show. Spends every granted
    /// source acting on the side with a roll_result condition on this roll whose other conditions hold; PerDie records the ones a die triggered.</summary>
    public static void ReadPerDie(LiveSet live, Side side, string roll)
    {
        foreach (var p in live.Live.Where(p => p.GrantId is not null))
            if (p.Effects.Any(e => p.ActedOn(e) == side && e.Conditions.Any(c => c.Kind == ConditionKind.RollResult && c.Values.Count >= 2 && c.Values[0] == roll)
                    && e.Conditions.Where(c => c.Kind != ConditionKind.RollResult).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null, live))))
                live.Spend(p);
    }

    /// <summary>A GrantRemoved for every grant the live set read, and for every other grant on that unit the same spell left (spell library → the grants
    /// a spell left on a unit are consumed together). A grant already gone, or on a unit no longer in play, removes nothing.</summary>
    public static IReadOnlyList<StateChange> Consume(LiveSet live)
    {
        var units = live.Interaction.State.Projection.Units;
        return live.GrantsRead
            .Where(r => units.ContainsKey(r.UnitId))
            .SelectMany(r => units[r.UnitId].Grants.FirstOrDefault(g => g.Id == r.GrantId) is { } read
                ? units[r.UnitId].Grants.Where(g => g.EntryName == read.EntryName).Select(g => (r.UnitId, g.Id)) : [])
            .Distinct()
            .Select(x => (StateChange)new GrantRemoved(x.UnitId, x.Item2)).ToList();
    }
}
