using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfShooting
{
    public static Sequence Shooting() => new(GfNames.Shooting, [new DeclareTargetsStep(), new FireGroupsStep(), new GfMoves.PointWindowStep(GfNames.AfterShooting, "after-shooting")]);

    /// <summary>
    /// One group per ranged weapon type over the living models (p.8), in GfGroups' order. A Limited weapon already fired is left out
    /// (p.14 "may only be used once per game"). Asked from inside a shooting frame, only `sequence_started` events ahead of that frame's
    /// own count, so the groups this frame fired do not shrink the list `fire-groups` indexes; asked from outside one, every start counts.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Groups(GameState state, PropertySet props, Guid unit, Guid? shootingFrame)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var start = shootingFrame is { } frame ? log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == frame) : -1;
        var used = log.Take(start < 0 ? log.Count : start).Where(e => e.Payload is SequenceStarted).SelectMany(e => ((SequenceStarted)e.Payload).Context.WeaponIds).ToHashSet();
        return GfGroups.Of(state, Lookup.AliveModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => w.Stats.ContainsKey(GfNames.Range))
            .Where(w => !(w.Properties.Any(p => p.Name == "Limited") && used.Contains(w.Id))));
    }

    public static IReadOnlyList<Guid> ValidTargets(StepContext ctx, Guid unit, WeaponGroup group)
    {
        var team = ctx.TeamOf(unit);
        // The in-play reading: an enemy unit inside a container is still a candidate, and EligibleModels is what rules it out (or, for a fellow occupant, in).
        return ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team && Lookup.AliveModels(ctx.State, u.Id).Count > 0).Select(u => u.Id).Where(target =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, unit, target, group.WeaponIds, AttackKind.Shooting, ctx.Frame.Context.Action));
            // p.7: "Rush - Moves 12" but can't shoot", unless Quick Shot is live for this target ("This model may shoot after using Rush actions"):
            // innate, or granted against this target (army-rule hooks → Shooting after Rush). Offering reads nothing; FireGroupsStep spends it.
            if (ctx.Frame.Context.Action == GfNames.Rush && !live.Has(Side.Attacker, GfNames.QuickShot)) return false;
            return GfAttack.EligibleModels(ctx.State, live, unit, target, group.WeaponIds, AttackKind.Shooting).Count > 0;
        }).ToList();
    }

    /// <summary>The shooting a Rush's Quick Shot enables is where it is applied: read its source on this target's live set and spend it, so a once grant goes with the shooting and an innate rule is untouched.</summary>
    static IReadOnlyList<StateChange> SpendQuickShot(StepContext ctx, Guid unit, Guid target, IReadOnlyList<Guid> weapons)
    {
        var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, unit, target, weapons, AttackKind.Shooting, ctx.Frame.Context.Action));
        foreach (var p in live.On(Side.Attacker).Where(p => p.Entry.Name == GfNames.QuickShot)) live.Read(p);
        return RollEffects.Consume(live);
    }

    sealed class DeclareTargetsStep : IStep
    {
        public string Name => "declare-targets";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit, ctx.Frame.Id).Select(g => (Group: g, Targets: ValidTargets(ctx, unit, g))).Where(x => x.Targets.Count > 0).ToList();
            if (groups.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("declare_targets", "Declare weapon groups and up to two targets", ctx.ActorTeam!.Value,
                new Assignment(groups.Select(x => new AskOption(x.Group.Name, x.Group.Name, x.Group.WeaponIds)).ToList(), groups.SelectMany(x => x.Targets).Distinct().ToList(), 2))]);
        }
    }

    /// <summary>
    /// p.8: a unit's weapons are fired simultaneously, so every group's attack counts and the defending unit's model count are settled
    /// on this frame before the first group is pushed, and a later group is neither shortened, nor capped lower, nor left blocking a
    /// joined hero on its own Defense by an earlier group's casualties. Two phases over
    /// `attacks_0`: record, then push one `attack-sequence` at a time, groups in GfGroups' order (Takedown, Deadly, the rest).
    ///
    /// A group's attacks are recorded per Quality value as `attacks_&lt;i&gt;_q&lt;n&gt;`, since p.4 tests each against *"the model's Quality
    /// value"* and a joined hero brings its own, with `attacks_&lt;i&gt;` kept as the group's total for a reader that wants one number.
    /// `defender_models_&lt;i&gt;` is read by p.13's `Blast` cap and by p.14's *"until all other models have been killed"*.
    /// `in_cover_&lt;i&gt;` is p.11's majority, settled here for the same reason and read by `roll-to-block`.
    /// </summary>
    sealed class FireGroupsStep : IStep
    {
        public string Name => "fire-groups";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            // Only a group the Ask listed, aimed at a unit the Ask offered: what else was assigned is dropped.
            if (ctx.Frame.Frame.Steps.FirstOrDefault(s => s.Step == "declare-targets")?.Answer is not Assigned answered || FrameReader.Recorded(ctx.State, ctx.Frame.Id, "declare_targets")?.Legal is not Assignment offer) return new Complete([]);
            var assigned = answered with { Targets = answered.Targets.Where(kv => offer.Items.Any(i => i.Id == kv.Key) && offer.Targets.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value) };
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit, ctx.Frame.Id).Where(g => assigned.Targets.ContainsKey(g.Name)).ToList();
            if (groups.Count == 0) return new Complete([]);                                   // nothing was assigned a target: also the guard that bounds the recording phase
            if (!ctx.Frame.Values.ContainsKey("attacks_0"))
            {
                var settled = new Dictionary<string, int>();
                var spent = new List<StateChange>();
                foreach (var (g, i) in groups.Select((g, i) => (g, i)))
                {
                    var byQuality = GfAttack.AttacksAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds, AttackKind.Shooting, ctx.Frame.Context.Action);
                    if (ctx.Frame.Context.Action == GfNames.Rush) spent.AddRange(SpendQuickShot(ctx, unit, assigned.Targets[g.Name], g.WeaponIds));
                    settled[$"attacks_{i}"] = byQuality.Sum(x => x.Attacks);
                    foreach (var (quality, attacks) in byQuality) settled[$"attacks_{i}_q{quality}"] = attacks;
                    settled[$"defender_models_{i}"] = Lookup.LivingModels(ctx.State, assigned.Targets[g.Name]).Count;   // p.13's Blast cap and p.14's Hero sunset, frozen with the counts
                    settled[$"in_cover_{i}"] = GfAttack.InCoverAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds) ? 1 : 0;   // p.11's majority, frozen with them
                }
                return new Complete(spent.Distinct().ToList(), settled, Repeat: true);
            }
            var pushed = ctx.History.Children.Count;
            if (pushed < groups.Count && (pushed == 0 || ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned))
                return new Push(GfNames.AttackSequence, new SequenceContext(unit, [assigned.Targets[groups[pushed].Name]], groups[pushed].WeaponIds, ctx.Frame.Context.Action));
            return new Complete([]);
        }
    }
}
