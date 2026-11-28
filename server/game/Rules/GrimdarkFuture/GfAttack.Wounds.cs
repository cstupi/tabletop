using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>
    /// The wounds the parent dealt, read off its dice as they now stand (an edit between steps counts): the failures of its defense rolls and
    /// the wounds each defense die's face adds (`extra_wounds_&lt;roll&gt;_&lt;face&gt;`, which roll-to-block maps for every face), or the failures of
    /// a dangerous terrain test; else the parent's declared wounds.
    /// </summary>
    public static int WoundsToDeal(FrameView frame)
    {
        var parent = frame.Parent ?? throw new InvalidOperationException("deal-wounds needs a parent frame");
        var defense = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        if (defense.Count > 0) return defense.Select((r, i) => Dice.RollOutcome.Failures(r) + r.Dice.Sum(d => parent.Value(ExtraWoundsKey(i, d.Raw)))).Sum();
        if (parent.Frame.Sequence is GfNames.DangerousTerrain or GfNames.DangerousTerrain36) return GfMoves.DangerousWounds(parent);
        return parent.Value("wounds");
    }

    /// <summary>
    /// The model a Takedown group named for its wound (p.14). Values holds ints only and SequenceContext has no model, so the choice is
    /// read back off the nearest ancestor attack-sequence frame, the way HitsFrom reads the parent's rolls. Null for every other group.
    /// </summary>
    public static Guid? TakedownModel(FrameView frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
        {
            if (f.Frame.Sequence == GfNames.AttackSequence)
                return f.Frame.Steps.FirstOrDefault(s => s.Step == "pick-target-model")?.Answer is SelectedPieces { PieceIds.Count: 1 } p ? p.PieceIds[0] : null;
            // A model-grain spell is "resolved as if the target was a unit of [1]" (p.14's Takedown wording): its current target, by the
            // effects step's target_index. A unit id here is not one of the target's models, and the assign step's check discards it.
            if (f.Frame.Sequence == GfNames.CastingSeq)
                return f.Frame.Steps.FirstOrDefault(s => s.Step == "targets" && s.Answer is SelectedPieces)?.Answer is SelectedPieces t && f.Value("target_index") < t.PieceIds.Count
                    ? t.PieceIds[f.Value("target_index")] : null;
        }
        return null;
    }

    /// <summary>The `granted_` keys of the nearest attack-sequence or spell-hits frame above this one: the per-die rules the group's hits carry,
    /// with how many hits carry each (a hit carrying two is counted under both).</summary>
    public static IReadOnlyList<(string Entry, int Amount, int Count)> GrantedOf(FrameView frame)
    {
        for (var f = frame.Parent; f is not null; f = f.Parent)
            if (f.Frame.Sequence is GfNames.AttackSequence or GfNames.SpellHitsSeq)
                return f.Values.Where(kv => kv.Key.StartsWith("granted_")).SelectMany(kv => ParseGrantSet(kv.Key).Select(g => (g.Entry, g.Amount, kv.Value))).ToList();
        return [];
    }

    /// <summary>Wounds have reached the model's Tough (1 without it). A dead model is removed at once, so on the table this is only a destroyed container held for disgorge (Task 19).</summary>
    public static bool AtCapacity(GameState state, Guid modelId) =>
        state.Projection.Models[modelId].Wounds >= (Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1);

    /// <summary>
    /// p.15, in the order the rule writes them: heroes are assigned wounds last even if already wounded; a Tough model that joined a unit
    /// without it "is removed last", so while models without Tough remain they are the pool; among Tough models alone the most-wounded one
    /// keeps taking wounds until it is killed. The in-play reading, so a unit inside a wrecked transport can be wounded before it is placed (Task 19).
    /// </summary>
    public static IReadOnlyList<Guid> EligibleForNextWound(GameState state, PropertySet props, Guid unit) =>
        EligibleForNextWound(state, unit, m => state.Projection.Models[m].Wounds);

    /// <summary>The models wounds would kill, laid one at a time on a copy of the unit's wounds in p.15's order (EligibleForNextWound, the first
    /// eligible model when several are): what a unit counts as having lost to wounds it only counts as having taken.</summary>
    public static int LaidCasualties(GameState state, PropertySet props, Guid unit, int wounds)
    {
        var laid = Lookup.AliveModels(state, unit).ToDictionary(m => m, m => state.Projection.Models[m].Wounds);
        var killed = 0;
        for (var i = 0; i < wounds; i++)
        {
            if (EligibleForNextWound(state, unit, m => laid.GetValueOrDefault(m)) is not [var next, ..]) break;
            if (++laid[next] >= Size(state, next)) killed++;
        }
        return killed;
    }

    static int Size(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1;

    static IReadOnlyList<Guid> EligibleForNextWound(GameState state, Guid unit, Func<Guid, int> wounds)
    {
        // A destroyed container still on the table (Task 19) has taken all it can. Among Tough models the most-wounded is offered first,
        // so without this a carried wound would be handed to the wreck, which cannot take it.
        var living = Lookup.AliveModels(state, unit).Where(m => wounds(m) < Size(state, m)).ToList();
        // Heroes are deferred before the Tough ordering is read, not after it. p.15 writes "heroes must be assigned wounds last,
        // even if already wounded" as a parenthetical inside the continuation clause, so it is an exception to that clause: a
        // wounded Tough hero waits for every other model rather than soaking the next wound.
        var notHeroes = living.Where(m => !state.Projection.Models[m].Slot.Properties.Any(p => GfNames.IsHero(p.Name))).ToList();
        var pool = notHeroes.Count > 0 ? notHeroes : living;
        var plain = pool.Where(m => !IsTough(state, m)).ToList();
        if (plain.Count > 0) return plain;
        var woundedTough = pool.Where(m => wounds(m) > 0).OrderByDescending(wounds).ToList();
        return woundedTough.Count > 0 ? [woundedTough[0]] : pool;
    }

    static bool IsTough(GameState state, Guid modelId) => (Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1) > 1;

    /// <summary>p.15's "heroes must be assigned wounds last": a living hero and a living model that is not one, so the hero was held back from this wound.</summary>
    static bool HeroDeferred(GameState state, Guid unit)
    {
        var living = Lookup.AliveModels(state, unit).Where(m => !AtCapacity(state, m)).ToList();
        bool IsHero(Guid m) => state.Projection.Models[m].Slot.Properties.Any(p => GfNames.IsHero(p.Name));
        return living.Any(IsHero) && living.Any(m => !IsHero(m));
    }

    /// <summary>
    /// One segment per run, repeating (p.8: one model removed for each wound, so ordinary wounds carry over past a dead model): the
    /// wounds left come from the parent's failed defense rolls on the first run and from the previous segment's "wounds_left" after.
    /// Deadly's multiplied wounds and a Takedown's (a unit of one, p.14) do not carry over, so only their excess is lost. A carried wound
    /// has already taken its wound-ignoring roll (Regeneration p.14, Resistance, Self-Repair, Plaguebound: "roll one die for each"), so
    /// "wounds_carried" counts the wounds left that keep their result and roll no die again. With no model left to take a wound (an earlier
    /// weapon group destroyed the unit, p.8 "all weapons are fired simultaneously") the wounds are lost and nothing is asked.
    /// A segment that rolls to ignore wounds takes two runs: the first rolls each model's dice and records the segment (`ignore_wounds` and
    /// `allocated_&lt;i&gt;_&lt;model&gt;`), the second reads those dice as they then stand and applies the wounds, so an edit between the two counts.
    /// </summary>
    sealed class AssignStep : IStep
    {
        public string Name => "assign";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            if (ctx.Frame.Frame.Steps is [.., { Step: "assign", Values: { } rolled } last] && rolled.TryGetValue(IgnoreWoundsKey, out var pending))
                return Apply(ctx, target, pending, RecordedAllocation(rolled), TakedownModel(ctx.Frame) is { } t && Lookup.AliveModels(ctx.State, target).Contains(t) || SelfWoundModels(ctx, target) is not null, last.Rolls);
            var wounds = ctx.Frame.Values.ContainsKey("wounds_left") ? ctx.Frame.Value("wounds_left") : WoundsToDeal(ctx.Frame);
            if (wounds <= 0) return new Finish([]);
            // Protection Feat: "Once per game, when this unit takes wounds …, you may use this rule and roll one die per wound": until it is used its team is
            // asked once per deal-wounds frame on the unit; the answer rides the frame as use_<entry> for every segment of it (WoundEffects).
            if (Lookup.AliveModels(ctx.State, target).Count > 0 && OnceUnused(ctx, target) is { } once && !ctx.Frame.Values.ContainsKey(UseKey(once)))
            {
                if (ctx.History.LastClosed(UseRule) is not { } use)
                    return new OpenAsks([ctx.Ask(UseRule, $"Use {once} on {wounds} {(wounds == 1 ? "wound" : "wounds")}?", ctx.TeamOf(target)!.Value,
                        new Options([new AskOption("yes", "Yes", [target]), new AskOption("no", "No", [])]), allowPass: true)]);
                return new Complete([], new Dictionary<string, int> { [UseKey(once)] = use.Single is Chosen { OptionId: "yes" } ? 1 : 0 }, Repeat: true);
            }
            // 3.6 Hazardous: "this model takes one wound": each self wound on the model that rolled it, nothing asked, nothing carried over.
            if (SelfWoundModels(ctx, target) is { } own) return Apply(ctx, target, wounds, own, fixedModels: true, null);
            var team = ctx.TeamOf(target) ?? throw new InvalidOperationException("target unit has no team");
            // A Takedown group named its model, so nothing is asked and no ordering is consulted: p.15's Tough-first and heroes-last
            // decide the prompt's targets, and there is no prompt (p.14).
            var takedown = TakedownModel(ctx.Frame) is { } named && Lookup.AliveModels(ctx.State, target).Contains(named) ? named : (Guid?)null;
            var eligible = takedown is null ? EligibleForNextWound(ctx.State, ctx.Properties, target) : [];
            if (takedown is null && eligible.Count == 0) return new Finish([]);
            if (takedown is not null) ctx.Applied.Add(GfNames.Takedown);
            else if (HeroDeferred(ctx.State, target)) ctx.Applied.Add(GfNames.Hero);
            // p.15: "continue to put wounds on the tough model with most wounds in the unit until it is killed, before starting to put them on
            // the next tough model". Offered only Tough models (none wounded), the defender places every wound in one answer, read as an order
            // (FillInTurn): each model it names takes wounds until it is killed before the next takes any, so a spread still keeps p.15.
            var toughOnly = eligible.All(m => IsTough(ctx.State, m));
            Dictionary<Guid, int> allocation;
            if (takedown is { } one) allocation = new Dictionary<Guid, int> { [one] = wounds };
            else if (eligible.Count == 1) allocation = new Dictionary<Guid, int> { [eligible[0]] = wounds };   // nothing to choose
            else if (ctx.History.LastClosed() is { Single: Allocated } closed)
            {
                allocation = GfAnswers.Allocated(closed, Lookup.AliveModels(ctx.State, target), wounds);
                if (toughOnly && allocation.Count > 0) allocation = FillInTurn(ctx.State, [.. allocation.Keys], wounds);
            }
            else if (KillsEveryModel(ctx, target, wounds) is { } forced) allocation = forced;   // no choice changes the outcome
            else return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(eligible, wounds))]);
            return Apply(ctx, target, wounds, allocation.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)).ToList(), takedown is not null, null);
        }

        /// <summary>p.15 over the defender's answer: the models in the order it names them, each taking the wounds it has room for before the next
        /// takes any, the rest on the last model named for Apply to carry over (p.8), as an answer naming one model always did.</summary>
        static Dictionary<Guid, int> FillInTurn(GameState state, IReadOnlyList<Guid> order, int wounds)
        {
            var filled = new Dictionary<Guid, int>(); var left = wounds;
            foreach (var m in order)
            {
                if (left <= 0) break;
                var take = Math.Min(left, Math.Max(0, Size(state, m) - state.Projection.Models[m].Wounds));
                if (take > 0) { filled[m] = take; left -= take; }
            }
            if (left > 0) { var last = filled.Count > 0 ? filled.Keys.Last() : order[0]; filled[last] = filled.GetValueOrDefault(last) + left; }
            return filled;
        }

        const string IgnoreWoundsKey = "ignore_wounds";

        /// <summary>The wounds the frame started with (its first segment's), on every segment: what a later segment's Ask, asking for the
        /// wounds left, is a part of (the clients' `1 of 2 wounds assigned`).</summary>
        const string WoundsTotalKey = "wounds_total";
        static int WoundsTotal(StepContext ctx, int wounds) => ctx.Frame.Values.ContainsKey(WoundsTotalKey) ? ctx.Frame.Value(WoundsTotalKey) : wounds;

        /// <summary>
        /// Owner ruling (2026-11-09): a unit whose every model the wounds kill is not asked to allocate them. p.8 "For each wound that the
        /// unit has taken, the defending player must remove one model", p.15 Tough "must take X wounds before being killed": when the wounds
        /// reach the remaining wounds of every living model (Deadly(X)'s multiplied wounds counted per model, one per X of room) every allocation
        /// removes the whole unit. A rule that may ignore a wound after allocation (Regeneration and its kind, rolled per allocated model in Apply)
        /// can leave a model standing, so its unit is still asked. The allocation lays the wounds one at a time in p.15's order
        /// (EligibleForNextWound), each model taking what kills it: heroes last, Split's kept model the last one laid.
        /// </summary>
        static Dictionary<Guid, int>? KillsEveryModel(StepContext ctx, Guid target, int wounds)
        {
            var (multiplier, regen) = WoundEffects(ctx, target, LiveProperties.Settle(WoundInteraction(ctx, target)));
            if (regen.Roll is not null) return null;
            var laid = Lookup.AliveModels(ctx.State, target).ToDictionary(m => m, m => ctx.Projection.Models[m].Wounds);
            var allocation = new List<(Guid Model, int Count)>();
            for (var i = 0; i < wounds; i++)
            {
                if (EligibleForNextWound(ctx.State, target, m => laid.GetValueOrDefault(m)) is not [var next, ..]) break;
                laid[next] = Math.Min(Size(ctx.State, next), laid[next] + multiplier);
                var at = allocation.FindIndex(a => a.Model == next);
                if (at < 0) allocation.Add((next, 1)); else allocation[at] = (next, allocation[at].Count + 1);
            }
            if (EligibleForNextWound(ctx.State, target, m => laid.GetValueOrDefault(m)).Count > 0) return null;
            var ordered = new Dictionary<Guid, int>();
            foreach (var (model, count) in allocation) ordered[model] = count;
            return ordered;
        }

        static Interaction WoundInteraction(StepContext ctx, Guid target)
        {
            var actor = ctx.Frame.Context.ActorUnitId;
            return AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, actor is null ? null : KindOf(ctx), ctx.Frame.Context.Action);
        }

        /// <summary>Deadly's wound multiplier and the rule that rolls to ignore the target's wounds, if any, off a settled live set.</summary>
        static (int Multiplier, (LiveProperty Source, Effect Roll) Regen) WoundEffects(StepContext ctx, Guid target, LiveSet live)
        {
            var actor = ctx.Frame.Context.ActorUnitId;
            var withGranted = GrantedOf(ctx.Frame).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(withGranted, Side.Attacker);
            // Regeneration is aggregation all under 3.5.1 ("a unit where all models have this rule") and majority under 3.6.0 (3.6.0 p.14: "most models"), so LiveProperties.Gather settles it
            // as one unit-level property with no ModelId, and it applies to every wound the unit takes. p.13: instances of a rule
            // without (X) do not stack, so one roll is taken however many are live, a granted instance included. Threshold is the row's.
            // A once-per-game roll (Protection Feat: "Once per game, when this unit takes wounds … you may use this rule and roll one die per wound") rolls for
            // every wound of the frame its team chose to use it in (use_<entry>), and in no later frame.
            // No Retreat's wounds "can't be ignored": no roll is made for them.
            var regen = IgnoreRolls(ctx, live, (source, roll) => !OncePerGame(roll) || (!RolledInAnotherFrame(ctx, source.Entry.Name, target) && ctx.Frame.Value(UseKey(source.Entry.Name)) == 1))
                .FirstOrDefault();
            return (multiplier, regen);
        }

        static IReadOnlyList<(LiveProperty Source, Effect Roll)> IgnoreRolls(StepContext ctx, LiveSet live, Func<LiveProperty, Effect, bool> keep) =>
            ctx.Frame.Context.Action == GfNames.NoRetreat ? []
                : RollEffects.OwnRolls(live, ctx.Frame.Context.ActorUnitId is null ? Side.Attacker : Side.Defender, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }, keep);

        /// <summary>The once-per-game roll to ignore wounds the unit holds and has not rolled in another deal-wounds frame: the rule its team is asked to use.</summary>
        static string? OnceUnused(StepContext ctx, Guid target) =>
            IgnoreRolls(ctx, LiveProperties.Settle(WoundInteraction(ctx, target)), (source, roll) => OncePerGame(roll) && !RolledInAnotherFrame(ctx, source.Entry.Name, target))
                .Select(x => x.Source.Entry.Name).FirstOrDefault();

        static string UseKey(string entry) => $"use_{entry}";

        /// <summary>The allocation a rolling run recorded, in its order.</summary>
        static IReadOnlyList<(Guid Model, int Count)> RecordedAllocation(IReadOnlyDictionary<string, int> values) =>
            values.Where(kv => kv.Key.StartsWith("allocated_")).Select(kv => (Parts: kv.Key["allocated_".Length..].Split('_'), kv.Value))
                .OrderBy(x => int.Parse(x.Parts[0])).Select(x => (Guid.ParseExact(x.Parts[1], "N"), x.Value)).ToList();

        /// <summary>The segment's wounds on the allocated models. `ignoreRolls` null: roll to ignore wounds when a rule says so, and stop there with the
        /// segment recorded; otherwise the rolling run's dice, one record per model that rolled, in the allocation's order, read as they now stand.</summary>
        static StepOutcome Apply(StepContext ctx, Guid target, int wounds, IReadOnlyList<(Guid Model, int Count)> allocation, bool fixedModels, IReadOnlyList<RollRecord>? ignoreRolls)
        {
            var ix = WoundInteraction(ctx, target);
            // The rolling run lists what acted on the segment; the run that reads its dice settles the same rules again only to spend them.
            var live = ignoreRolls is null ? ctx.Settle(ix) : LiveProperties.Settle(ix);
            var (multiplier, regen) = WoundEffects(ctx, target, live);
            // Deadly(X): "Assign each wound to one model, and multiply it by X ... these wounds don't carry over to other models if the original
            // target is killed" (3.5.1 p.13, 3.6.0 p.14). A model keeps only the wounds it needs to die; each wound past those is a wound of its own,
            // assigned to no removed model (p.8: one model for each wound), so it goes to the next segment,
            // where it is multiplied by X on the model it is assigned to; its excess there is lost too.
            if (multiplier > 1 && !fixedModels)
                allocation = allocation.Select(a =>
                {
                    var room = Size(ctx.State, a.Model) - ctx.Projection.Models[a.Model].Wounds;
                    return (a.Model, Count: Math.Min(a.Count, Math.Max(1, (room + multiplier - 1) / multiplier)));
                }).ToList();

            // Carried wounds come first and keep the result they rolled; each of a model's other wounds rolls one die.
            var carried = ctx.Frame.Value("wounds_carried");
            var segment = allocation.Select(a => { var incoming = a.Count * multiplier; var kept = Math.Min(carried, incoming); carried -= kept; return (a.Model, Incoming: incoming, Kept: kept); }).ToList();
            if (ignoreRolls is null && regen.Roll is { } roll && segment.Any(m => m.Incoming > m.Kept))
            {
                foreach (var m in segment.Where(m => m.Incoming > m.Kept))
                    ctx.Roll(new Dice.RollPlan(regen.Source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), m.Incoming - m.Kept);
                var recorded = new Dictionary<string, int> { [IgnoreWoundsKey] = wounds, [WoundsTotalKey] = WoundsTotal(ctx, wounds) };
                foreach (var (a, i) in allocation.Select((a, i) => (a, i))) recorded[$"allocated_{i}_{a.Model:N}"] = a.Count;
                return new Complete([], recorded, Repeat: true);
            }

            var changes = new List<StateChange>(); var landed = 0; var spilled = 0; var moves = new List<ModelRelocation>(); var dead = new HashSet<Guid>();
            var perModel = new Dictionary<string, int>();
            var next = 0;
            foreach (var (modelId, rolledIncoming, kept) in segment)
            {
                var model = ctx.Projection.Models[modelId];
                var capacity = Lookup.ModelParameter(ctx.State, modelId, "Tough") ?? 1;
                if (capacity > 1) ctx.Applied.Add(GfNames.Tough);
                var incoming = rolledIncoming;
                if (ignoreRolls is not null && incoming > kept)
                {
                    var record = ignoreRolls[next++];
                    incoming = kept + Dice.RollOutcome.Failures(record);   // each success overrides one wound
                    // The wounds this model ignored in this step (Regenerative Strength: "Place one marker on this model when it ignores a wound").
                    if (Dice.RollOutcome.Successes(record) is > 0 and var ignored) perModel[$"ignored_{modelId:N}"] = ignored;
                }
                var applied = Math.Min(capacity - model.Wounds, incoming);
                if (multiplier == 1) spilled += incoming - applied;   // an ordinary wound past a dead model is removed from the next one (p.8)
                landed += applied;
                changes.Add(new WoundsSet(modelId, model.Wounds + applied));
                // Each model's wounds and death in this frame, for what its rules set off (retaliation: Retaliate, Deathstrike, Self-Destruct).
                if (applied > 0) perModel[$"took_{modelId:N}"] = ctx.Frame.Value($"took_{modelId:N}") + applied;
                // p.15: a container holding anybody stays on the table until disgorge has placed them, "before it's removed".
                if (model.Wounds + applied < capacity) continue;
                perModel[$"killed_{modelId:N}"] = 1;
                dead.Add(modelId);
                if (!(GfMoves.IsContainer(ctx.State, modelId) && Lookup.OccupantUnits(ctx.State, modelId).Count > 0)) moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
            }
            // Split: "you may place a new unit of X fully within 6" of it before removing the last model": the last model stays for `split`, which removes it.
            if (moves.Count > 0 && !Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m) && !AtCapacity(ctx.State, m)) && GfReserves.KeepsLastModel(ctx.State, target))
                moves.RemoveAt(moves.Count - 1);
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            changes.AddRange(ctx.Consume(live));   // whatever the step read is spent: a granted regeneration rolled, a granted wound multiplier applied
            var total = ctx.Frame.Value("wounds_dealt") + landed;
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            var left = !fixedModels ? wounds - allocation.Sum(a => a.Count) + spilled : 0;
            var survivors = Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m) && !AtCapacity(ctx.State, m));
            var values = new Dictionary<string, int>(perModel) { ["wounds_dealt"] = total, ["wounds_left"] = left, ["wounds_carried"] = !fixedModels ? spilled : 0, [WoundsTotalKey] = WoundsTotal(ctx, wounds) };
            if (multiplier > 1) values["wound_multiplier"] = multiplier;   // Deadly's ×X on each wound of the segment: what the combat log multiplies its wounds by
            return new Complete(changes, values, Repeat: left > 0 && survivors);
        }
    }

    /// <summary>The Ask that offers a once-per-game rule ("you may use this rule"): Yes or No, a pass is No.</summary>
    public const string UseRule = "use_rule";

    static bool OncePerGame(Effect roll) => roll.Conditions.Any(c => c is { Kind: ConditionKind.UsageCount, Values: ["game", ..] });

    /// <summary>Whether a deal-wounds frame other than this one already rolled the rule's dice for wounds on this unit.</summary>
    static bool RolledInAnotherFrame(StepContext ctx, string entry, Guid unit)
    {
        var frames = new HashSet<Guid>();
        foreach (var p in EventApplier.EffectiveLog(ctx.State.Applier.Log).Select(e => e.Payload))
            switch (p)
            {
                case SequenceStarted { Sequence: GfNames.DealWounds } s when s.FrameId != ctx.Frame.Id && s.Context.TargetUnitIds is [var t, ..] && t == unit: frames.Add(s.FrameId); break;
                case StepCompleted { Step: "assign" } c when frames.Contains(c.FrameId) && c.Rolls.Any(r => r.Roll == entry): return true;
            }
        return false;
    }

    /// <summary>
    /// p.15: a destroyed container is not removed until the units inside are out. `assign` left it on the table; this runs
    /// transport-destroyed once per occupant unit (each takes its dangerous terrain test, is Shaken and is placed), then shelves every
    /// wreck. Nothing to do is the common case and costs one predicate.
    /// </summary>
    sealed class DisgorgeStep : IStep
    {
        public string Name => "disgorge";
        public StepOutcome Run(StepContext ctx)
        {
            // The occupants' own dangerous terrain test pushes a deal-wounds of its own; that one must not disgorge the wreck the frame
            // above it is already emptying. The guard walks this frame's ancestors, not the stack, so the outer disgorge is unaffected
            // while its child runs. A container wrecked by that nested test is still caught: the outer disgorge re-reads Wrecks after every
            // child and only shelves when no wreck holds anybody.
            for (var f = ctx.Frame.Parent; f is not null; f = f.Parent)
                if (f.Frame.Sequence == GfNames.TransportDestroyedSeq) return new Complete([]);
            var wrecks = GfContainers.Wrecks(ctx.State);
            if (wrecks.Count == 0) return new Complete([]);
            foreach (var wreck in wrecks)
                if (Lookup.OccupantUnits(ctx.State, wreck) is [var occupant, ..])
                    return new Push(GfNames.TransportDestroyedSeq, new SequenceContext(occupant, [], [], null));
            return new Complete([new ModelsMoved(wrecks.Select(w => new ModelRelocation(w, ctx.Projection.Models[w].Location, new OnShelf())).ToList())]);
        }
    }

    /// <summary>The grants this step used (spell library → consumption): every granted source it read, with the rest of its spell's grants on that unit.</summary>
}
