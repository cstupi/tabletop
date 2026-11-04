using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>The wounds a die showing `face` adds on the frame's `roll`-th defense roll, mapped when the roll is made and read when the wounds are dealt.</summary>
    static string ExtraWoundsKey(int roll, int face) => $"extra_wounds_{roll}_{face}";

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
            var wounds = 0; var extraWounds = 0; var plain = hits;
            var values = new Dictionary<string, int>();
            RollEffects.ReadPerDie(live, Side.Defender, GfNames.DefenseRoll);
            // What each face of a roll adds, so WoundsToDeal reads the face a die shows when the wounds are dealt rather than the face it was rolled on.
            var rolls = 0;
            void MapFaces(LiveSet set, Dice.RollPlan plan)
            {
                var roll = rolls++;
                foreach (var face in ctx.Faces(plan))
                    if (RollEffects.ExtraWounds(set, Side.Defender, face, GfNames.DefenseRoll) is > 0 and var n) values[ExtraWoundsKey(roll, face.Raw)] = n;
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
            }
            if (plain > 0)
            {
                var plan = GfRolls.DefensePlan(ctx, live, inCover, defenderModels, cover, picked);
                var record = ctx.Roll(plan, plain, d => RollEffects.Triggers(live, Side.Defender, d, GfNames.DefenseRoll));
                MapFaces(live, plan);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(live, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
            }
            values["wounds"] = wounds; values["extra_wounds"] = extraWounds;
            return new Complete(ctx.Consume(live), values);
        }
    }
}
