using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book rules that change who casts and with what (army-book rules spec, batch 7). Caster Group: "Pick one model with this rule in this unit
/// to have Caster(X), where X is the total number of models with this rule in this unit. If the model is killed, pick another to be the new caster,
/// and transfer all spell tokens to it. The caster loses all unspent spell tokens at the end of the round." Spell Accumulator: "Gets X accumulator
/// tokens at the start of each round, but can't hold more than 6 tokens at once. Casters from other friendly units within 12" may spend this model's
/// accumulator tokens as if they were their own spell tokens." Spell Conduit: "Casters within 12" that are from other friendly units may cast spells as
/// if they were in this model's position, and get +1 to casting rolls when doing so." Both: "Friendly casters may only use this rule if this unit isn't
/// Shaken."
/// </summary>
public static class GfSpellSupport
{
    public const string CasterGroup = "Caster Group", SpellAccumulator = "Spell Accumulator", SpellConduit = "Spell Conduit";
    public const double SupportRange = 12;

    static bool Carries(GameState state, Guid model, string rule) => state.Projection.Models[model].Slot.Properties.Any(p => p.Name == rule);

    /// <summary>The model condition naming the Caster Group model the player picked ("Pick one model with this rule in this unit to have Caster(X)").</summary>
    public const string GroupCaster = "group caster";
    public const string PickSeq = "caster-group-pick", Question = "caster_group";

    /// <summary>The Caster Group's models still in play, in slot order.</summary>
    static IReadOnlyList<Guid> Group(GameState state, Guid unit) =>
        Lookup.AliveModels(state, unit).Where(m => Carries(state, m, CasterGroup)).OrderBy(m => state.Projection.Models[m].Slot.Ordinal).ToList();

    /// <summary>The Caster Group model that has Caster(X): the one picked (GroupCaster) while it is in play, else, until a pick is made, the group's first
    /// model in play by its place in the unit.</summary>
    public static Guid? DesignatedCaster(GameState state, Guid unit) =>
        Group(state, unit) is { Count: > 0 } group ? group.FirstOrDefault(m => state.Projection.Models[m].Conditions.Contains(GroupCaster), group[0]) : null;

    /// <summary>The units whose Caster Group has no picked model in play: asked at the round's start (GrimdarkFutureRules.RoundBoundaryStep), the first
    /// time the group gains tokens, and again when a caster killed outside deal-wounds left none.</summary>
    public static IReadOnlyList<Guid> Unpicked(GameState state) =>
        state.Projection.Units.Values.Where(u => Group(state, u.Id) is { Count: > 0 } group && !group.Any(m => state.Projection.Models[m].Conditions.Contains(GroupCaster)))
            .Select(u => u.Id).ToList();

    public static Sequence Pick() => new(PickSeq, [new PickStep()]);

