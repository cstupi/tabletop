using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Army-book Buffs, Marks, Debuffs and the before-attacking actives (army-book rules spec → the pick): "Once per activation, before attacking,
/// pick one … unit within 12"/18" …, which gets X once (next time the effect would apply)". A carrying model offers a use at before-attacking;
/// the use rolls the entry's dice when it has a roll row ("roll 3 dice. For each 2+ one enemy unit …"), picks a target per success (one without
/// dice) by the spell-targeting reading measured from that model, writes one grant per once row on each, and resolves its use rows on each:
/// hits (spell-hits), a morale test (spell-test), a heal.
/// </summary>
public static class GfPick
{
    public static Sequence PickRule() => new(GfNames.PickRuleSeq, [new RollStep(), new TargetStep(), new EffectsStep()]);

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

    /// <summary>"Once per game" on a pick entry: a usage_count row scoped to the game.</summary>
    static bool OncePerGame(CatalogEntry e) =>
        e.Effects.Any(f => f.Conditions.Any(c => c is { Kind: ConditionKind.UsageCount, Values: ["game", ..] }));

    /// <summary>The entry's dice row: "roll one die", "roll 3 dice"; its add_hit children are what each success deals.</summary>
    static Effect? DiceRow(CatalogEntry e) => e.Effects.FirstOrDefault(f => f.Kind == EffectKind.Roll);

    /// <summary>
    /// The pick entries a model carries: its slot's, and its unit's own properties, which an army imported before the army-book rules were
    /// generated holds a rule every model carried as (ArmyForgeImport puts a rule on every model of an entry on the unit when the catalog
    /// does not name it a model rule); import now puts it on every model's slot.
    /// </summary>
    static IEnumerable<CatalogEntry> Carried(GameState state, PropertySet props, Guid model) =>
        state.Projection.Models[model].Slot.Properties.Concat(state.Projection.Units[state.Projection.Models[model].UnitId].Definition.Properties)
            .Select(p => props.Entry(p.Name)).OfType<CatalogEntry>().Where(IsPick).DistinctBy(e => e.Name);

    /// <summary>
    /// Each living model of the unit with each pick entry it carries that it has not used this activation (nor this game, for a once-per-game
    /// entry) and that has a target now. A model inside a container has no position to measure from, so it offers none.
    /// </summary>
    public static IReadOnlyList<(CatalogEntry Entry, Guid Model)> Offers(GameState state, PropertySet props, Guid unit) =>
        Lookup.LivingModels(state, unit)
            .SelectMany(m => Carried(state, props, m).Select(e => (Entry: e, Model: m)))
            .Where(x => Uses(state, unit, Key(x.Entry.Name, x.Model)) == 0 && (!OncePerGame(x.Entry) || GameUses(state, Key(x.Entry.Name, x.Model)) == 0)
                && GfCasting.Targets(state, x.Model, x.Entry.Targeting!).Count > 0)
            .ToList();

