using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    sealed class DetermineAttacksStep : IStep
    {
        public string Name => "determine-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var eligible = EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, ctx.Frame.Context.WeaponIds, kind);
            // `attacks_<i>` is the presence signal rather than the per-Quality keys, because a group the pusher settled at zero
            // attacks writes no per-Quality key at all and must still not be recounted here.
            var settled = Pushed(ctx.Frame, "attacks");
            var byQuality = settled is null ? Attacks(ctx, live, eligible, ctx.Frame.Context.WeaponIds, kind) : PushedByQuality(ctx.Frame);
            var values = new Dictionary<string, int>
            {
                ["attacks"] = settled ?? byQuality.Sum(x => x.Attacks),
                ["eligible_models"] = eligible.Count,
                ["defender_models"] = Pushed(ctx.Frame, "defender_models") ?? Lookup.LivingModels(ctx.State, ix.Defender.UnitId).Count,
            };
            if (Pushed(ctx.Frame, "in_cover") is { } pushedCover)
            {
                values["in_cover"] = pushedCover;
                foreach (var key in new[] { "cover_models", "cover_of", "cover_indirect" }) if (Pushed(ctx.Frame, key) is { } v) values[key] = v;
            }
            else if (kind == AttackKind.Shooting) WriteCover(values, CoverOf(ctx.State, live, ix.Defender.UnitId, eligible));
            else values["in_cover"] = 0;
            foreach (var (quality, attacks) in byQuality) values[$"attacks_q{quality}"] = attacks;
            if (GfEdition.Of(ctx.State).AttackRangeRecorded && ix.RangeInches is { } range) values[RangeKey] = (int)Math.Round(range * 100);
            if (kind == AttackKind.Shooting)
                foreach (var (shooter, target, point) in Aims(ctx.State, live, ix.Attacker.UnitId, ix.Defender.UnitId, eligible, ctx.Frame.Context.WeaponIds))
                    values[$"aim_{shooter}_{target}"] = point;
            return values["attacks"] == 0 ? new Finish([.. ctx.Consume(live), .. ReleaseHeld(ctx)], values) : new Complete(ctx.Consume(live), values);
        }

    }

    /// <summary>
    /// A number the pushing step settled for this child before it pushed anything, keyed by the child's ordinal among that step's
    /// children: `defender_models_&lt;i&gt;`, and `attacks_&lt;i&gt;_q&lt;n&gt;` through `ByQuality`. Read off the pusher's recording run, the
    /// parent's last completion, which is still marked as a repeat while its children run; a frame's merged values would mix in the
    /// keys an earlier striking step of the same melee wrote. Null when the pusher recorded none.
    /// </summary>
    static int? Pushed(FrameView frame, string prefix) =>
        Recording(frame) is { } values && values.TryGetValue($"{prefix}_{frame.Parent!.History.Children.Count - 1}", out var n) ? n : null;

    /// <summary>The same read for the per-Quality keys, which are a set rather than one number. Empty when the pusher recorded none.</summary>
    static IReadOnlyList<(int Quality, int Attacks)> PushedByQuality(FrameView frame) =>
        Recording(frame) is { } values ? ByQuality(values, $"attacks_{frame.Parent!.History.Children.Count - 1}_q") : [];

    static IReadOnlyDictionary<string, int>? Recording(FrameView frame) =>
        frame.Parent is { History.Children.Count: > 0 } parent && parent.Frame.Steps is [.., var last] && last.Values is { } values && values.ContainsKey(StepRepeat.Key) ? values : null;

    /// <summary>
    /// p.14 Takedown: the model names one model in the target unit as the target of its wounds. A target declaration, so it is made
    /// before this group's hit roll (p.8), and after determine-attacks, so a group with nothing in range never asks. One pick per
    /// group and not per shot: GfGroups gives a takedown weapon type one group per model, so the group is one model's dice
    /// at one Quality value and roll-to-hit pools them into one roll. deal-wounds reads the answer
    /// back through TakedownModel and assigns to that model without opening an allocation.
    /// </summary>
    sealed class PickTargetModelStep : IStep
    {
        public string Name => "pick-target-model";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var (weapons, target) = (ctx.Frame.Context.WeaponIds, ctx.Frame.Context.TargetUnitIds[0]);
            if (!IsTakedownGroup(ctx.State, weapons)) return new Complete([]);
            var models = Lookup.AliveModels(ctx.State, target);
            if (models.Count < 2) return new Complete([]);   // one model to choose from: the ordinary allocation reaches it anyway
            var reachable = Pickable(ctx, weapons, target, models);
            return new OpenAsks([ctx.Ask("takedown_target", "Pick the model this takedown weapon targets", ctx.ActorTeam!.Value,
                new PieceSelection(reachable.Count > 0 ? reachable : models, 1, 1))]);
        }

        /// <summary>
        /// "resolved as if it was a unit of [1]": a model the group's weapon is in range of and, shooting without Indirect, sees (p.8), with the
        /// target's own models out of the way, since "other models in the target's unit don't block line of sight ... to the target model".
        /// Melee and a container's occupants (no sight line inside a box) are not narrowed.
        /// </summary>
        static IReadOnlyList<Guid> Pickable(StepContext ctx, IReadOnlyList<Guid> weapons, Guid target, IReadOnlyList<Guid> models)
        {
            var (_, live, kind) = Settle(ctx);
            var attacker = ctx.Frame.Context.ActorUnitId!.Value;
            if (kind != AttackKind.Shooting || Battlefield.InsideReach(ctx.State, attacker, target) is not null) return models;
            var sight = IndirectApplies(live) ? null : Sight.From(ctx.State, attacker, alsoIgnoring: target);
            var shooters = Lookup.LivingModels(ctx.State, attacker).Select(m => (Id: m, Range: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id))
                .Select(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0).DefaultIfEmpty(-1).Max())).Where(s => s.Range >= 0).ToList();
            return models.Where(t => ctx.Projection.Models[t].Location is OnTable
                && shooters.Any(s => Battlefield.Distance(ctx.State, s.Id, t) <= s.Range && (sight is null || sight.CanSee(s.Id, t)))).ToList();
        }
    }

    /// <summary>
    /// p.8's second step: *"take as many Quality tests as attacks"*. One pooled roll per Quality value the attacking models brought,
    /// in ascending order, since p.4 scores each test against *"the model's Quality value"* and a joined hero brings its own.
    /// p.4's *"having dice of multiple colors ... and then roll them all at once"* is the tabletop procedure for it. The
    /// counts are `determine-attacks`' `attacks_q&lt;n&gt;`, so nothing is recounted here and the pools are the ones the action started
    /// with. What a face decides is `score-hits`' (opr-sequences → Dice edits); only the dice an extra-attacks row adds are drawn
    /// here, on the faces as rolled, because drawing is the roll's.
    /// </summary>
    sealed class RollToHitStep : IStep
    {
        public string Name => "roll-to-hit";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            // p.13 caps Blast at "as many hits as models in the target unit" without saying when they are counted. p.8 fires all
            // weapons simultaneously, so the cap is fixed at the same moment as the attack counts: this reads the defender's model
            // count that determine-attacks carried down from the pushing frame.
            if (kind == AttackKind.Shooting && IndirectApplies(live)) live.Spend(Side.Attacker, GfNames.Indirect);   // p.14: applies to this attack (its to-hit row is read below), so a granted one is spent here; it is recorded where it acts (EligibleModels, InCover, its to-hit row)
            // p.14 Takedown: the picked model is "resolved as if it was a unit of [1]", so a Takedown group's cap is that one model.
            var living = Lookup.LivingModels(ctx.State, ix.Defender!.UnitId);
            var cap = TakedownModel(ctx.Frame) is { } picked && living.Contains(picked) ? 1 : ctx.Frame.Value("defender_models", living.Count);
            var values = new Dictionary<string, int> { ["hit_multiplier"] = RollEffects.HitMultiplier(live, Side.Attacker, cap) };
            var rollers = HazardRollers(ctx, live, ix, kind);
            foreach (var (quality, attacks) in ByQuality(ctx.Frame.Values, "attacks_q"))
            {
                var plan = GfRolls.HitPlan(ctx, live, kind, quality);
                IReadOnlyList<string>? Triggers(Die d) => plan.HitsTriggerNothing && d.Success ? null : RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit);
                foreach (var (model, dice) in Shares(rollers, quality, attacks))
                {
                    var rolled = ctx.Roll(plan, dice, d => HazardLabels(ctx, Triggers(d), model));
                    MarkRoller(ctx, values, model);
                    var added = rolled.HitsTriggerNothing == true ? 0 : rolled.Dice.Sum(d => AddedAttacks(live, d));
                    // "This rule doesn't apply to newly generated attacks": the added dice add no attacks of their own.
                    if (added > 0) { ctx.Roll(plan, added, d => HazardLabels(ctx, RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit, attacks: false), model)); MarkRoller(ctx, values, model); }
                }
            }
            return new Complete(GfDeferred.Leave(ctx.Consume(live), ix, l => RollEffects.ReadPerDie(l, Side.Attacker, GfNames.Hit)), values);
        }

        /// <summary>The attacks a successful die's extra-attacks rows add, recorded as having acted; nothing else a face decides is read here.</summary>
        static int AddedAttacks(LiveSet live, Die die)
        {
            if (!die.Success) return 0;
            var adds = live.Effects(Side.Attacker, EffectKind.AddHit, new DieContext(GfNames.Hit, die.Raw, die.Modified, die.Success))
                .Where(x => x.Effect.TargetQuantity == TargetQuantity.Attacks && x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).ToList();
            foreach (var x in adds) live.Record(x.Source);
            return adds.Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        }
    }

    /// <summary>
    /// What the frame's `hit` dice decide, read as they now stand, so an edit made after the roll counts (opr-sequences → Dice edits):
    /// `extra_hits` and every `granted_&lt;entry&gt;_&lt;amount&gt;` on a success, and `self_wounds` on any die, summed across every pool and
    /// the added dice, since they belong to the group's hits rather than to one pool. `wounds` is `self_wounds`, for the self-wounds
    /// deal. Shared by `attack-sequence` and `spell-hits`; a spell's frame finishes here when it scored no hit.
    /// </summary>
    public sealed class ScoreHitsStep(bool finishWithoutHits = false) : IStep
    {
        public string Name => "score-hits";
        public StepOutcome Run(StepContext ctx)
        {
            var (_, live, _) = Settle(ctx);
            var attributed = HazardAttributed(ctx, live);
            var rolls = ctx.Frame.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["self_wounds"] = 0 };
            // 3.6.0 p.9: a fatigued hit is no roll of 6 for special rules, so its die reads nothing; a failed die's rows (Hazardous) still act.
            foreach (var (die, roller) in rolls.Zip(HitRollers(ctx.Frame)).SelectMany(x => x.First.Dice.Where(d => !(x.First.HitsTriggerNothing == true && d.Success)).Select(d => (d, x.Second))))
            {
                var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                AddSelfWounds(ctx, values, roller, effects.ExtraWounds, attributed);
                if (!die.Success) continue;
                values["extra_hits"] += effects.ExtraHits;
                if (GrantKey(effects.Grants) is { } key) values[key] = values.GetValueOrDefault(key) + 1;
            }
            if (rolls.Count > 0) RollEffects.ReadPerDie(live, Side.Attacker, GfNames.Hit);
            values["wounds"] = values["self_wounds"];
            var spent = ctx.Consume(live);
            var scored = ctx.Frame.Value("hits") + rolls.Sum(Dice.RollOutcome.Successes);
            return finishWithoutHits && scored == 0 ? new Finish([.. spent, .. ReleaseHeld(ctx)], values) : new Complete(spent, values);
        }
    }
}
