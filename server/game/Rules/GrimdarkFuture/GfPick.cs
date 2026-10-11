using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book Buffs, Marks and Debuffs (army-book rules spec → the pick): "Once per activation, before attacking, pick one … unit within
/// 12"/18" …, which gets X once (next time the effect would apply)". A carrying model offers a use at before-attacking; the use picks a
/// unit by the spell-targeting reading measured from that model and writes one grant per once row, which the grant reading spends.
/// </summary>
public static class GfPick
{
    public static Sequence PickRule() => new(GfNames.PickRuleSeq, [new TargetStep()]);

    /// <summary>"&lt;entry&gt;|&lt;model&gt;": the react option's id and the pick-rule frame's Action, as GfCasting's pick ids name a caster.</summary>
    public static string Key(string entry, Guid model) => $"{entry}|{model:N}";

    /// <summary>The entry and carrying model of a key; split at the last '|', since an entry name may hold one. Null when it is no key.</summary>
    public static (string Entry, Guid Model)? Parse(string? key)
    {
        if (key is null) return null;
        var cut = key.LastIndexOf('|');
        return cut > 0 && Guid.TryParseExact(key.AsSpan(cut + 1), "N", out var model) ? (key[..cut], model) : null;
    }

    static bool IsPick(CatalogEntry e) => e is { Kind: CatalogKind.Model, Targeting: not null, Played: true };

    /// <summary>
    /// Each living model of the unit with each pick entry it carries that it has not used this activation and that has a target now. A model
    /// inside a container has no position to measure from, so it offers none.
    /// </summary>
    public static IReadOnlyList<(CatalogEntry Entry, Guid Model)> Offers(GameState state, PropertySet props, Guid unit) =>
        Lookup.LivingModels(state, unit)
            .SelectMany(m => state.Projection.Models[m].Slot.Properties.Select(p => props.Entry(p.Name)).OfType<CatalogEntry>().Where(IsPick)
                .DistinctBy(e => e.Name).Select(e => (Entry: e, Model: m)))
            .Where(x => Uses(state, unit, Key(x.Entry.Name, x.Model)) == 0 && GfCasting.Targets(state, x.Model, x.Entry.Targeting!).Count > 0)
            .ToList();

    /// <summary>The picks this key made since the unit's activation began (its last root action sequence, which only the scheduler starts):
    /// rule-target steps of its pick-rule frames that picked a target.</summary>
    public static int Uses(GameState state, Guid unit, string key)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && s.Context.ActorUnitId == unit);
        var frames = new HashSet<Guid>();
        var uses = 0;
        foreach (var e in log.Skip(Math.Max(0, since)))
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: GfNames.PickRuleSeq } s when s.Context.ActorUnitId == unit && s.Context.Action == key: frames.Add(s.FrameId); break;
                case StepCompleted { Step: GfNames.RuleTargetStep } c when frames.Contains(c.FrameId) && c.Values?.GetValueOrDefault("picked") == 1: uses++; break;
            }
        return uses;
    }

    /// <summary>Ask for the target; on an answer, grant every once row to it ("a once-row's conditions say when the grant applies, not whether
    /// it is written", as EffectsStep). A pass, or an answer naming nothing offered, uses nothing.</summary>
    sealed class TargetStep : IStep
    {
        public string Name => GfNames.RuleTargetStep;
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Parse(ctx.Frame.Context.Action) is not var (name, model) || ctx.Properties.Entry(name) is not { } entry || !IsPick(entry)
                || !ctx.Projection.Models.TryGetValue(model, out var carrier) || carrier.Location is not OnTable) return new Finish([]);
            if (ctx.History.LastClosed() is { } closed)
            {
                if (GfAnswers.Pieces(closed.Single, closed.Ask) is not [var target, ..]) return new Finish([]);
                var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
                // p.13: "Effects from multiple instances of the same special rule or spell don't stack".
                var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == entry.Name);
                IReadOnlyList<StateChange> grants = already ? []
                    : entry.Effects.Where(e => e.Duration == EffectDuration.Once).Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, entry.Name, unit, 1))).ToList();
                ctx.Applied.Add(entry.Name);
                return new Complete(grants, new Dictionary<string, int> { ["picked"] = 1 });
            }
            var eligible = GfCasting.Targets(ctx.State, model, entry.Targeting!);
            if (eligible.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask(GfNames.RuleTarget, $"{entry.Name}: pick a target", ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, 1), allowPass: true)]);
        }
    }
}
