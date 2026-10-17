using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep(), new DisgorgeStep(), new GfSpellSupport.TransferStep(), new GfMarkers.DestroyedStep(), new GfRuleHits.RetaliationStep()]);

    public static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IReadOnlyList<Guid> weapons, AttackKind? kind, string? action) =>
        AttackInteraction(ctx.State, ctx.Properties, ctx.Frame, actor, target, weapons, kind, action,
            actor is { } a && kind == AttackKind.Spell && weapons.Count == 0 ? GfCasting.WeaponRules(ctx, a, target) : null);

    /// <summary>
    /// The attack's interaction from state, properties and, inside a running sequence, its frame: the frame gives a charge's recorded range and the
    /// nearest shooting or melee frame's faces. With no frame (a question asked outside Advance, such as who can shoot) the range is the units'
    /// distance and there are no faces. `spellRules` is the spell's weapon rules, which only a step context can read.
    /// </summary>
    public static Interaction AttackInteraction(GameState state, PropertySet properties, FrameView? frame, Guid? actor, Guid target, IReadOnlyList<Guid> weapons,
        AttackKind? kind, string? action, IReadOnlyList<(string Entry, int? Parameter)>? spellRules = null)
    {
        if (actor is not { } a) return new Interaction(state, properties, Party.Whole(state, target), null, kind, action, null, Battlefield.TerrainUnder(state, target));
        var models = weapons.Count > 0 ? state.Projection.Models.Values.Where(m => m.Slot.Weapons.Any(w => weapons.Contains(w.Id))).Select(m => m.Id).ToList() : Lookup.LivingModels(state, a);
        return new Interaction(state, properties, Party.Of(state, a, models, weapons), Party.Whole(state, target), kind, action, ChargeRange(frame) ?? Battlefield.UnitDistance(state, a, target), Battlefield.TerrainUnder(state, target),
            Faces: FacesFor(frame, a, target), SpellRules: spellRules);
    }

    /// <summary>"Charges enemies over 9" away", "charged from over 9" away": in the melee of a Charge, the distance the charge-target step recorded
    /// before the move (spell library, Grammar additions). Null outside a charge.</summary>
    static double? ChargeRange(FrameView? frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.ChargeSeq && f.Values.TryGetValue(GfNames.ChargeRangeKey, out var hundredths)) return hundredths / 100.0;
        return null;
    }

    /// <summary>Failures of the parent's defense rolls as they now stand (a reroll between steps counts), or the parent's declared wounds.</summary>
    public static int WoundsToDeal(FrameView frame)
    {
        var parent = frame.Parent ?? throw new InvalidOperationException("deal-wounds needs a parent frame");
        var defense = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        return defense.Count > 0 ? defense.Sum(Dice.RollOutcome.Failures) + parent.Value("extra_wounds") : parent.Value("wounds");
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
    public static IReadOnlyList<Guid> EligibleForNextWound(GameState state, PropertySet props, Guid unit)
    {
        // A destroyed container still on the table (Task 19) has taken all it can. Among Tough models the most-wounded is offered first,
        // so without this a carried wound would be handed to the wreck, which cannot take it.
        var living = Lookup.AliveModels(state, unit).Where(m => !AtCapacity(state, m)).ToList();
        // Heroes are deferred before the Tough ordering is read, not after it. p.15 writes "heroes must be assigned wounds last,
        // even if already wounded" as a parenthetical inside the continuation clause, so it is an exception to that clause: a
        // wounded Tough hero waits for every other model rather than soaking the next wound.
        var notHeroes = living.Where(m => !state.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero)).ToList();
        var pool = notHeroes.Count > 0 ? notHeroes : living;
        var plain = pool.Where(m => !IsTough(state, m)).ToList();
        if (plain.Count > 0) return plain;
        var woundedTough = pool.Where(m => state.Projection.Models[m].Wounds > 0).OrderByDescending(m => state.Projection.Models[m].Wounds).ToList();
        return woundedTough.Count > 0 ? [woundedTough[0]] : pool;
    }

    static bool IsTough(GameState state, Guid modelId) => (Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1) > 1;

    /// <summary>p.15's "heroes must be assigned wounds last": a living hero and a living model that is not one, so the hero was held back from this wound.</summary>
    static bool HeroDeferred(GameState state, Guid unit)
    {
        var living = Lookup.AliveModels(state, unit).Where(m => !AtCapacity(state, m)).ToList();
        bool IsHero(Guid m) => state.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero);
        return living.Any(IsHero) && living.Any(m => !IsHero(m));
    }

    /// <summary>
    /// One segment per run, repeating (p.8: one model removed for each wound, so ordinary wounds carry over past a dead model): the
    /// wounds left come from the parent's failed defense rolls on the first run and from the previous segment's "wounds_left" after.
    /// Deadly's multiplied wounds and a Takedown's (a unit of one, p.14) do not carry over, so only their excess is lost. A carried wound
    /// has already taken its wound-ignoring roll (Regeneration p.14, Resistance, Self-Repair, Plaguebound: "roll one die for each"), so
    /// "wounds_carried" counts the wounds left that keep their result and roll no die again. With no model left to take a wound (an earlier
    /// weapon group destroyed the unit, p.8 "all weapons are fired simultaneously") the wounds are lost and nothing is asked.
    /// </summary>
    sealed class AssignStep : IStep
    {
        public string Name => "assign";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var wounds = ctx.Frame.Values.ContainsKey("wounds_left") ? ctx.Frame.Value("wounds_left") : WoundsToDeal(ctx.Frame);
            if (wounds <= 0) return new Finish([]);
            var team = ctx.TeamOf(target) ?? throw new InvalidOperationException("target unit has no team");
            // A Takedown group named its model, so nothing is asked and no ordering is consulted: p.15's Tough-first and heroes-last
            // decide the prompt's targets, and there is no prompt (p.14).
            var takedown = TakedownModel(ctx.Frame) is { } named && Lookup.AliveModels(ctx.State, target).Contains(named) ? named : (Guid?)null;
            var eligible = takedown is null ? EligibleForNextWound(ctx.State, ctx.Properties, target) : [];
            if (takedown is null && eligible.Count == 0) return new Finish([]);
            if (takedown is not null) ctx.Applied.Add(GfNames.Takedown);
            else if (HeroDeferred(ctx.State, target)) ctx.Applied.Add(GfNames.Hero);
            // p.15: "continue to put wounds on the tough model with most wounds in the unit until it is killed, before starting to put them on
            // the next tough model". Offered only Tough models (none wounded), the defender names one, which takes every wound it can; the
            // rest carry to the next segment, which offers the survivors again. A spread answer (out of set, warned) reads as its first model.
            var toughOnly = eligible.All(m => IsTough(ctx.State, m));
            Dictionary<Guid, int> allocation;
            if (takedown is { } one) allocation = new Dictionary<Guid, int> { [one] = wounds };
            else if (eligible.Count == 1) allocation = new Dictionary<Guid, int> { [eligible[0]] = wounds };   // nothing to choose
            else if (ctx.History.LastClosed() is { Single: Allocated } closed)
            {
                allocation = GfAnswers.Allocated(closed, Lookup.AliveModels(ctx.State, target), wounds);
                if (toughOnly && allocation.Keys.FirstOrDefault() is var first && first != Guid.Empty)
                    allocation = new Dictionary<Guid, int> { [first] = wounds };
            }
            else return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(eligible, wounds, toughOnly ? 1 : null))]);

            var actor = ctx.Frame.Context.ActorUnitId;
            var ix = AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, actor is null ? null : KindOf(ctx), ctx.Frame.Context.Action);
            var live = ctx.Settle(ix);
            var targetSide = actor is null ? Side.Attacker : Side.Defender;
            var withGranted = GrantedOf(ctx.Frame).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(withGranted, Side.Attacker);
            // Regeneration is aggregation all (3.5.1: "a unit where all models have this rule"), so LiveProperties.Gather settles it
            // as one unit-level property with no ModelId, and it applies to every wound the unit takes. p.13: instances of a rule
            // without (X) do not stack, so one roll is taken however many are live, a granted instance included. Threshold is the row's.
            // A once-per-game roll (Protection Feat: "Once per game, when this unit takes wounds … roll one die per wound") is this unit's for every wound of
            // the first attack it is rolled against, and for no later one.
            var regen = RollEffects.OwnRolls(live, targetSide, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound },
                (source, roll) => !OncePerGame(roll) || !RolledInAnotherFrame(ctx, source.Entry.Name, target)).FirstOrDefault();

            var carried = ctx.Frame.Value("wounds_carried");
            var changes = new List<StateChange>(); var landed = 0; var spilled = 0; var moves = new List<ModelRelocation>(); var dead = new HashSet<Guid>();
            var perModel = new Dictionary<string, int>();
            foreach (var (modelId, count) in allocation.Where(kv => kv.Value > 0))
            {
                var model = ctx.Projection.Models[modelId];
                var capacity = Lookup.ModelParameter(ctx.State, modelId, "Tough") ?? 1;
                if (capacity > 1) ctx.Applied.Add(GfNames.Tough);
                var incoming = count * multiplier;
                var kept = Math.Min(carried, incoming);   // carried wounds come first and keep the result they rolled
                carried -= kept;
                if (regen.Roll is { } roll && incoming > kept)
                {
                    var record = ctx.Roll(new Dice.RollPlan(regen.Source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), incoming - kept);
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
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            changes.AddRange(Consumed(live));   // whatever the step read is spent: a granted regeneration rolled, a granted wound multiplier applied
            var total = ctx.Frame.Value("wounds_dealt") + landed;
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            var left = takedown is null ? wounds - allocation.Values.Sum() + spilled : 0;
            var survivors = Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m) && !AtCapacity(ctx.State, m));
            var values = new Dictionary<string, int>(perModel) { ["wounds_dealt"] = total, ["wounds_left"] = left, ["wounds_carried"] = takedown is null ? spilled : 0 };
            return new Complete(changes, values, Repeat: left > 0 && survivors);
        }
    }

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
    static IReadOnlyList<StateChange> Consumed(LiveSet live) => RollEffects.Consume(live);

    public static Sequence AttackSequence() => new(GfNames.AttackSequence, [new DetermineAttacksStep(), new PickTargetModelStep(), new RollToHitStep(), new ScoreHitsStep(), new PushStep("resolve", GfNames.ResolveHits), new SelfWoundsStep()]);
    public static Sequence ResolveHits() => new(GfNames.ResolveHits, [new RollToBlockStep(), new PushStep("deal", GfNames.DealWounds)]);

    /// <summary>p.14 Takedown is a weapon property, so a group carries it when one of its weapons does. GfGroups reads this for WeaponGroup.Takedown, and splits such a group so each holds one model's weapon.</summary>
    public static bool IsTakedownGroup(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => weapons.Contains(w.Id)).Any(w => w.Properties.Any(p => p.Name == GfNames.Takedown));

    /// <summary>The attack kind of the frame's group: a weapon-less group under a spell-hits frame is a spell's hits (spell library → `spell`); a
    /// weapon-less group elsewhere is Impact's, which is melee; otherwise the weapons decide.</summary>
    public static AttackKind KindOf(StepContext ctx)
    {
        if (ctx.Frame.Context.WeaponIds.Count == 0)
            for (var f = ctx.Frame; f is not null; f = f.Parent)
                if (f.Frame.Sequence == GfNames.SpellHitsSeq) return AttackKind.Spell;
        return KindOf(ctx.State, ctx.Frame.Context.WeaponIds);
    }

    public static AttackKind KindOf(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => weapons.Contains(w.Id))?.Stats.ContainsKey(GfNames.Range) == true ? AttackKind.Shooting : AttackKind.Melee;

    /// <summary>
    /// p.8: models with line of sight to a target model and a weapon in range of it; p.9: within 2" horizontally and 4" vertically.
    /// Containment answers before any of that (p.15): a unit inside a container and a unit outside it cannot target each other at
    /// all, and two units in one container — or an occupant and the container model itself — need no measurement, because there is
    /// no distance and no sight line inside a box. Everything armed is eligible then; only the weapon check survives.
    /// The occupant-and-container half of that clause is unreachable in a game: `ValidTargets` offers enemy units
    /// only, a unit's own transport is friendly, and no unit may enter an enemy container, so the branch is written for
    /// faithfulness to p.15 and is exercised only by a menu-written state.
    /// </summary>
    public static IReadOnlyList<Guid> EligibleModels(GameState state, LiveSet live, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind)
    {
        if (Battlefield.InsideReach(state, attacker, target) is { } reaches)
            return reaches ? Lookup.AliveModels(state, attacker).Where(m => state.Projection.Models[m].Slot.Weapons.Any(w => weapons.Contains(w.Id))).ToList() : [];
        var targets = Lookup.LivingModels(state, target);
        var indirect = live.Has(Side.Attacker, GfNames.Indirect);
        bool Reaches(Guid m, Sight? sight)
        {
            var mine = state.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).ToList();
            if (mine.Count == 0) return false;
            if (kind == AttackKind.Melee) return targets.Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical);
            var range = mine.Max(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0);
            return targets.Any(t => Battlefield.Distance(state, m, t) <= range && (sight is null || sight.CanSee(m, t)));
        }
        var sight = kind == AttackKind.Shooting && !indirect ? Sight.From(state, attacker) : null;               // built once for every pair below
        var eligible = Lookup.LivingModels(state, attacker).Where(m => Reaches(m, sight)).ToList();
        // p.14 Indirect "may target enemies that are not in line of sight": it acted when a model is eligible only because sight was not asked.
        if (indirect && kind == AttackKind.Shooting && eligible.Count > 0)
        {
            var seen = Sight.From(state, attacker);
            if (eligible.Any(m => !Reaches(m, seen))) live.Record(Side.Attacker, GfNames.Indirect);
        }
        return eligible;
    }

    /// <summary>p.11's majority with p.14 Indirect's "ignores cover from sight obstructions": Indirect acted when dropping the sight half took the cover away,
    /// and the verdict then is the full test's, ignored by Indirect. The +1 applies when the verdict is in cover and nothing ignored it (Applies).</summary>
    static CoverVerdict CoverOf(GameState state, LiveSet live, Guid target, IReadOnlyList<Guid> eligible, Guid? takedown = null)
    {
        var indirect = live.Has(Side.Attacker, GfNames.Indirect);
        var count = GfRolls.Cover(state, target, eligible, indirect, takedown);
        if (indirect && !count.Majority && GfRolls.Cover(state, target, eligible, ignoresSightObstructions: false, takedown) is { Majority: true } full)
        {
            live.Record(Side.Attacker, GfNames.Indirect);
            return new CoverVerdict(true, full.Models, full.Of, GfNames.Indirect);
        }
        return new CoverVerdict(count.Majority, count.Models, count.Of);
    }

    static bool Applies(CoverVerdict c) => c is { InCover: true, IgnoredBy: null };

    /// <summary>The verdict as Values: `in_cover` (the +1 test passed), `cover_models`, `cover_of`, and `cover_indirect` when Indirect's dropped
    /// sight half lost the cover, each with the pusher's `_&lt;i&gt;` suffix when fire-groups settles it.</summary>
    public static void WriteCover(IDictionary<string, int> values, CoverVerdict c, string suffix = "")
    {
        values[$"in_cover{suffix}"] = Applies(c) ? 1 : 0;
        values[$"cover_models{suffix}"] = c.Models;
        values[$"cover_of{suffix}"] = c.Of;
        if (c.IgnoredBy == GfNames.Indirect) values[$"cover_indirect{suffix}"] = 1;
    }

    /// <summary>The verdict a frame's Values hold; null when it holds no `cover_of` (a frame written before the verdict).</summary>
    static CoverVerdict? ReadCover(IReadOnlyDictionary<string, int> values)
    {
        if (!values.TryGetValue("cover_of", out var of)) return null;
        var indirect = values.GetValueOrDefault("cover_indirect") == 1;
        return new CoverVerdict(values.GetValueOrDefault("in_cover") == 1 || indirect, values.GetValueOrDefault("cover_models"), of, indirect ? GfNames.Indirect : null);
    }

    /// <summary>
    /// p.8's Determine Attacks, asked from outside the group's own frame: what this group would roll against this target now,
    /// split by the Quality value each attack is tested against. `fire-groups` calls it once per group before the first shot,
    /// because p.8 fires a unit's weapons simultaneously, and melee's striking steps before the first strike (p.9). The action is
    /// the pushed child's, since a defender strikes back under none.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> AttacksAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind, string? action)
    {
        var live = ctx.Settle(AttackInteraction(ctx, attacker, target, weapons, kind, action));
        return Attacks(ctx, live, EligibleModels(ctx.State, live, attacker, target, weapons, kind), weapons, kind);
    }

    /// <summary>
    /// p.11's cover majority, asked from outside the group's own frame, for the same reason and at the same moment as the attack
    /// counts: p.8 fires a unit's weapons simultaneously, so a group firing after another group's casualties judges *"the majority
    /// of models in a unit"* against the unit as it stood when the action started. Positions do not change
    /// between groups; only the count does.
    /// </summary>
    public static CoverVerdict InCoverAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons)
    {
        var live = ctx.Settle(AttackInteraction(ctx, attacker, target, weapons, AttackKind.Shooting, ctx.Frame.Context.Action));
        return CoverOf(ctx.State, live, target, EligibleModels(ctx.State, live, attacker, target, weapons, AttackKind.Shooting));
    }

    /// <summary>
    /// p.8 sums the Attack values of the models that can shoot, and p.8's next step takes "as many Quality tests as attacks". p.4
    /// scores each of those against *"the model's Quality value"*, so the sum is per Quality value rather than per unit: a joined
    /// hero contributes its own attacks at its own value and everything else contributes at the unit's. Ordered
    /// by the value, best first, so the pools and therefore the dice cursor do not depend on model order.
    /// </summary>
    static IReadOnlyList<(int Quality, int Attacks)> Attacks(StepContext ctx, LiveSet live, IEnumerable<Guid> models, IReadOnlyList<Guid> weapons, AttackKind kind) =>
        models.Select(m => (Quality: GfRolls.HitQuality(ctx, live, kind, m),
                            Attacks: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).Sum(w => StatReader.Attacks(ctx.State, w.Id))
                                + RegenerativeStrength(ctx, m, kind, weapons)))
              .Where(x => x.Attacks > 0)
              .GroupBy(x => x.Quality).Select(g => (Quality: g.Key, Attacks: g.Sum(x => x.Attacks)))
              .OrderBy(x => x.Quality).ToList();

    /// <summary>Regenerative Strength's "+X attacks" in melee, on the model's first melee weapon when this group holds it.</summary>
    static int RegenerativeStrength(StepContext ctx, Guid model, AttackKind kind, IReadOnlyList<Guid> weapons)
    {
        if (kind != AttackKind.Melee || ctx.Projection.Models[model].Slot.Weapons.FirstOrDefault(w => !w.Stats.ContainsKey(GfNames.Range)) is not { } first || !weapons.Contains(first.Id)) return 0;
        var x = GfMarkers.RegenerativeStrength(ctx.State, model);
        if (x > 0) ctx.Applied.Add(GfNames.RegenerativeStrength);
        return x;
    }

    /// <summary>
    /// Attack counts keyed by the Quality value they are rolled at, read back off a frame's `Values`: `&lt;prefix&gt;&lt;n&gt;` holds the
    /// attacks tested against `n`+. `determine-attacks` writes `attacks_q&lt;n&gt;` on its own frame and `fire-groups` writes
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` on the pushing one, so one reader serves both.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> ByQuality(IReadOnlyDictionary<string, int> values, string prefix) =>
        values.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(kv.Key.AsSpan(prefix.Length), out _))
              .Select(kv => (Quality: int.Parse(kv.Key[prefix.Length..]), Attacks: kv.Value))
              .OrderBy(x => x.Quality).ToList();

    public static int HitsFrom(FrameView parent)
    {
        var hitRolls = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
        if (hitRolls.Count == 0) return parent.Value("hits") * Math.Max(1, parent.Value("hit_multiplier", 1));
        return (hitRolls.Sum(Dice.RollOutcome.Successes) + parent.Value("extra_hits")) * Math.Max(1, parent.Value("hit_multiplier", 1));
    }

    static (Interaction Ix, LiveSet Live, AttackKind Kind) Settle(StepContext ctx)
    {
        var c = ctx.Frame.Context;
        var kind = KindOf(ctx);
        var ix = AttackInteraction(ctx, c.ActorUnitId, c.TargetUnitIds[0], c.WeaponIds, kind, c.Action);
        return (ix, ctx.Settle(ix), kind);
    }

    /// <summary>Pushes a child once and completes when it has finished. Values pass through the frame stack, not through this step.</summary>
    public sealed class PushStep(string name, string sequence, Func<StepContext, SequenceContext>? context = null) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx) => ctx.History.Children.Any(ch => ch.Finished || ch.Abandoned) ? new Complete([]) : new Push(sequence, context?.Invoke(ctx) ?? ctx.Frame.Context);
    }

    /// <summary>
    /// p.8's first step. The counts are not this frame's to compute when a pushing step already settled them: `fire-groups` fixes
    /// every group's attacks before the first shot, because p.8 fires a unit's weapons simultaneously, and melee's striking steps
    /// fix them before the first strike, because p.9 sums "how many attacks the unit has in total for this melee"; this reads
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` back off the pushing frame. Only a push that recorded nothing is counted here and now.
    /// `eligible_models` is the live figure either way, so a recorded count standing over an empty eligible set is visible in the log.
    ///
    /// The counts are per Quality value, one key each, because p.4 tests every attack against *"the model's Quality value"* and a
    /// joined hero brings its own. `attacks` stays the total, which is what the log line and the readers that do
    /// not care about the split read. `defender_models` is the defending unit's living models, settled at the same moment and for
    /// the same reason, and carried here so `roll-to-hit` can cap `Blast` with it (p.13) and `roll-to-block` can decide whether
    /// p.14's joined hero is alone. `in_cover` is p.11's majority, settled at that same moment and carried here for
    /// `roll-to-block`. A melee push settles no `in_cover`, which melee does not read; a push that settled nothing falls back to the live reading.
    /// </summary>
    sealed class DetermineAttacksStep : IStep
    {
        public string Name => "determine-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var eligible = EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, ctx.Frame.Context.WeaponIds, kind);
            // `attacks_<i>` is the presence signal rather than the per-Quality keys, because a group the pusher settled at zero
            // attacks writes no per-Quality key at all and must still not be recounted here.
            var settled = Pushed(ctx.Frame, "attacks");
            var byQuality = settled is null ? Attacks(ctx, live, eligible, ctx.Frame.Context.WeaponIds, kind) : PushedByQuality(ctx.Frame);
            var values = new Dictionary<string, int>
            {
                ["attacks"] = settled ?? byQuality.Sum(x => x.Attacks),
                ["eligible_models"] = eligible.Count,
                ["defender_models"] = Pushed(ctx.Frame, "defender_models") ?? Lookup.LivingModels(ctx.State, ix.Defender.UnitId).Count,
            };
            if (Pushed(ctx.Frame, "in_cover") is { } pushedCover)
            {
                values["in_cover"] = pushedCover;
                foreach (var key in new[] { "cover_models", "cover_of", "cover_indirect" }) if (Pushed(ctx.Frame, key) is { } v) values[key] = v;
            }
            else if (kind == AttackKind.Shooting) WriteCover(values, CoverOf(ctx.State, live, ix.Defender.UnitId, eligible));
            else values["in_cover"] = 0;
            foreach (var (quality, attacks) in byQuality) values[$"attacks_q{quality}"] = attacks;
            return values["attacks"] == 0 ? new Finish(Consumed(live), values) : new Complete(Consumed(live), values);
        }

    }

    /// <summary>
    /// A number the pushing step settled for this child before it pushed anything, keyed by the child's ordinal among that step's
    /// children: `defender_models_&lt;i&gt;`, and `attacks_&lt;i&gt;_q&lt;n&gt;` through `ByQuality`. Read off the pusher's recording run, the
    /// parent's last completion, which is still marked as a repeat while its children run; a frame's merged values would mix in the
    /// keys an earlier striking step of the same melee wrote. Null when the pusher recorded none.
    /// </summary>
    static int? Pushed(FrameView frame, string prefix) =>
        Recording(frame) is { } values && values.TryGetValue($"{prefix}_{frame.Parent!.History.Children.Count - 1}", out var n) ? n : null;

    /// <summary>The same read for the per-Quality keys, which are a set rather than one number. Empty when the pusher recorded none.</summary>
    static IReadOnlyList<(int Quality, int Attacks)> PushedByQuality(FrameView frame) =>
        Recording(frame) is { } values ? ByQuality(values, $"attacks_{frame.Parent!.History.Children.Count - 1}_q") : [];

    static IReadOnlyDictionary<string, int>? Recording(FrameView frame) =>
        frame.Parent is { History.Children.Count: > 0 } parent && parent.Frame.Steps is [.., var last] && last.Values is { } values && values.ContainsKey(StepRepeat.Key) ? values : null;

    /// <summary>
    /// p.14 Takedown: the model names one model in the target unit as the target of its wounds. A target declaration, so it is made
    /// before this group's hit roll (p.8), and after determine-attacks, so a group with nothing in range never asks. One pick per
    /// group and not per shot: GfGroups gives a takedown weapon type one group per model, so the group is one model's dice
    /// at one Quality value and roll-to-hit pools them into one roll. deal-wounds reads the answer
    /// back through TakedownModel and assigns to that model without opening an allocation.
    /// </summary>
    sealed class PickTargetModelStep : IStep
    {
        public string Name => "pick-target-model";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var (weapons, target) = (ctx.Frame.Context.WeaponIds, ctx.Frame.Context.TargetUnitIds[0]);
            if (!IsTakedownGroup(ctx.State, weapons)) return new Complete([]);
            var models = Lookup.AliveModels(ctx.State, target);
            if (models.Count < 2) return new Complete([]);   // one model to choose from: the ordinary allocation reaches it anyway
            var reachable = Pickable(ctx, weapons, target, models);
            return new OpenAsks([ctx.Ask("takedown_target", "Pick the model this takedown weapon targets", ctx.ActorTeam!.Value,
                new PieceSelection(reachable.Count > 0 ? reachable : models, 1, 1))]);
        }

        /// <summary>
        /// "resolved as if it was a unit of [1]": a model the group's weapon is in range of and, shooting without Indirect, sees (p.8), with the
        /// target's own models out of the way, since "other models in the target's unit don't block line of sight ... to the target model".
        /// Melee and a container's occupants (no sight line inside a box) are not narrowed.
        /// </summary>
        static IReadOnlyList<Guid> Pickable(StepContext ctx, IReadOnlyList<Guid> weapons, Guid target, IReadOnlyList<Guid> models)
        {
            var (_, live, kind) = Settle(ctx);
            var attacker = ctx.Frame.Context.ActorUnitId!.Value;
            if (kind != AttackKind.Shooting || Battlefield.InsideReach(ctx.State, attacker, target) is not null) return models;
            var sight = live.Has(Side.Attacker, GfNames.Indirect) ? null : Sight.From(ctx.State, attacker, alsoIgnoring: target);
            var shooters = Lookup.LivingModels(ctx.State, attacker).Select(m => (Id: m, Range: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id))
                .Select(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0).DefaultIfEmpty(-1).Max())).Where(s => s.Range >= 0).ToList();
            return models.Where(t => ctx.Projection.Models[t].Location is OnTable
                && shooters.Any(s => Battlefield.Distance(ctx.State, s.Id, t) <= s.Range && (sight is null || sight.CanSee(s.Id, t)))).ToList();
        }
    }

    /// <summary>
    /// p.8's second step: *"take as many Quality tests as attacks"*. One pooled roll per Quality value the attacking models brought,
    /// in ascending order, since p.4 scores each test against *"the model's Quality value"* and a joined hero brings its own.
    /// p.4's *"having dice of multiple colors ... and then roll them all at once"* is the tabletop procedure for it. The
    /// counts are `determine-attacks`' `attacks_q&lt;n&gt;`, so nothing is recounted here and the pools are the ones the action started
    /// with. What a face decides is `score-hits`' (opr-sequences → Dice edits); only the dice an extra-attacks row adds are drawn
    /// here, on the faces as rolled, because drawing is the roll's.
    /// </summary>
    sealed class RollToHitStep : IStep
    {
        public string Name => "roll-to-hit";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            // p.13 caps Blast at "as many hits as models in the target unit" without saying when they are counted. p.8 fires all
            // weapons simultaneously, so the cap is fixed at the same moment as the attack counts: this reads the defender's model
            // count that determine-attacks carried down from the pushing frame.
            if (kind == AttackKind.Shooting) live.Spend(Side.Attacker, GfNames.Indirect);   // p.14: applies to this attack (its to-hit row is read below), so a granted one is spent here; it is recorded where it acts (EligibleModels, InCover, its to-hit row)
            // p.14 Takedown: the picked model is "resolved as if it was a unit of [1]", so a Takedown group's cap is that one model.
            var living = Lookup.LivingModels(ctx.State, ix.Defender!.UnitId);
            var cap = TakedownModel(ctx.Frame) is { } picked && living.Contains(picked) ? 1 : ctx.Frame.Value("defender_models", living.Count);
            var values = new Dictionary<string, int> { ["hit_multiplier"] = RollEffects.HitMultiplier(live, Side.Attacker, cap) };
            foreach (var (quality, attacks) in ByQuality(ctx.Frame.Values, "attacks_q"))
            {
                var plan = GfRolls.HitPlan(ctx, live, kind, quality);
                IReadOnlyList<string>? Triggers(Die d) => RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit);
                var added = ctx.Roll(plan, attacks, Triggers).Dice.Sum(d => AddedAttacks(live, d));
                // "This rule doesn't apply to newly generated attacks": the added dice add no attacks of their own.
                if (added > 0) ctx.Roll(plan, added, d => RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit, attacks: false));
            }
            return new Complete(GfDeferred.Leave(Consumed(live), ix, l => RollEffects.ReadPerDie(l, Side.Attacker, GfNames.Hit)), values);
        }

        /// <summary>The attacks a successful die's extra-attacks rows add, recorded as having acted; nothing else a face decides is read here.</summary>
        static int AddedAttacks(LiveSet live, Die die)
        {
            if (!die.Success) return 0;
            var adds = live.Effects(Side.Attacker, EffectKind.AddHit, new DieContext(GfNames.Hit, die.Raw, die.Modified, die.Success))
                .Where(x => x.Effect.TargetQuantity == TargetQuantity.Attacks && x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).ToList();
            foreach (var x in adds) live.Record(x.Source);
            return adds.Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        }
    }

    /// <summary>
    /// What the frame's `hit` dice decide, read as they now stand, so an edit made after the roll counts (opr-sequences → Dice edits):
    /// `extra_hits` and every `granted_&lt;entry&gt;_&lt;amount&gt;` on a success, and `self_wounds` on any die, summed across every pool and
    /// the added dice, since they belong to the group's hits rather than to one pool. `wounds` is `self_wounds`, for the self-wounds
    /// deal. Shared by `attack-sequence` and `spell-hits`; a spell's frame finishes here when it scored no hit.
    /// </summary>
    public sealed class ScoreHitsStep(bool finishWithoutHits = false) : IStep
    {
        public string Name => "score-hits";
        public StepOutcome Run(StepContext ctx)
        {
            var (_, live, _) = Settle(ctx);
            var rolls = ctx.Frame.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["self_wounds"] = 0 };
            foreach (var die in rolls.SelectMany(r => r.Dice))
            {
                var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                values["self_wounds"] += effects.ExtraWounds;
                if (!die.Success) continue;
                values["extra_hits"] += effects.ExtraHits;
                if (GrantKey(effects.Grants) is { } key) values[key] = values.GetValueOrDefault(key) + 1;
            }
            if (rolls.Count > 0) RollEffects.ReadPerDie(live, Side.Attacker, GfNames.Hit);
            values["wounds"] = values["self_wounds"];
            var spent = Consumed(live);
            var scored = ctx.Frame.Value("hits") + rolls.Sum(Dice.RollOutcome.Successes);
            return finishWithoutHits && scored == 0 ? new Finish(spent, values) : new Complete(spent, values);
        }
    }

    /// <summary>`granted_&lt;entry&gt;_&lt;amount&gt;`: the entry may hold underscores, the amount never does, so the split is at the last one.</summary>
    public static (string Entry, int Amount) ParseGrantKey(string key)
    {
        var cut = key.LastIndexOf('_');
        return (key["granted_".Length..cut], int.TryParse(key[(cut + 1)..], out var amount) ? amount : 0);
    }

    /// <summary>`granted_&lt;entry&gt;_&lt;amount&gt;` for one per-die grant on a hit; a hit carrying several is keyed by the whole set, `granted_&lt;a&gt;_&lt;n&gt;|&lt;b&gt;_&lt;m&gt;`,
    /// in name order, so it is counted in one key and blocked once. Null when the die carries none.</summary>
    public static string? GrantKey(IEnumerable<(string Entry, int? Amount)> grants) =>
        grants.Select(g => $"{g.Entry}_{g.Amount ?? 0}").Distinct().Order(StringComparer.Ordinal).ToList() is { Count: > 0 } set ? "granted_" + string.Join('|', set) : null;

    /// <summary>The grants a `granted_` key names: one for a single grant, several for a set.</summary>
    public static IReadOnlyList<(string Entry, int Amount)> ParseGrantSet(string key) =>
        key["granted_".Length..].Split('|').Select(m => ParseGrantKey("granted_" + m)).ToList();

    sealed class RollToBlockStep : IStep
    {
        public string Name => "roll-to-block";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var parent = ctx.Frame.Parent!;
            var hits = HitsFrom(parent);
            if (hits <= 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            // p.11's majority is frozen with the attack counts (p.8: all weapons fire simultaneously), so a group firing after casualties reads the
            // fact determine-attacks carried down. A parent that recorded none is a spell's hits or Impact's, where live is the answer.
            var defender = ix.Defender!.UnitId;
            // The verdict is the unit's, one for every block group (RollRecord.Cover); a frame written before it holds in_cover alone and logs none.
            CoverVerdict? cover = null; var inCover = false;
            // p.14 Takedown: the picked model is "resolved as if it was a unit of [1]", so its own cover is the verdict, read now that it is named.
            var picked = TakedownModel(ctx.Frame) is { } named && Lookup.LivingModels(ctx.State, defender).Contains(named) ? named : (Guid?)null;
            if (kind == AttackKind.Shooting)
            {
                if (picked is { } m) { cover = CoverOf(ctx.State, live, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind), m); inCover = Applies(cover); }
                else if (parent.Values.TryGetValue("in_cover", out var settled)) { inCover = settled == 1; cover = ReadCover(parent.Values); }
                else { cover = CoverOf(ctx.State, live, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind)); inCover = Applies(cover); }
            }
            var multiplier = Math.Max(1, parent.Value("hit_multiplier", 1));
            // p.14's Hero clause needs the defending unit's model count, and determine-attacks carried down the one settled before any
            // group fired. A parent that recorded none is a spell's hits or Impact's, where the live count is the count.
            var defenderModels = parent.Value("defender_models", Lookup.LivingModels(ctx.State, defender).Count);
            var wounds = 0; var extraWounds = 0; var plain = hits;
            RollEffects.ReadPerDie(live, Side.Defender, GfNames.DefenseRoll);
            // One live set carries every whole-group rule (a weapon's, a spell's); the hits split only by the set of per-die grants each die carries.
            foreach (var (key, count) in parent.Values.Where(kv => kv.Key.StartsWith("granted_")).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var granted = ParseGrantSet(key).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
                var group = count * multiplier; plain -= group;
                var plan = GfRolls.DefensePlan(ctx, granted, inCover, defenderModels, cover, picked);
                var record = ctx.Roll(plan, group, d => RollEffects.Triggers(granted, Side.Defender, d, GfNames.DefenseRoll));
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(granted, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
            }
            if (plain > 0)
            {
                var plan = GfRolls.DefensePlan(ctx, live, inCover, defenderModels, cover, picked);
                var record = ctx.Roll(plan, plain, d => RollEffects.Triggers(live, Side.Defender, d, GfNames.DefenseRoll));
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(live, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
            }
            return new Complete(Consumed(live), new Dictionary<string, int> { ["wounds"] = wounds, ["extra_wounds"] = extraWounds });
        }
    }
}
