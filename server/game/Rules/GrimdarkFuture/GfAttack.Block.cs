using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>The wounds a die showing `face` adds on the frame's `roll`-th defense roll, mapped when the roll is made and read when the wounds are dealt.</summary>
    static string ExtraWoundsKey(int roll, int face) => $"extra_wounds_{roll}_{face}";

    /// <summary>The wounds a die showing `face` makes the defender count as having taken on the frame's `roll`-th defense roll through one count_wounds
    /// rule (`entry`), mapped as ExtraWoundsKey is, so the reader names the rule of the face a die shows.</summary>
    static string CountedKey(int roll, int face, string entry) => $"{CountedPrefix}{roll}_{face}_{entry}";
    const string CountedPrefix = "counted_";

    /// <summary>The roll-to-block value holding the counted wounds as rolled (its forced rerolls applied); CountedWounds reads the faces as they stand.</summary>
    public const string CountedWoundsKey = "counted_wounds";

    /// <summary>
    /// The wounds each unit counts as having taken (count_wounds) from the resolve-hits frames on it started at or after the effective log's index `from`,
    /// each read off its defense dice as they now stand (a forced reroll or an edit counts) through its `counted_&lt;roll&gt;_&lt;face&gt;_&lt;entry&gt;` map,
    /// with the rules that counted them. `shootingAndSpells` keeps the frames of an attack-sequence no melee pushed and of a spell's hits (owner ruling: Scourge on a
    /// spell's hits counts as shooting's does). Empty when `from` is negative.
    /// </summary>
    public static IReadOnlyDictionary<Guid, (int Count, IReadOnlyList<string> Rules)> CountedWounds(IReadOnlyList<GameEvent> log, int from, bool shootingAndSpells)
    {
        var result = new Dictionary<Guid, (int Count, IReadOnlyList<string> Rules)>();
        if (from < 0) return result;
        var open = new List<(Guid Id, string Sequence)>();
        var frames = new Dictionary<Guid, Guid>();   // resolve-hits frame -> its target unit
        for (var i = from; i < log.Count; i++)
            switch (log[i].Payload)
            {
                case SequenceStarted s:
                    // Shooting: an attack-sequence on top whose own pusher is not a melee; an attack-sequence started before `from` has no pusher here.
                    // A spell's hits: a spell-hits frame on top.
                    if (s.Sequence == GfNames.ResolveHits && s.Context.TargetUnitIds.Count > 0
                        && (!shootingAndSpells || (open is [.., var parent] && (parent.Sequence == GfNames.SpellHitsSeq
                            || parent.Sequence == GfNames.AttackSequence && (open.Count < 2 || open[^2].Sequence != GfNames.Melee)))))
                        frames[s.FrameId] = s.Context.TargetUnitIds[0];
                    open.Add((s.FrameId, s.Sequence)); break;
                case SequenceFinished f: open.RemoveAll(x => x.Id == f.FrameId); break;
                case SequenceAbandoned a when open.FindIndex(x => x.Id == a.FrameId) is var k and >= 0: open.RemoveRange(k, open.Count - k); break;
            }
        if (frames.Count == 0) return result;
        var steps = new Dictionary<Guid, List<StepCompleted>>();
        var replacements = new List<(Guid Frame, Guid Roll, IReadOnlyList<DieReplacement> Replaced)>();
        for (var i = from; i < log.Count; i++)
            switch (log[i].Payload)
            {
                case StepCompleted c when frames.ContainsKey(c.FrameId): (steps.TryGetValue(c.FrameId, out var list) ? list : steps[c.FrameId] = []).Add(c); break;
                case DiceRerolled d when frames.ContainsKey(d.FrameId): replacements.Add((d.FrameId, d.RollId, d.Replaced)); break;
                case DiceEdited d when frames.ContainsKey(d.FrameId): replacements.Add((d.FrameId, d.RollId, d.Replaced)); break;
            }
        foreach (var (frame, unit) in frames)
        {
            if (!steps.TryGetValue(frame, out var done)) continue;
            // counted_<roll>_<face>_<entry>: the entry may hold underscores, the roll and face never do.
            var map = done.SelectMany(s => s.Values ?? new Dictionary<string, int>()).Where(kv => kv.Key.StartsWith(CountedPrefix, StringComparison.Ordinal) && kv.Key != CountedWoundsKey)
                .Select(kv => (Parts: kv.Key[CountedPrefix.Length..].Split('_', 3), kv.Value)).Where(x => x.Parts.Length == 3 && int.TryParse(x.Parts[0], out _) && int.TryParse(x.Parts[1], out _))
                .Select(x => (Roll: int.Parse(x.Parts[0]), Face: int.Parse(x.Parts[1]), Entry: x.Parts[2], Amount: x.Value)).ToList();
            if (map.Count == 0) continue;
            var rolls = done.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
            var dice = rolls.ToDictionary(r => r.Id, r => r.Dice.ToList());
            foreach (var (_, roll, replaced) in replacements.Where(r => r.Frame == frame))
                if (dice.TryGetValue(roll, out var faces)) foreach (var r in replaced) faces[r.Index] = r.New;
            var count = 0; var rules = new List<string>();
            for (var i = 0; i < rolls.Count; i++)
                foreach (var d in dice[rolls[i].Id])
                    foreach (var m in map.Where(m => m.Roll == i && m.Face == d.Raw)) { count += m.Amount; rules.Add(m.Entry); }
            if (count == 0) continue;
            var (had, hadRules) = result.GetValueOrDefault(unit, (0, []));
            result[unit] = (had + count, [.. hadRules.Concat(rules).Distinct()]);
        }
        return result;
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
            // Parry: the hits its 6+ ignored are not blocked (GfParry), taken from the hits with the most per-die grants first.
            var all = HitsFrom(parent);
            var parried = Math.Min(GfParry.Parried(ctx.Frame), Math.Max(0, all));
            var hits = all - parried;
            if (hits <= 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            // Heavy Impact: "Counts as having Impact(X) with hits that have AP(1)": the hits of its own models' pool (GfMelee.HeavyImpactHits) block as a
            // group with AP(1), in the melee's weapon-less block that is Impact's.
            var heavy = parent.Frame.Sequence == GfNames.Melee && ctx.Frame.Context.WeaponIds.Count == 0 ? Math.Min(GfMelee.HeavyImpactHits(parent), all) : 0;
            var heavyKey = GrantKey([("AP", 1)])!;
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
            var wounds = 0; var extraWounds = 0; var counted = 0;
            // The hits split only by the set of per-die grants each die carries; the parried ones go from the groups with the most grants first.
            var groups = parent.Values.Where(kv => kv.Key.StartsWith("granted_")).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (Key: kv.Key, Size: kv.Value * multiplier)).ToList();
            if (heavy > 0) groups.Add((heavyKey, heavy));
            var plain = all - groups.Sum(g => g.Size);
            var toIgnore = parried;
            foreach (var i in Enumerable.Range(0, groups.Count).OrderByDescending(i => ParseGrantSet(groups[i].Key).Count).ThenBy(i => i).ToList())
            {
                var cut = Math.Min(toIgnore, groups[i].Size);
                groups[i] = groups[i] with { Size = groups[i].Size - cut }; toIgnore -= cut;
            }
            plain -= toIgnore;
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
                    foreach (var rule in RollEffects.CountedRows(set, Side.Defender, face, GfNames.DefenseRoll).GroupBy(x => x.Source.Entry.Name))
                        if (rule.Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter)) is > 0 and var k) values[CountedKey(roll, face.Raw, rule.Key)] = k;
                }
            }
            // count_wounds: wounds the defender counts as having taken, never dealt (GfMelee.WoundsTaken, the general morale test).
            // Read on the faces the dice show once the roll's forced rerolls are made.
            int Counted(LiveSet set, RollRecord record)
            {
                var sum = 0;
                var dice = record.Dice.ToList();
                foreach (var r in ctx.ForcedRerolls.Where(f => f.RollId == record.Id).SelectMany(f => f.Replaced)) dice[r.Index] = r.New;
                foreach (var d in dice)
                    foreach (var (src, e) in RollEffects.CountedRows(set, Side.Defender, d, GfNames.DefenseRoll).ToList()) { sum += e.EffectiveAmount(src.Parameter); set.Read(src); }
                return sum;
            }
            // One live set carries every whole-group rule (a weapon's, a spell's); each group adds its per-die grants.
            if (groups.Any(g => g.Key == heavyKey && g.Size > 0) && heavy > 0) ctx.Applied.Add(GfNames.HeavyImpact);
            foreach (var (key, group) in groups.Where(g => g.Size > 0))
            {
                var granted = ParseGrantSet(key).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount, heavy > 0 && key == heavyKey ? GfNames.HeavyImpact : null));
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
            return new Complete(ctx.Consume(live), values);
        }
    }
}