    /// <summary>The pick: the owning team chooses a group model (PieceSelection, a pass keeps the designated one); it gets the condition and the
    /// designated model's tokens. With one model left there is nothing to ask.</summary>
    sealed class PickStep : IStep
    {
        public string Name => "pick-caster";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var group = Group(ctx.State, unit);
            if (group.Count == 0 || DesignatedCaster(ctx.State, unit) is not { } current) return new Complete([]);
            if (group.Count > 1 && ctx.History.LastClosed(Question) is null) return new OpenAsks([Ask(ctx, unit, group)]);
            var picked = ctx.History.LastClosed(Question)?.Single is SelectedPieces { PieceIds: [var m] } && group.Contains(m) ? m : current;
            ctx.Applied.Add(CasterGroup);
            return new Complete(Designate(ctx.State, current, picked, ctx.Projection.Models[current].SpellTokens));
        }
    }

    static Ask Ask(StepContext ctx, Guid unit, IReadOnlyList<Guid> group) =>
        ctx.Ask(Question, $"Caster Group: pick the caster of {ctx.Projection.Units[unit].Definition.Name}", ctx.TeamOf(unit)!.Value, new PieceSelection(group, 1, 1), allowPass: true);

    /// <summary>The picked model gets the condition and `tokens`; the model that held them (when another) gets none.</summary>
    static IReadOnlyList<StateChange> Designate(GameState state, Guid from, Guid to, int tokens)
    {
        var changes = new List<StateChange>();
        if (from != to) changes.Add(new SpellTokensSet(from, 0));
        changes.Add(new SpellTokensSet(to, Math.Min(GfNames.TokenCap, (from == to ? 0 : state.Projection.Models[to].SpellTokens) + tokens)));
        if (!state.Projection.Models[to].Conditions.Contains(GroupCaster)) changes.Add(new ConditionSet(new Subject(SubjectKind.Model, to), GroupCaster, true));
        return changes;
    }

    /// <summary>A model's Caster(X): its own, or the group's X when it is its Caster Group's designated caster. Null when it is no caster.</summary>
    public static int? CasterValue(GameState state, Guid model)
    {
        if (Lookup.ModelParameter(state, model, GfNames.Caster) is { } own) return own;
        var unit = state.Projection.Models[model].UnitId;
        return Carries(state, model, CasterGroup) && DesignatedCaster(state, unit) == model ? Lookup.AliveModels(state, unit).Count(m => Carries(state, m, CasterGroup)) : null;
    }

    /// <summary>The round start's tokens: a caster's X capped at 6; a Caster Group's caster starts from none ("loses all unspent spell tokens at the end of the
    /// round"); an accumulator's X capped at 6.</summary>
    public static IReadOnlyList<StateChange> RoundStartTokens(GameState state)
    {
        var changes = new List<StateChange>();
        foreach (var m in state.Projection.Models.Values)
        {
            if (Lookup.ModelParameter(state, m.Id, GfNames.Caster) is { } x && x > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + x)));
            else if (CasterValue(state, m.Id) is { } group && group > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, group)));
            else if (Lookup.ModelParameter(state, m.Id, SpellAccumulator) is { } y && y > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + y)));
        }
        return changes;
    }

    /// <summary>A supporting model's unit's rule reaches the caster: a friendly unit other than the caster's, not Shaken, the model on the table within 12".</summary>
    static bool Reaches(GameState state, Guid casterModel, ModelInPlay support) =>
        support.UnitId != state.Projection.Models[casterModel].UnitId && support.Location is OnTable
        && Teams.Of(state, support.UnitId) == Teams.Of(state, state.Projection.Models[casterModel].UnitId)
        && !state.Projection.Units[support.UnitId].Conditions.Contains(GfNames.Shaken)
        && state.Projection.Models[casterModel].Location is OnTable && Battlefield.Distance(state, casterModel, support.Id) <= SupportRange;

    /// <summary>The accumulators a caster may spend from, with tokens.</summary>
    public static IReadOnlyList<ModelInPlay> Accumulators(GameState state, Guid casterModel) =>
        state.Projection.Models.Values.Where(m => m.SpellTokens > 0 && Lookup.ModelParameter(state, m.Id, SpellAccumulator) is not null && CasterValue(state, m.Id) is null && Reaches(state, casterModel, m)).ToList();

    /// <summary>The tokens a caster may pay a spell's value from: its own and the accumulators'.</summary>
    public static int Available(GameState state, Guid casterModel) => state.Projection.Models[casterModel].SpellTokens + Accumulators(state, casterModel).Sum(m => m.SpellTokens);

    /// <summary>A spell's value paid: the caster's own tokens first, then the accumulators' in turn.</summary>
    public static IReadOnlyList<StateChange> Pay(GameState state, Guid casterModel, int cost)
    {
        var own = state.Projection.Models[casterModel].SpellTokens;
        var changes = new List<StateChange> { new SpellTokensSet(casterModel, Math.Max(0, own - cost)) };
        var left = cost - own;
        foreach (var m in Accumulators(state, casterModel))
        {
            if (left <= 0) break;
            var take = Math.Min(left, m.SpellTokens);
            changes.Add(new SpellTokensSet(m.Id, m.SpellTokens - take));
            left -= take;
        }
        return changes;
    }

    /// <summary>The conduits a caster may cast from.</summary>
    public static IReadOnlyList<Guid> Conduits(GameState state, Guid casterModel) =>
        state.Projection.Models.Values.Where(m => Carries(state, m.Id, SpellConduit) && Reaches(state, casterModel, m)).Select(m => m.Id).ToList();

    /// <summary>deal-wounds' step for a Caster Group whose caster was killed: "If the model is killed, pick another to be the new caster, and transfer all
    /// spell tokens to it": the owning team picks among the group's models left (the same Ask as the round start's; one left needs none) and the tokens pass.</summary>
    public sealed class TransferStep : IStep
    {
        public string Name => "caster-group";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Context.TargetUnitIds is not [var unit, ..]) return new Complete([]);
            var dead = ctx.Frame.Values.Keys.Where(k => k.StartsWith("killed_", StringComparison.Ordinal)).Select(k => Guid.ParseExact(k["killed_".Length..], "N"))
                .Where(m => ctx.Projection.Models.TryGetValue(m, out var d) && d.UnitId == unit && Carries(ctx.State, m, CasterGroup)
                    && (d.SpellTokens > 0 || d.Conditions.Contains(GroupCaster))).ToList();
            var group = Group(ctx.State, unit);
            if (dead.Count == 0 || group.Count == 0) return new Complete([]);
            if (group.Count > 1 && ctx.History.LastClosed(Question) is null) return new OpenAsks([Ask(ctx, unit, group)]);
            var heir = ctx.History.LastClosed(Question)?.Single is SelectedPieces { PieceIds: [var m] } && group.Contains(m) ? m : group[0];
            ctx.Applied.Add(CasterGroup);
            var tokens = dead.Sum(m => ctx.Projection.Models[m].SpellTokens);
            return new Complete([.. dead.Where(m => ctx.Projection.Models[m].SpellTokens > 0).Select(m => (StateChange)new SpellTokensSet(m, 0)),
                .. Designate(ctx.State, heir, heir, ctx.Projection.Models[heir].SpellTokens + tokens)]);
        }
    }
}
