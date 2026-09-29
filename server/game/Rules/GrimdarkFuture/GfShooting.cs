using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Takedown);

public static class GfShooting
{
    public static Sequence Shooting() => new(GfNames.Shooting, [new DeclareTargetsStep(), new FireGroupsStep()]);

    /// <summary>
    /// One group per ranged weapon type over the living models (p.8). A Limited weapon already fired this game is left out (p.14).
    /// A Takedown weapon type splits into one group per model, because p.14 gives the pick to "This model": each takedown model
    /// names its own target and rolls its own dice. The split group's name carries the model's ordinal, since the name is the key
    /// the target declaration and fire-groups use, and two groups sharing one name would collapse into a single target.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Groups(GameState state, PropertySet props, Guid unit)
    {
        var used = EventApplier.EffectiveLog(state.Applier.Log).Where(e => e.Payload is SequenceStarted).SelectMany(e => ((SequenceStarted)e.Payload).Context.WeaponIds).ToHashSet();
        var groups = new List<WeaponGroup>();
        foreach (var g in Lookup.AliveModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => w.Stats.ContainsKey(GfNames.Range))
                     .Where(w => !(w.Properties.Any(p => p.Name == "Limited") && used.Contains(w.Id))).GroupBy(w => w.Name))
        {
            var ids = g.Select(w => w.Id).ToList();
            if (!GfAttack.IsTakedownGroup(state, ids)) { groups.Add(new WeaponGroup(g.Key, ids, false)); continue; }
            groups.AddRange(ids.Select((id, i) => new WeaponGroup(ids.Count == 1 ? g.Key : $"{g.Key} ({i + 1})", [id], true)));
        }
        return groups;
    }

    public static IReadOnlyList<Guid> ValidTargets(StepContext ctx, Guid unit, WeaponGroup group)
    {
        var team = ctx.TeamOf(unit);
        // The in-play reading: an enemy unit inside a container is still a candidate, and EligibleModels is what rules it out (or, for a fellow occupant, in).
        return ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team && Lookup.AliveModels(ctx.State, u.Id).Count > 0).Select(u => u.Id).Where(target =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, unit, target, group.WeaponIds, AttackKind.Shooting, ctx.Frame.Context.Action));
            return GfAttack.EligibleModels(ctx.State, live, unit, target, group.WeaponIds, AttackKind.Shooting).Count > 0;
        }).ToList();
    }

    sealed class DeclareTargetsStep : IStep
    {
        public string Name => "declare-targets";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Select(g => (Group: g, Targets: ValidTargets(ctx, unit, g))).Where(x => x.Targets.Count > 0).ToList();
            if (groups.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("declare_targets", "Declare weapon groups and up to two targets", ctx.ActorTeam!.Value,
                new Assignment(groups.Select(x => new AskOption(x.Group.Name, x.Group.Name, x.Group.WeaponIds)).ToList(), groups.SelectMany(x => x.Targets).Distinct().ToList(), 2))]);
        }
    }

    /// <summary>
    /// p.8: a unit's weapons are fired simultaneously, so every group's attack counts and the defending unit's model count are settled
    /// on this frame before the first group is pushed, and a later group is neither shortened, nor capped lower, nor left blocking a
    /// joined hero on its own Defense by an earlier group's casualties. Two phases over
    /// `attacks_0`: record, then push one `attack-sequence` at a time, Takedown groups first (p.14).
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
            if (ctx.Frame.Frame.Steps[0].Answer is not Assigned assigned) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Where(g => assigned.Targets.ContainsKey(g.Name)).OrderByDescending(g => g.Takedown).ThenBy(g => g.Name).ToList();
            if (groups.Count == 0) return new Complete([]);                                   // nothing was assigned a target: also the guard that bounds the recording phase
            if (!ctx.Frame.Values.ContainsKey("attacks_0"))
            {
                var settled = new Dictionary<string, int>();
                foreach (var (g, i) in groups.Select((g, i) => (g, i)))
                {
                    var byQuality = GfAttack.AttacksAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds, AttackKind.Shooting);
                    settled[$"attacks_{i}"] = byQuality.Sum(x => x.Attacks);
                    foreach (var (quality, attacks) in byQuality) settled[$"attacks_{i}_q{quality}"] = attacks;
                    settled[$"defender_models_{i}"] = Lookup.LivingModels(ctx.State, assigned.Targets[g.Name]).Count;   // p.13's Blast cap and p.14's Hero sunset, frozen with the counts
                    settled[$"in_cover_{i}"] = GfAttack.InCoverAgainst(ctx, unit, assigned.Targets[g.Name], g.WeaponIds) ? 1 : 0;   // p.11's majority, frozen with them
                }
                return new Complete([], settled, Repeat: true);
            }
            var pushed = ctx.History.Children.Count;
            if (pushed < groups.Count && (pushed == 0 || ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned))
                return new Push(GfNames.AttackSequence, new SequenceContext(unit, [assigned.Targets[groups[pushed].Name]], groups[pushed].WeaponIds, ctx.Frame.Context.Action));
            return new Complete([]);
        }
    }
}
