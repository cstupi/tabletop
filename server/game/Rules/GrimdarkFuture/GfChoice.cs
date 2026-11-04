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
