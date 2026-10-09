using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep(), new DisgorgeStep()]);

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
        bool IsTough(Guid m) => (Lookup.ModelParameter(state, m, "Tough") ?? 1) > 1;
        var plain = pool.Where(m => !IsTough(m)).ToList();
        if (plain.Count > 0) return plain;
        var woundedTough = pool.Where(m => state.Projection.Models[m].Wounds > 0).OrderByDescending(m => state.Projection.Models[m].Wounds).ToList();
        return woundedTough.Count > 0 ? [woundedTough[0]] : pool;
    }

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
    /// Deadly's multiplied wounds and a Takedown's (a unit of one, p.14) do not carry over, so only their excess is lost.
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
            if (takedown is not null) ctx.Applied.Add(GfNames.Takedown);
            else if (HeroDeferred(ctx.State, target)) ctx.Applied.Add(GfNames.Hero);
            Dictionary<Guid, int> allocation;
            if (takedown is { } one) allocation = new Dictionary<Guid, int> { [one] = wounds };
            else if (eligible.Count == 1) allocation = new Dictionary<Guid, int> { [eligible[0]] = wounds };   // nothing to choose
            else if (ctx.History.LastClosed() is { Single: Allocated } closed) allocation = GfAnswers.Allocated(closed, Lookup.AliveModels(ctx.State, target), wounds);
            else return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(eligible, wounds))]);

            var actor = ctx.Frame.Context.ActorUnitId;
            var ix = AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, actor is null ? null : KindOf(ctx), ctx.Frame.Context.Action);
            var live = ctx.Settle(ix);
            var targetSide = actor is null ? Side.Attacker : Side.Defender;
            var withGranted = GrantedOf(ctx.Frame).Aggregate(live, (l, g) => l.WithGranted(Side.Attacker, g.Entry, g.Amount));
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(withGranted, Side.Attacker);
            // Regeneration is aggregation all (3.5.1: "a unit where all models have this rule"), so LiveProperties.Gather settles it
            // as one unit-level property with no ModelId, and it applies to every wound the unit takes. p.13: instances of a rule
            // without (X) do not stack, so one roll is taken however many are live, a granted instance included. Threshold is the row's.
            var regen = RollEffects.OwnRolls(live, targetSide, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound })
                .FirstOrDefault();

            var changes = new List<StateChange>(); var landed = 0; var spilled = 0; var moves = new List<ModelRelocation>(); var dead = new HashSet<Guid>();
            foreach (var (modelId, count) in allocation.Where(kv => kv.Value > 0))
            {
                var model = ctx.Projection.Models[modelId];
                var capacity = Lookup.ModelParameter(ctx.State, modelId, "Tough") ?? 1;
                if (capacity > 1) ctx.Applied.Add(GfNames.Tough);
                var incoming = count * multiplier;
                if (regen.Roll is { } roll)
                {
                    var record = ctx.Roll(new Dice.RollPlan(regen.Source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), incoming);
                    incoming = Dice.RollOutcome.Failures(record);   // each success overrides one wound
                }
                var applied = Math.Min(capacity - model.Wounds, incoming);
                if (multiplier == 1) spilled += incoming - applied;   // an ordinary wound past a dead model is removed from the next one (p.8)
                landed += applied;
                changes.Add(new WoundsSet(modelId, model.Wounds + applied));
                // p.15: a container holding anybody stays on the table until disgorge has placed them, "before it's removed".
                if (model.Wounds + applied < capacity) continue;
                dead.Add(modelId);
                if (!(GfMoves.IsContainer(ctx.State, modelId) && Lookup.OccupantUnits(ctx.State, modelId).Count > 0)) moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
            }
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            changes.AddRange(Consumed(live));   // whatever the step read is spent: a granted regeneration rolled, a granted wound multiplier applied
            var total = ctx.Frame.Value("wounds_dealt") + landed;
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            var left = takedown is null ? wounds - allocation.Values.Sum() + spilled : 0;
            var survivors = Lookup.AliveModels(ctx.State, target).Any(m => !dead.Contains(m) && !AtCapacity(ctx.State, m));
            var values = new Dictionary<string, int> { ["wounds_dealt"] = total, ["wounds_left"] = left };
            return new Complete(changes, values, Repeat: left > 0 && survivors);
        }
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

    public static Sequence AttackSequence() => new(GfNames.AttackSequence, [new DetermineAttacksStep(), new PickTargetModelStep(), new RollToHitStep(), new PushStep("resolve", GfNames.ResolveHits), new SelfWoundsStep()]);
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

    /// <summary>p.11's majority with p.14 Indirect's "ignores cover from sight obstructions": Indirect acted when dropping the sight half took the cover away.</summary>
    static bool InCover(GameState state, LiveSet live, Guid target, IReadOnlyList<Guid> eligible)
    {
        var indirect = live.Has(Side.Attacker, GfNames.Indirect);
        var covered = GfRolls.InCover(state, target, eligible, indirect);
        if (indirect && !covered && GfRolls.InCover(state, target, eligible, ignoresSightObstructions: false)) live.Record(Side.Attacker, GfNames.Indirect);
        return covered;
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
    public static bool InCoverAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons)
    {
        var live = ctx.Settle(AttackInteraction(ctx, attacker, target, weapons, AttackKind.Shooting, ctx.Frame.Context.Action));
        return InCover(ctx.State, live, target, EligibleModels(ctx.State, live, attacker, target, weapons, AttackKind.Shooting));
    }

    /// <summary>
    /// p.8 sums the Attack values of the models that can shoot, and p.8's next step takes "as many Quality tests as attacks". p.4
    /// scores each of those against *"the model's Quality value"*, so the sum is per Quality value rather than per unit: a joined
    /// hero contributes its own attacks at its own value and everything else contributes at the unit's. Ordered
    /// by the value, best first, so the pools and therefore the dice cursor do not depend on model order.
    /// </summary>
    static IReadOnlyList<(int Quality, int Attacks)> Attacks(StepContext ctx, LiveSet live, IEnumerable<Guid> models, IReadOnlyList<Guid> weapons, AttackKind kind) =>
        models.Select(m => (Quality: GfRolls.HitQuality(ctx, live, kind, m),
                            Attacks: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).Sum(w => StatReader.Attacks(ctx.State, w.Id))))
              .Where(x => x.Attacks > 0)
              .GroupBy(x => x.Quality).Select(g => (Quality: g.Key, Attacks: g.Sum(x => x.Attacks)))
              .OrderBy(x => x.Quality).ToList();

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
                ["in_cover"] = Pushed(ctx.Frame, "in_cover") ?? (kind == AttackKind.Shooting && InCover(ctx.State, live, ix.Defender.UnitId, eligible) ? 1 : 0),
            };
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
            return new OpenAsks([ctx.Ask("takedown_target", "Pick the model this takedown weapon targets", ctx.ActorTeam!.Value, new PieceSelection(models, 1, 1))]);
        }
    }

    /// <summary>
    /// p.8's second step: *"take as many Quality tests as attacks"*. One pooled roll per Quality value the attacking models brought,
    /// in ascending order, since p.4 scores each test against *"the model's Quality value"* and a joined hero brings its own.
    /// p.4's *"having dice of multiple colors ... and then roll them all at once"* is the tabletop procedure for it. The
    /// counts are `determine-attacks`' `attacks_q&lt;n&gt;`, so nothing is recounted here and the pools are the ones the action started
    /// with. `extra_hits` and every `granted_&lt;entry&gt;_&lt;amount&gt;` sum across all of the pools, because they belong to the group's hits
    /// rather than to one pool, and `GfAttack.HitsFrom` already sums the successes of every `hit` roll on the frame.
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
            var cap = ctx.Frame.Value("defender_models", Lookup.LivingModels(ctx.State, ix.Defender!.UnitId).Count);
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = RollEffects.HitMultiplier(live, Side.Attacker, cap) };
            foreach (var (quality, attacks) in ByQuality(ctx.Frame.Values, "attacks_q"))
            {
                var plan = GfRolls.HitPlan(ctx, live, kind, quality);
                // One die's per-die effects; the attacks it adds are returned for the caller to roll.
                int Read(Die die)
                {
                    var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                    values["self_wounds"] = values.GetValueOrDefault("self_wounds") + effects.ExtraWounds;
                    if (!die.Success) return 0;
                    values["extra_hits"] += effects.ExtraHits;
                    if (GrantKey(effects.Grants) is { } key) values[key] = values.GetValueOrDefault(key) + 1;
                    return effects.ExtraAttacks;
                }
                var added = ctx.Roll(plan, attacks).Dice.Sum(Read);
                // "This rule doesn't apply to newly generated attacks": the added dice add no attacks of their own.
                if (added > 0)
                    foreach (var die in ctx.Roll(plan, added).Dice) Read(die);
            }
            RollEffects.ReadPerDie(live, Side.Attacker, GfNames.Hit);
            values["wounds"] = values.GetValueOrDefault("self_wounds");
            return new Complete(Consumed(live), values);
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
            var inCover = kind == AttackKind.Shooting
                && (parent.Values.TryGetValue("in_cover", out var settled) ? settled == 1
                    : InCover(ctx.State, live, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind)));
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
                var plan = GfRolls.DefensePlan(ctx, granted, inCover, defenderModels);
                var record = ctx.Roll(plan, group);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(granted, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
            }
            if (plain > 0)
            {
                var plan = GfRolls.DefensePlan(ctx, live, inCover, defenderModels);
                var record = ctx.Roll(plan, plain);
                wounds += Dice.RollOutcome.Failures(record);
                extraWounds += record.Dice.Sum(d => RollEffects.PerDie(live, Side.Defender, d, GfNames.DefenseRoll).ExtraWounds);
            }
            return new Complete(Consumed(live), new Dictionary<string, int> { ["wounds"] = wounds, ["extra_wounds"] = extraWounds });
        }
    }
}
