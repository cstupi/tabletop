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

    /// <summary>Surprise Attack: "The first time this unit is activated, pick one enemy unit within 6" in line of sight, and roll X dice. For each 2+ it
    /// takes one hit with AP(1)." One target takes every success's hit (rule-hits under its Action, AP(1) in roll-to-block), and only in the unit's
    /// first activation.</summary>
    static bool OneTarget(CatalogEntry e) => e.Name == GfAmbush.SurpriseAttack;

    /// <summary>The unit's root action sequences in the game: one while its first activation runs.</summary>
    static int Activations(GameState state, Guid unit) =>
        EventApplier.EffectiveLog(state.Applier.Log).Count(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && s.Context.ActorUnitId == unit);

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
    public static IReadOnlyList<(CatalogEntry Entry, Guid Model)> Offers(GameState state, PropertySet props, Guid unit)
    {
        var offers = Lookup.LivingModels(state, unit)
            .SelectMany(m => Carried(state, props, m).Select(e => (Entry: e, Model: m)))
            .Where(x => Uses(state, unit, Key(x.Entry.Name, x.Model)) == 0 && (!OncePerGame(x.Entry) || GameUses(state, Key(x.Entry.Name, x.Model)) == 0)
                && (!OneTarget(x.Entry) || (Activations(state, unit) <= 1 && UnitUses(state, unit, x.Entry.Name) == 0))
                && GfCasting.Targets(state, x.Model, x.Entry.Targeting!, entry: x.Entry.Name).Count > 0)
            .ToList();
        // A one-target rule is the unit's: one use, by its first carrying model.
        return offers.Where((x, i) => !OneTarget(x.Entry) || offers.FindIndex(y => y.Entry.Name == x.Entry.Name) == i).ToList();
    }

    /// <summary>The uses this key made since the unit's activation began (its last root action sequence, which only the scheduler starts):
    /// pick-rule frames of it that rolled their dice or picked a target.</summary>
    public static int Uses(GameState state, Guid unit, string key)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && s.Context.ActorUnitId == unit);
        return Used(log.Skip(Math.Max(0, since)), a => a == key, unit);
    }

    /// <summary>The uses this key (one carrying model's entry) made in the whole game.</summary>
    public static int GameUses(GameState state, string key) => Used(EventApplier.EffectiveLog(state.Applier.Log), a => a == key, null);

    /// <summary>The uses any model of the unit made of the entry in the whole game.</summary>
    static int UnitUses(GameState state, Guid unit, string entry) =>
        Used(EventApplier.EffectiveLog(state.Applier.Log), a => Parse(a) is var (name, _) && name == entry, unit);

    static int Used(IEnumerable<GameEvent> log, Func<string?, bool> key, Guid? unit)
    {
        var frames = new HashSet<Guid>();
        var used = new HashSet<Guid>();
        foreach (var e in log)
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: GfNames.PickRuleSeq } s when (unit is null || s.Context.ActorUnitId == unit) && key(s.Context.Action): frames.Add(s.FrameId); break;
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
            if (Use(ctx) is not var (entry, model)) return new Finish([]);
            if (DiceRow(entry) is not { } row) return new Complete([]);
            // "Roll X dice": the carrier's X, bounded as every imported count is.
            var n = Math.Clamp(row.EffectiveAmount(Lookup.ModelParameter(ctx.State, model, entry.Name)), 0, GfRuleHits.MaxDealt);
            ctx.Roll(new Dice.RollPlan(entry.Name, row.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
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
            var count = OneTarget(entry) ? Math.Min(dice ?? 1, 1) : dice ?? 1;
            var index = ctx.Frame.Value("pick_index");
            if (count == 0) return new Finish([]);
            var x = Lookup.ModelParameter(ctx.State, model, entry.Name);
            if (ctx.History.LastClosed() is { } closed)
            {
                var targets = GfAnswers.Pieces(closed.Single, closed.Ask).Distinct().ToList();
                if (targets.Count == 0) return dice is null ? new Finish([]) : new Complete([]);
                if (!entry.Targeting!.CountFromParameter) targets = [targets[0]];
                var grants = new List<StateChange>();
                foreach (var target in targets)
                {
                    var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
                    // p.13: "Effects from multiple instances of the same special rule or spell don't stack".
                    var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == entry.Name);
                    // Markers stack: "place a marker on it" places one, "place X markers on it" the carrier's X; a pick of 1 to X units ("place X tokens on
                    // one enemy unit …, or one token on X enemy units") places X on one unit picked and one on each of several.
                    var markers = entry.Targeting.CountFromParameter ? targets.Count == 1 ? x ?? 1 : 1 : entry.TakesParameter ? x ?? 1 : 1;
                    grants.AddRange(GfMarkers.Rows(entry).Count > 0
                        ? GfMarkers.Place(entry, ctx.Projection.Units[targetUnit], unit, markers)
                        : already ? []
                        : entry.Effects.Where(e => e.Duration == EffectDuration.Once).Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, entry.Name, unit, 1))));
                    // A unit picked beyond the pick's range was reached through Extended Buff Range (GfCasting.Targets).
                    if (Battlefield.UnitDistance(ctx.State, unit, targetUnit) is { } apart && apart > entry.Targeting.Range) ctx.Applied.Add(GfCasting.ExtendedBuffRange);
                }
                ctx.Applied.Add(entry.Name);
                return new Complete(grants, new Dictionary<string, int> { ["picked"] = 1, ["pick_index"] = index + 1 }, Repeat: index + 1 < count);
            }
            var eligible = GfCasting.Targets(ctx.State, model, entry.Targeting!, entry: entry.Name);
            if (eligible.Count == 0) return dice is null ? new Finish([]) : new Complete([]);
            // countFromParameter: 1 to X units, `count` when the carrier has no X.
            var most = entry.Targeting!.CountFromParameter ? Math.Clamp(x ?? entry.Targeting.Count, 1, eligible.Count) : 1;
            var prompt = most > 1 ? $"{entry.Name}: pick up to {most} targets" : dice is null ? $"{entry.Name}: pick a target" : $"{entry.Name}: pick a target ({index + 1} of {count})";
            return new OpenAsks([ctx.Ask(GfNames.RuleTarget, prompt, ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, most), allowPass: true)]);
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
            var targets = PickedTargets(ctx.State, ctx.Frame);
            var index = ctx.Frame.Value("effect_index");
            if (index >= targets.Count || !entry.Effects.Any(e => e.Duration != EffectDuration.Once)) return new Complete([]);
            var target = targets[index];
            var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
            var values = new Dictionary<string, int> { ["effect_index"] = index + 1 };
            var more = index + 1 < targets.Count;
            if (ctx.History.Children.Count == 0)
            {
                if (OneTarget(entry)) return Successes(ctx.Frame, entry) is > 0 and var hits
                    ? new Push(GfNames.RuleHitsSeq, new SequenceContext(actor, [targetUnit], [], GfRuleHits.Key(entry.Name, hits))) : new Complete([], values);
                if (GfCasting.HitRows(entry).Count > 0) return new Push(GfNames.SpellHitsSeq, new SequenceContext(actor, [targetUnit], [], null));
                if (entry.Effects.Any(e => e.Kind == EffectKind.TakeTest)) return new Push(GfNames.SpellTestSeq, new SequenceContext(actor, [targetUnit], [], null));
            }
            var changes = new List<StateChange>();
            // "Which may immediately move by up to 9"": the picked model's unit moves under its own move rules, its player answering for it.
            if (entry.Effects.FirstOrDefault(e => e.Kind == EffectKind.Move && e.Duration != EffectDuration.Once) is { } move)
            {
                var inches = move.EffectiveAmount(null);
                if (ctx.History.LastClosed("move") is not { } moved)
                    return new OpenAsks([ctx.Ask("move", $"{entry.Name}: move up to {inches}\"", ctx.ActorTeam!.Value, GfMoves.ForcedMoveRegion(ctx, targetUnit, inches), allowPass: true)]);
                changes.AddRange(GfMoves.ApplyForcedMove(ctx, targetUnit, inches, GfAnswers.Moves(ctx.State, moved)).Changes);
                ctx.Applied.Add(entry.Name);
            }
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

    }

    /// <summary>Every target a pick-rule frame's rule-target runs picked, in order, each read against the Ask it answered (a repeat's own Ask).</summary>
    internal static IReadOnlyList<Guid> PickedTargets(GameState state, FrameView frame)
    {
        var asks = EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).OfType<AskOpened>()
            .Where(o => o.Ask.FrameId == frame.Id && o.Ask.Question == GfNames.RuleTarget).Select(o => o.Ask).ToList();
        var answers = frame.Frame.Steps.Where(s => s.Step == GfNames.RuleTargetStep && s.Answer is not null).Select(s => s.Answer).ToList();
        return answers.SelectMany((a, i) => GfAnswers.Pieces(a, i < asks.Count ? asks[i] : null).Distinct())
            .Where(t => state.Projection.Units.ContainsKey(t) || state.Projection.Models.ContainsKey(t)).ToList();
    }
}
