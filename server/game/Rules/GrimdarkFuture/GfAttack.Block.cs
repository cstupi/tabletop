using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>The wounds a die showing `face` adds on the frame's `roll`-th defense roll, mapped when the roll is made and read when the wounds are dealt.</summary>
    static string ExtraWoundsKey(int roll, int face) => $"extra_wounds_{roll}_{face}";

    /// <summary>The wounds a die showing `face` makes the defender count as having taken on the frame's `roll`-th defense roll (count_wounds), mapped
    /// as ExtraWoundsKey is.</summary>
    static string CountedKey(int roll, int face) => $"counted_{roll}_{face}";

    /// <summary>The roll-to-block value holding the counted wounds as rolled; CountedWounds reads the faces as they stand.</summary>
    public const string CountedWoundsKey = "counted_wounds";

    /// <summary>
    /// The wounds a unit counts as having taken (count_wounds) from the resolve-hits frames on it started at or after the effective log's index `from`,
    /// each read off its defense dice as they now stand (a reroll or an edit counts) through its `counted_&lt;roll&gt;_&lt;face&gt;` map; with the rules that
    /// counted them. `shootingOnly` keeps the frames of an attack-sequence no melee pushed.
    /// </summary>
    public static (int Count, IReadOnlyList<string> Rules) CountedWounds(GameState state, PropertySet props, IReadOnlyList<GameEvent> log, int from, Guid unit, bool shootingOnly)
    {
        if (from < 0) return (0, []);
        var open = new List<(Guid Id, string Sequence)>();
        var frames = new List<Guid>();
        for (var i = from; i < log.Count; i++)
            switch (log[i].Payload)
            {
                case SequenceStarted s:
                    if (s.Sequence == GfNames.ResolveHits && s.Context.TargetUnitIds.FirstOrDefault() == unit
                        && (!shootingOnly || (open.Count > 0 && open[^1].Sequence == GfNames.AttackSequence && !StartedInMelee(state, open[^1].Id))))
                        frames.Add(s.FrameId);
                    open.Add((s.FrameId, s.Sequence)); break;
                case SequenceFinished f: open.RemoveAll(x => x.Id == f.FrameId); break;
                case SequenceAbandoned a when open.FindIndex(x => x.Id == a.FrameId) is var k and >= 0: open.RemoveRange(k, open.Count - k); break;
            }
        var count = 0; var rules = new List<string>();
        foreach (var frame in frames)
        {
            var steps = log.Select(e => e.Payload).OfType<StepCompleted>().Where(s => s.FrameId == frame).ToList();
            var values = steps.SelectMany(s => s.Values ?? new Dictionary<string, int>()).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            if (!values.ContainsKey(CountedWoundsKey)) continue;
            var rolls = steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
            var dice = rolls.ToDictionary(r => r.Id, r => r.Dice.ToList());
            foreach (var e in log.Select(e => e.Payload))
            {
                var (owner, rollId, replaced) = e switch
                {
                    DiceRerolled d => (d.FrameId, d.RollId, d.Replaced),
                    DiceEdited d => (d.FrameId, d.RollId, d.Replaced),
                    _ => (Guid.Empty, Guid.Empty, (IReadOnlyList<DieReplacement>?)null),
                };
                if (replaced is null || owner != frame || !dice.TryGetValue(rollId, out var faces)) continue;
                foreach (var r in replaced) faces[r.Index] = r.New;
            }
            var counted = rolls.Select((r, i) => dice[r.Id].Sum(d => values.GetValueOrDefault(CountedKey(i, d.Raw)))).Sum();
            if (counted == 0) continue;
            count += counted;
            rules.AddRange(steps.SelectMany(s => s.Applied ?? []).Where(n => props.Entry(n)?.Effects.Any(x => x.Kind == EffectKind.CountWounds) == true));
        }
        return (count, rules.Distinct().ToList());
    }

    static (Interaction Ix, LiveSet Live, AttackKind Kind) Settle(StepContext ctx)
    {
        var c = ctx.Frame.Context;
        var kind = KindOf(ctx);
        var ix = AttackInteraction(ctx, c.ActorUnitId, c.TargetUnitIds[0], c.WeaponIds, kind, c.Action);
        return (ix, ctx.Settle(ix), kind);
    }

    sealed class RollToBlockStep : IStep
    {
        public string Name => "roll-to-block";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var parent = ctx.Frame.Parent!;
            var hits = HitsFrom(parent);
            if (hits <= 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            // Heavy Impact: "Counts as having Impact(X) with hits that have AP(1)": Impact's hits are the melee's weapon-less block.
            if (parent.Frame.Sequence == GfNames.Melee && ctx.Frame.Context.WeaponIds.Count == 0 && Lookup.UnitHas(ctx.State, ctx.Properties, ix.Attacker.UnitId, GfNames.HeavyImpact))
            {
                live = live.WithGranted(Side.Attacker, "AP", 1);
                ctx.Applied.Add(GfNames.HeavyImpact);
            }
            // Surprise Attack: "For each 2+ it takes one hit with AP(1)".
            if (parent.Frame.Sequence == GfNames.RuleHitsSeq && GfRuleHits.Parse(parent.Frame.Context.Action) is (GfAmbush.SurpriseAttack, _)) live = live.WithGranted(Side.Attacker, "AP", 1);
            // p.11's majority is frozen with the attack counts (p.8: all weapons fire simultaneously), so a group firing after casualties reads the
            // fact determine-attacks carried down. A parent that recorded none is a spell's hits or Impact's, where live is the answer.
            var defender = ix.Defender!.UnitId;
            // The verdict is the unit's, one for every block group (RollRecord.Cover); a frame written before it holds in_cover alone and logs none.
            CoverVerdict? cover = null; var inCover = false;
            // p.14 Takedown: the picked model is "resolved as if it was a unit of [1]", so its own cover is the verdict, read now that it is named.
            var picked = TakedownModel(ctx.Frame) is { } named && Lookup.LivingModels(ctx.State, defender).Contains(named) ? named : (Guid?)null;
            if (kind == AttackKind.Shooting)
            {
                if (picked is { } m) { cover = CoverOf(ctx.State, live, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind), m); inCover = Applies(cover); }
                else if (parent.Values.TryGetValue("in_cover", out var settled)) { inCover = settled == 1; cover = ReadCover(parent.Values); }
                else { cover = CoverOf(ctx.State, live, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind)); inCover = Applies(cover); }
            }
            var multiplier = Math.Max(1, parent.Value("hit_multiplier", 1));
            // p.14's Hero clause needs the defending unit's model count, and determine-attacks carried down the one settled before any
            // group fired. A parent that recorded none is a spell's hits or Impact's, where the live count is the count.
            var defenderModels = parent.Value("defender_models", Lookup.LivingModels(ctx.State, defender).Count);
            var wounds = 0; var extraWounds = 0; var counted = 0; var plain = hits;
            var values = new Dictionary<string, int>();
            RollEffects.ReadPerDie(live, Side.Defender, GfNames.DefenseRoll);
            // What each face of a roll adds, so WoundsToDeal reads the face a die shows when the wounds are dealt rather than the face it was rolled on.
            var rolls = 0;
            void MapFaces(LiveSet set, Dice.RollPlan plan)
            {
                var roll = rolls++;
                foreach (var face in ctx.Faces(plan))
                {
                    if (RollEffects.ExtraWounds(set, Side.Defender, face, GfNames.DefenseRoll) is > 0 and var n) values[ExtraWoundsKey(roll, face.Raw)] = n;
                    if (RollEffects.CountedWounds(set, Side.Defender, face, GfNames.DefenseRoll) is > 0 and var k) values[CountedKey(roll, face.Raw)] = k;
                }
            }
            // count_wounds: wounds the defender counts as having taken, never dealt (GfMelee.WoundsTaken, the general morale test).
            int Counted(LiveSet set, RollRecord record)
            {
                var sum = 0;
                foreach (var d in record.Dice)
                    foreach (var (src, e) in RollEffects.CountedRows(set, Side.Defender, d, GfNames.DefenseRoll).ToList()) { sum += e.EffectiveAmount(src.Parameter); set.Read(src); }
                return sum;
            }
            // One live set carries every whole-group rule (a weapon's, a spell's); the hits split only by the set of per-die grants each die carries.
            foreach (var (key, count) in parent.Values.Where(kv => kv.Key.StartsWith("granted_")).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var granted = ParseGrantSet(key).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
                var group = count * multiplier; plain -= group;
                var plan = GfRolls.DefensePlan(ctx, granted, inCover, defenderModels, cover, picked);
                var record = ctx.Roll(plan, group, d => RollEffects.Triggers(granted, Side.Defender, d, GfNames.DefenseRoll));
                MapFaces(granted, plan);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(granted, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
                counted += Counted(granted, record);
            }
            if (plain > 0)
            {
                var plan = GfRolls.DefensePlan(ctx, live, inCover, defenderModels, cover, picked);
                var record = ctx.Roll(plan, plain, d => RollEffects.Triggers(live, Side.Defender, d, GfNames.DefenseRoll));
                MapFaces(live, plan);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(live, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
                counted += Counted(live, record);
            }
            values["wounds"] = wounds; values["extra_wounds"] = extraWounds;
            if (counted > 0) values[CountedWoundsKey] = counted;
            return new Complete(Consumed(live), values);
        }
    }
}