    /// <summary>The uses this key made since the unit's activation began (its last root action sequence, which only the scheduler starts):
    /// pick-rule frames of it that rolled their dice or picked a target.</summary>
    public static int Uses(GameState state, Guid unit, string key)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && s.Context.ActorUnitId == unit);
        return Used(log.Skip(Math.Max(0, since)), key, unit);
    }

    /// <summary>The uses this key (one carrying model's entry) made in the whole game.</summary>
    public static int GameUses(GameState state, string key) => Used(EventApplier.EffectiveLog(state.Applier.Log), key, null);

    static int Used(IEnumerable<GameEvent> log, string key, Guid? unit)
    {
        var frames = new HashSet<Guid>();
        var used = new HashSet<Guid>();
        foreach (var e in log)
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: GfNames.PickRuleSeq } s when (unit is null || s.Context.ActorUnitId == unit) && s.Context.Action == key: frames.Add(s.FrameId); break;
                case StepCompleted c when frames.Contains(c.FrameId) && c.Values?.GetValueOrDefault("picked") == 1: used.Add(c.FrameId); break;
            }
        return used.Count;
    }

    /// <summary>The frame's entry and carrying model, when the entry is a played pick entry and the model stands on the table.</summary>
    static (CatalogEntry Entry, Guid Model)? Use(StepContext ctx)
    {
        if (Parse(ctx.Frame.Context.Action) is not var (name, model) || ctx.Properties.Entry(name) is not { } entry || !IsPick(entry)
            || !ctx.Projection.Models.TryGetValue(model, out var carrier) || carrier.Location is not OnTable) return null;
        return (entry, model);
    }

    /// <summary>Successes of the entry's dice as they now stand (an edit after rule-roll counts); null when the entry rolls none.</summary>
    static int? Successes(FrameView frame, CatalogEntry entry) =>
        DiceRow(entry) is null ? null : frame.Frame.Steps.Where(s => s.Step == GfNames.RuleRollStep).SelectMany(s => s.Rolls).Where(r => r.Roll == entry.Name).Sum(Dice.RollOutcome.Successes);

    /// <summary>"Roll one die. On a 2+ …", "roll 3 dice. For each 2+ …": the entry's dice under its own name at the row's threshold. Rolling is the use.</summary>
    sealed class RollStep : IStep
    {
        public string Name => GfNames.RuleRollStep;
        public StepOutcome Run(StepContext ctx)
        {
            if (Use(ctx) is not var (entry, _)) return new Finish([]);
            if (DiceRow(entry) is not { } row) return new Complete([]);
            ctx.Roll(new Dice.RollPlan(entry.Name, row.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), row.EffectiveAmount(null));
            ctx.Applied.Add(entry.Name);
            return new Complete([], new Dictionary<string, int> { ["picked"] = 1 });
        }
    }

    /// <summary>Ask for a target, once per success of the dice (once without dice); on an answer grant every once row to it ("a once-row's conditions
    /// say when the grant applies, not whether it is written", as EffectsStep). A pass, or an answer naming nothing offered, picks nothing more;
    /// without dice it uses nothing.</summary>
    sealed class TargetStep : IStep
    {
        public string Name => GfNames.RuleTargetStep;
        public StepOutcome Run(StepContext ctx)
        {
            if (Use(ctx) is not var (entry, model)) return new Finish([]);
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var dice = Successes(ctx.Frame, entry);
            var count = dice ?? 1;
            var index = ctx.Frame.Value("pick_index");
            if (count == 0) return new Finish([]);
            if (ctx.History.LastClosed() is { } closed)
            {
                if (GfAnswers.Pieces(closed.Single, closed.Ask) is not [var target, ..]) return dice is null ? new Finish([]) : new Complete([]);
                var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
                // p.13: "Effects from multiple instances of the same special rule or spell don't stack".
                var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == entry.Name);
                IReadOnlyList<StateChange> grants = already ? []
                    : entry.Effects.Where(e => e.Duration == EffectDuration.Once).Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, entry.Name, unit, 1))).ToList();
                ctx.Applied.Add(entry.Name);
                return new Complete(grants, new Dictionary<string, int> { ["picked"] = 1, ["pick_index"] = index + 1 }, Repeat: index + 1 < count);
            }
            var eligible = GfCasting.Targets(ctx.State, model, entry.Targeting!);
            if (eligible.Count == 0) return dice is null ? new Finish([]) : new Complete([]);
            var prompt = dice is null ? $"{entry.Name}: pick a target" : $"{entry.Name}: pick a target ({index + 1} of {count})";
            return new OpenAsks([ctx.Ask(GfNames.RuleTarget, prompt, ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, 1), allowPass: true)]);
        }
    }

    /// <summary>The use rows on each picked target in turn (Repeat): hits push spell-hits, a test pushes spell-test, a heal removes wounds.</summary>
    sealed class EffectsStep : IStep
    {
        public string Name => GfNames.RuleEffectsStep;
        public StepOutcome Run(StepContext ctx)
        {
            if (Use(ctx) is not var (entry, _)) return new Complete([]);
            var actor = ctx.Frame.Context.ActorUnitId!.Value;
            var targets = Picked(ctx);
            var index = ctx.Frame.Value("effect_index");
            if (index >= targets.Count || !entry.Effects.Any(e => e.Duration != EffectDuration.Once)) return new Complete([]);
            var target = targets[index];
            var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
            var values = new Dictionary<string, int> { ["effect_index"] = index + 1 };
            var more = index + 1 < targets.Count;
            if (ctx.History.Children.Count == 0)
            {
                if (GfCasting.HitRows(entry).Count > 0) return new Push(GfNames.SpellHitsSeq, new SequenceContext(actor, [targetUnit], [], null));
                if (entry.Effects.Any(e => e.Kind == EffectKind.TakeTest)) return new Push(GfNames.SpellTestSeq, new SequenceContext(actor, [targetUnit], [], null));
            }
            var changes = new List<StateChange>();
            if (entry.Effects.FirstOrDefault(e => e.Kind == EffectKind.RemoveWounds) is { } heal && ctx.Projection.Models.TryGetValue(target, out var healed))
            {
                // p.4: "D3 … roll a D6 and halve the result, rounding up".
                var n = heal.AmountDice is null ? heal.EffectiveAmount(null)
                    : ctx.Roll(new Dice.RollPlan(entry.Name, null, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), heal.EffectiveAmount(null)).Dice.Sum(d => (d.Raw + 1) / 2);
                if (healed.Wounds > 0) changes.Add(new WoundsSet(target, Math.Max(0, healed.Wounds - n)));
                ctx.Applied.Add(entry.Name);
            }
            return new Complete(changes, values, Repeat: more);
        }

        /// <summary>Every target the rule-target runs picked, in order, each read against the Ask it answered (a repeat's own Ask).</summary>
        static IReadOnlyList<Guid> Picked(StepContext ctx)
        {
            var asks = EventApplier.EffectiveLog(ctx.State.Applier.Log).Select(e => e.Payload).OfType<AskOpened>()
                .Where(o => o.Ask.FrameId == ctx.Frame.Id && o.Ask.Question == GfNames.RuleTarget).Select(o => o.Ask).ToList();
            var answers = ctx.Frame.Frame.Steps.Where(s => s.Step == GfNames.RuleTargetStep && s.Answer is not null).Select(s => s.Answer).ToList();
            return answers.Select((a, i) => GfAnswers.Pieces(a, i < asks.Count ? asks[i] : null)).Where(p => p.Count > 0).Select(p => p[0])
                .Where(t => ctx.Projection.Units.ContainsKey(t) || ctx.Projection.Models.ContainsKey(t)).ToList();
        }
    }
}
