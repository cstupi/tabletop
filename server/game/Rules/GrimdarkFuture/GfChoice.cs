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
