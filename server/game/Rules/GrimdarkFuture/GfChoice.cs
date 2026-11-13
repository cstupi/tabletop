using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// A rule's choice of effect (army-book rules spec, batch 4: "When this unit is activated, pick one effect: …"). The choice is the rule's
/// set_condition rows at a point: GfHooks.ReactionsAt offers the rule once, `choose-effect` asks which row and sets its condition on the unit,
/// and the rule's effect rows read that condition (actor_state). A unit's chosen conditions clear when its next activation starts.
/// </summary>
public static class GfChoice
{
    public static Sequence ChooseEffect() => new(GfNames.ChooseEffectSeq, [new ChooseStep()]);

    /// <summary>The conditions an entry's choice rows set, in the entry's order.</summary>
    public static IReadOnlyList<string> Choices(CatalogEntry entry) =>
        entry.Effects.Where(IsChoice).Select(e => e.TargetCondition!).Distinct().ToList();

    static bool IsChoice(Effect e) => e is { Kind: EffectKind.SetCondition, TargetCondition: not null } && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint);

    /// <summary>The unit's chosen conditions, each cleared: what its activation's start writes before anything reads them.</summary>
    public static IReadOnlyList<StateChange> Cleared(GameState state, PropertySet props, Guid unit)
    {
        var held = state.Projection.Units[unit].Conditions;
        if (held.Count == 0) return [];
        var chosen = props.Entries.SelectMany(e => e.Effects).Where(IsChoice).Select(e => e.TargetCondition!).ToHashSet();
        return held.Where(chosen.Contains).Select(c => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, unit), c, false)).ToList();
    }

    /// <summary>The unit's conditions that a choice at the point sets, each cleared: what an attack's close writes for `attacked`.</summary>
    public static IReadOnlyList<StateChange> ClearedAtPoint(GameState state, PropertySet props, Guid unit, string point)
    {
        var held = state.Projection.Units[unit].Conditions;
        if (held.Count == 0) return [];
        var chosen = props.Entries.SelectMany(e => e.Effects).Where(e => IsChoice(e) && AtPoint(e, point)).Select(e => e.TargetCondition!).ToHashSet();
        return held.Where(chosen.Contains).Select(c => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, unit), c, false)).ToList();
    }

    static bool AtPoint(Effect e, string point) => e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains(point));

    /// <summary>
    /// The defender's choice per attack (spec → `attacked`): the attack's interaction at Point `attacked`; for each entry whose set_condition rows there act on
    /// the Defender with their other conditions holding, the defender's team is asked `choose_effect` in turn, one option per condition, pass allowed.
    /// The answers set each chosen condition on the defending unit and clear the entry's others; `attack-end` clears them all (GfAttack.Closing).
    /// </summary>
    public sealed class DefenderChoiceStep : IStep
    {
        public string Name => "defender-choice";
        public StepOutcome Run(StepContext ctx)
        {
            var c = ctx.Frame.Context;
            if (c.TargetUnitIds is not [var defender, ..] || Teams.Of(ctx.State, defender) is not { } team) return new Complete([]);
            var live = ctx.Settle(GfAttack.AttackInteraction(ctx, c.ActorUnitId, defender, c.WeaponIds, GfAttack.KindOf(ctx), c.Action) with { Point = GfNames.Attacked });
            var offers = live.AtPoint(Side.Defender, GfNames.Attacked).Where(x => IsChoice(x.Effect)).GroupBy(x => x.Source.Entry.Name)
                .Select(g => (Entry: g.First().Source.Entry, Choices: g.Select(x => x.Effect.TargetCondition!).Distinct().ToList())).ToList();
            if (offers.Count == 0) return new Complete([]);
            var answers = ctx.History.ClosedExceptConfirm.Where(a => a.Ask.Question == "choose_effect").ToList();
            if (answers.Count < offers.Count)
            {
                var (entry, choices) = offers[answers.Count];
                return new OpenAsks([ctx.Ask("choose_effect", $"{entry.Name}: pick one effect", team,
                    new Options(choices.Select(o => new AskOption(o, o, [defender])).ToList()), allowPass: true)]);
            }
            var subject = new Subject(SubjectKind.Unit, defender);
            var held = ctx.Projection.Units[defender].Conditions;
            var changes = new List<StateChange>();
            foreach (var ((entry, choices), answer) in offers.Zip(answers))
            {
                if (GfAnswers.Option(answer) is not { } option || !choices.Contains(option.Id)) continue;
                var others = entry.Effects.Where(e => IsChoice(e) && AtPoint(e, GfNames.Attacked)).Select(e => e.TargetCondition!).Where(o => o != option.Id && held.Contains(o)).Distinct();
                changes.AddRange([.. others.Select(o => new ConditionSet(subject, o, false)), new ConditionSet(subject, option.Id, true)]);
                ctx.Applied.Add(entry.Name);
            }
            return new Complete(changes);
        }
    }

    public const string RemoveTokens = "remove_tokens";

    /// <summary>The attack's tokens the attacking team may remove: per entry, the spent_marker grants on the target a unit of the attacker's team placed
    /// (Piercing Spotter, Precision Spotter, 3.5.1's Tags: "friendly units may remove tokens from their target"), in the order the target holds them.</summary>
    public static IReadOnlyList<(string Entry, int Count)> RemovableTokens(GameState state, PropertySet props, Guid attacker, Guid target) =>
        Removable(state, props, attacker, target).GroupBy(g => g.EntryName).Select(g => (g.Key, g.Count())).ToList();

    static IEnumerable<Grant> Removable(GameState state, PropertySet props, Guid attacker, Guid target)
    {
        var team = Teams.Of(state, attacker);
        return state.Projection.Units[target].Grants.Where(g => props.EffectById(g.EffectId) is { Duration: EffectDuration.SpentMarker } && Teams.Of(state, g.CasterUnitId) == team);
    }

    const string TokensPrefix = "tokens_";

    /// <summary>The tokens per entry the attacking team removed for this attack (`tokens_&lt;entry&gt;` of the nearest attack-sequence's `spotter-tokens`
    /// step), empty when none were or outside an attack. Interaction.RemovedTokens.</summary>
    public static IReadOnlyDictionary<string, int> Removed(FrameView? frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.AttackSequence)
                return f.Values.Where(kv => kv.Key.StartsWith(TokensPrefix, StringComparison.Ordinal) && kv.Value > 0).ToDictionary(kv => kv.Key[TokensPrefix.Length..], kv => kv.Value);
        return new Dictionary<string, int>();
    }

    /// <summary>The tokens of the entry the attacking team removed for this attack, 0 when none.</summary>
    public static int ChosenTokens(FrameView? frame, string entry) => Removed(frame).GetValueOrDefault(entry);

    /// <summary>
    /// Piercing Spotter / Precision Spotter: "When attacking, friendly units may remove tokens from their target before rolling to block to get +AP(X)
    /// on their weapons" / "before rolling to hit to get +X to hit rolls when attacking, where X is the number of removed tokens." Runs before
    /// `roll-to-hit`. The attacking team is asked `remove_tokens` once per entry with its tokens on the target, 0 to all of them (a pass is 0); the
    /// answer is recorded as `tokens_&lt;entry&gt;` and that many of the entry's tokens leave the target now. "Friendly units … when attacking" is the
    /// unit's attack on that target: a weapon group resolved in parts (GfGroups.Split) and every other group the same shooting or melee strike aims
    /// at the same target ask nothing again and copy the first attack's count (Shared), so X reaches all its weapons and no token is removed twice.
    /// </summary>
    public sealed class SpotterTokensStep : IStep
    {
        public string Name => "spotter-tokens";
        public StepOutcome Run(StepContext ctx)
        {
            var c = ctx.Frame.Context;
            if (c.ActorUnitId is not { } attacker || c.TargetUnitIds is not [var target, ..] || ctx.TeamOf(attacker) is not { } team || !ctx.Projection.Units.ContainsKey(target)) return new Complete([]);
            if (Shared(ctx, attacker, target) is { } shared) return new Complete([], shared.Count == 0 ? null : shared);
            var offers = RemovableTokens(ctx.State, ctx.Properties, attacker, target);
            if (offers.Count == 0) return new Complete([]);
            var answers = ctx.History.ClosedExceptConfirm.Where(a => a.Ask.Question == RemoveTokens).ToList();
            if (answers.Count < offers.Count)
            {
                var (entry, count) = offers[answers.Count];
                return new OpenAsks([ctx.Ask(RemoveTokens, $"{entry}: remove how many of {count} {(count == 1 ? "token" : "tokens")}?", team,
                    new Options(Enumerable.Range(0, count + 1).Select(n => new AskOption(n.ToString(), n.ToString(), n == 0 ? [] : [target])).ToList()), allowPass: true)]);
            }
            var values = new Dictionary<string, int>();
            var removed = new List<StateChange>();
            var held = Removable(ctx.State, ctx.Properties, attacker, target).ToList();
            foreach (var ((entry, count), answer) in offers.Zip(answers))
                if (GfAnswers.Option(answer) is { } option && int.TryParse(option.Id, out var n) && n is > 0 && n <= count)
                {
                    values[$"{TokensPrefix}{entry}"] = n;
                    removed.AddRange(held.Where(g => g.EntryName == entry).Take(n).Select(g => new GrantRemoved(target, g.Id)));
                    ctx.Applied.Add(entry);
                }
            return new Complete(removed, values);
        }

        /// <summary>What an earlier attack-sequence of the same pushing frame (a shooting's `fire-groups`, a melee), by the same unit at the same target,
        /// recorded at its own `spotter-tokens` step: its `tokens_` values (empty when it removed none). Null when no such attack reached the step.</summary>
        static IReadOnlyDictionary<string, int>? Shared(StepContext ctx, Guid attacker, Guid target)
        {
            if (ctx.Frame.Parent is not { } parent) return null;
            var log = EventApplier.EffectiveLog(ctx.State.Applier.Log).ToList();
            var start = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == parent.Frame.Id);
            if (start < 0) return null;
            var siblings = new HashSet<Guid>();
            var depth = 0;
            for (var i = start + 1; i < log.Count; i++)
                switch (log[i].Payload)
                {
                    case SequenceStarted s:
                        if (depth == 0 && s.FrameId != ctx.Frame.Id && s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == attacker && s.Context.TargetUnitIds is [var t, ..] && t == target)
                            siblings.Add(s.FrameId);
                        depth++; break;
                    case SequenceFinished or SequenceAbandoned: depth = Math.Max(0, depth - 1); break;
                    case StepCompleted done when done.Step == "spotter-tokens" && siblings.Contains(done.FrameId):
                        return (done.Values ?? new Dictionary<string, int>()).Where(kv => kv.Key.StartsWith(TokensPrefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            return null;
        }
    }

    /// <summary>Ask which effect; the answer sets its condition and clears the rule's others. A pass chooses nothing.</summary>
    sealed class ChooseStep : IStep
    {
        public string Name => "choose";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Frame.Context.Action is not { } name || ctx.Properties.Entry(name) is not { } entry || Choices(entry) is not { Count: > 0 } choices) return new Finish([]);
            if (ctx.History.LastClosed() is { } closed)
            {
                if (GfAnswers.Option(closed) is not { } option || !choices.Contains(option.Id)) return new Finish([]);
                var subject = new Subject(SubjectKind.Unit, unit);
                IReadOnlyList<StateChange> changes = [.. choices.Where(c => c != option.Id && ctx.Projection.Units[unit].Conditions.Contains(c)).Select(c => new ConditionSet(subject, c, false)),
                    new ConditionSet(subject, option.Id, true)];
                ctx.Applied.Add(entry.Name);
                return new Complete(changes);
            }
            return new OpenAsks([ctx.Ask("choose_effect", $"{entry.Name}: pick one effect", ctx.ActorTeam!.Value,
                new Options(choices.Select(c => new AskOption(c, c, [unit])).ToList()), allowPass: true)]);
        }
    }
}
