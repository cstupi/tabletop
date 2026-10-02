# Task 11: Grimdark Future's rolls, names, hooks and the battlefield reader (continued from part 1)

- [ ] **Step 4: Write `GfRolls.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>OPR's dice: 1D6 against a target number, never clamped. Thresholds are functions of the live set; fatigue and cover are read here.</summary>
public static class GfRolls
{
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, GfNames.Quality), Defense = new(StatLevel.Unit, GfNames.Defense);
    static readonly StatRef ModelQuality = new(StatLevel.Model, GfNames.Quality), ModelDefense = new(StatLevel.Model, GfNames.Defense);

    /// <summary>
    /// The default threshold for each roll, which is the unit's number. Three of them are overridden per plan where the rulebook
    /// makes the target number a model's: HitQuality for the attacking model (p.4), DefensePlan for a lone joined hero (p.14) and
    /// MoralePlan for a hero testing on the unit's behalf (p.14). A caller that names no threshold gets what is here.
    /// </summary>
    public static RollSpec Spec(string roll) => roll switch
    {
        GfNames.Hit => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.DefenseRoll => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Defense)),
        GfNames.Morale => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.Casting => new(roll, D6, (_, _) => 4),
        GfNames.RollOff => new(roll, D6, (_, _) => null),
        _ => new(roll, D6, (_, _) => null),   // a property's own roll (Regeneration, Impact, Fearless) carries its threshold in the effect
    };

    /// <summary>p.9: after attacking in melee for the first time in a round, a unit hits only on unmodified 6s until the round ends. p.10: a Shaken unit "may strike back counting as fatigued".</summary>
    public static bool Fatigued(StepContext ctx, LiveSet live, AttackKind kind) =>
        kind == AttackKind.Melee && ctx.Projection.Units[live.Interaction.Attacker.UnitId].Conditions is var c && (c.Contains(GfNames.Fatigued) || c.Contains(GfNames.Shaken));

    /// <summary>
    /// The Quality value one attacking model tests against. p.4: a Quality test succeeds on "the model's Quality value or higher",
    /// so the value is the model's, and a slot that declares its own is a joined hero (p.14). Every other slot
    /// falls back to the unit's, so an ordinary squad has one value. Effects on the roll's target number are already folded in,
    /// which is what puts Reliable's 2+ over a hero's own 3+.
    ///
    /// p.9's fatigue replaces the value rather than modifying it, and it is the whole unit's condition, so a fatigued melee has one
    /// value for every model and one pool however many Qualities the unit holds.
    /// </summary>
    public static int HitQuality(StepContext ctx, LiveSet live, AttackKind kind, Guid modelId) =>
        Fatigued(ctx, live, kind) ? 6 : StatReader.TargetNumber(live, Side.Attacker, ModelQuality, modelId);

    /// <summary>
    /// One pooled hit roll for one Quality value. `determine-attacks` splits the group's attacks by `HitQuality` and `roll-to-hit`
    /// asks for a plan per distinct value, which is p.4's *"we recommend having dice of multiple colors ... and then roll them all
    /// at once"* for the case a joined hero creates: a hero and its squad carrying one weapon type that `fire-groups`
    /// merged into a single group.
    ///
    /// A fatigued melee is a flat 6+ with no modifiers and `RequiredRaw = 6`, because p.9 says *"only hit on unmodified rolls of 6"*.
    /// </summary>
    public static RollPlan HitPlan(StepContext ctx, LiveSet live, AttackKind kind, int quality) =>
        Fatigued(ctx, live, kind)
            ? new RollPlan(GfNames.Hit, 6, [], D6, RequiredRaw: 6, RerollRaw: new HashSet<int>())
            : ctx.Plan(live, Side.Attacker, GfNames.Hit, [], threshold: quality);

    /// <summary>
    /// p.11's *"If the majority of models in a unit are fully inside a piece of cover terrain or behind a sight blocker"*: one
    /// majority over the models that pass either test, so a unit with one model in a wood and another screened by a third unit's
    /// base is in cover at three models. A majority is strictly more than half, so five of ten is not one.
    /// The terrain test follows the book's wording for the unit's size: whole base inside ("fully inside") for a
    /// multi-model unit, base centre inside (standing for "mostly inside") for a single-model unit, sized by starting size so a
    /// squad reduced to one model is still judged as a squad. The sight test is the same for both sizes: every attacking model
    /// with a clear line to the model has a sight blocker along it, or none sees it at all (`Battlefield.ModelsBehindSightBlockers`).
    /// p.14 Indirect *"ignores cover from sight obstructions"*, which is the sight test only: `ignoresSightObstructions` drops it and
    /// the terrain test stands. Blast's *"Ignores cover"* (p.13) is the whole modifier and is suppressed in the roll instead.
    /// The fraction is a ruleset number, so it is here and `Battlefield` only lists.
    /// </summary>
    public static bool InCover(GameState state, Guid targetUnit, IReadOnlyList<Guid> attackerModels, bool ignoresSightObstructions)
    {
        var models = Lookup.LivingModels(state, targetUnit).Count;
        if (models == 0) return false;
        var covered = Battlefield.ModelsInCoverTerrain(state, targetUnit, wholeBase: GfHooks.StartingSize(state, targetUnit) > 1).ToHashSet();
        if (covered.Count * 2 > models || ignoresSightObstructions) return covered.Count * 2 > models;
        covered.UnionWith(Battlefield.ModelsBehindSightBlockers(state, targetUnit, attackerModels));
        return covered.Count * 2 > models;
    }

    /// <summary>
    /// p.11: +1 to Defense against shooting when in cover. Blast suppresses it (pass one); Indirect's half is read in `InCover`.
    ///
    /// p.14's Hero clause is the other half of the threshold. A joined hero *"must use the unit's Defense until all other models have
    /// been killed"*, so the unit's value stands while anything else lives, and once the hero is alone its own slot Defense is read.
    /// `defenderModels` is that clause's *all other models*, and it is the count `fire-groups` settled before any group fired rather
    /// than a live one: p.8 fires a unit's weapons simultaneously, so a second group must not find the hero alone that
    /// the first group left it. A model without a slot Defense reads the unit's through the same call, so nothing branches on `Hero`.
    /// </summary>
    public static RollPlan DefensePlan(StepContext ctx, LiveSet live, bool inCover, int defenderModels)
    {
        var living = Lookup.LivingModels(ctx.State, live.Interaction.Defender!.UnitId);
        var alone = defenderModels == 1 && living.Count == 1 ? living[0] : (Guid?)null;
        return ctx.Plan(live, Side.Defender, GfNames.DefenseRoll,
            live.Interaction.Attack == AttackKind.Shooting && inCover ? [new RollModifier("cover", 1)] : [],
            threshold: alone is { } id ? StatReader.TargetNumber(live, Side.Defender, ModelDefense, id) : null);
    }

    /// <summary>
    /// p.10: one Quality test. p.14 lets a joined hero *"take morale tests on behalf of the unit"*, and that clause is resolved
    /// always-when-it-helps, so the threshold is the best of the unit's Quality and every living Hero's own and no Ask
    /// is opened. Nothing else of the test moves: p.14's *"even if the hero is the last model remaining, it takes morale tests as the
    /// unit"* keeps the starting-size comparison that decides Shaken from Rout on the unit.
    /// </summary>
    public static RollPlan MoralePlan(StepContext ctx, LiveSet live)
    {
        var unit = live.Interaction.Attacker.UnitId;
        var best = Lookup.LivingModels(ctx.State, unit)
            .Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero))
            .Select(m => StatReader.TargetNumber(live, Side.Attacker, ModelQuality, m))
            .Append(StatReader.TargetNumber(live, Side.Attacker, Quality)).Min();
        return ctx.Plan(live, Side.Attacker, GfNames.Morale, [], threshold: best);
    }

    /// <summary>p.13: one die on 4+, ±1 per token spent for or against.</summary>
    public static RollPlan CastingPlan(StepContext ctx, int netTokens) =>
        new(GfNames.Casting, 4, netTokens == 0 ? [] : [new RollModifier("tokens", netTokens)], D6, null, new HashSet<int>());

    /// <summary>
    /// The roll-off the scheduler starts (scheduler → Deployment; p.6, p.50). Two steps, because the boundary between them is the confirm
    /// gate that keeps the frame on the stack while a player edits the dice, and because
    /// dice_edited and dice_rerolled reach back into a RollRecord and never rewrite a step_completed's Values — so the ranking has to be
    /// re-derived after the gate rather than written by the step that rolled.
    /// </summary>
    public static Sequence RollOffSequence() => new(GfNames.RollOffSeq, [new RollOffStep(), new RollOffOrderStep()]);

    /// <summary>
    /// One die per team in ordinal order, no threshold; teams whose results are still tied roll again among themselves — roll k's dice
    /// belong, in ordinal order, to the teams tied after rolls 0..k-1 — until every team is ordered, because free-for-all activates
    /// "in descending order" and needs the whole order, not a winner.
    /// </summary>
    sealed class RollOffStep : IStep
    {
        public string Name => GfNames.RollOffRollStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var plan = new RollPlan(GfNames.RollOff, null, [], D6, null, new HashSet<int>());
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            for (var round = 0; round < 100; round++)
            {
                var tied = Tied(teams, keys);
                if (tied.Count == 0) break;
                var record = ctx.Roll(plan, tied.Count);
                for (var i = 0; i < tied.Count; i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            return new Complete([]);
        }
    }

    /// <summary>
    /// Ranks the teams from the dice the previous step recorded, replaying the same tie loop over them instead of rolling, so a die
    /// edited or rerolled at the gate is the one that counts. It never rolls: a residual tie an edit created falls to team ordinal, which
    /// is the tolerance Scheduler.RollOff.Order already has for a hand-edited log.
    /// Values: winner_ordinal, and place_1..place_n — the team ordinals best first — which Scheduler.RollOff.Order reads into round_started.Order.
    /// </summary>
    sealed class RollOffOrderStep : IStep
    {
        public string Name => GfNames.RollOffOrderStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            foreach (var record in ctx.Frame.Frame.Steps.Single(s => s.Step == GfNames.RollOffRollStep).Rolls)
            {
                var tied = Tied(teams, keys);
                for (var i = 0; i < Math.Min(tied.Count, record.Dice.Count); i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            var ranked = teams.OrderByDescending(t => keys[t.Id], KeyComparer.Instance).ThenBy(t => t.Ordinal).ToList();
            var values = new Dictionary<string, int>();
            if (ranked.Count > 0) values["winner_ordinal"] = ranked[0].Ordinal;
            for (var k = 0; k < ranked.Count; k++) values[$"place_{k + 1}"] = ranked[k].Ordinal;
            return new Complete([], values);
        }
    }

    /// <summary>The teams still sharing a result key with another team, in ordinal order: whose dice the next roll-off roll holds.</summary>
    static List<Team> Tied(List<Team> teams, Dictionary<Guid, List<int>> keys) =>
        teams.Where(t => teams.Any(o => o.Id != t.Id && keys[o.Id].SequenceEqual(keys[t.Id]))).ToList();

    /// <summary>Lexicographic over each team's raw results; a longer key (more tie-break rolls) ranks above its prefix.</summary>
    sealed class KeyComparer : IComparer<List<int>>
    {
        public static readonly KeyComparer Instance = new();
        public int Compare(List<int>? a, List<int>? b)
        {
            for (var i = 0; i < Math.Min(a!.Count, b!.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}
```

- [ ] **Step 5: Write `GfHooks.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The hooks the scheduler calls, and who may react at a window. Every reading is of the projection and the log, never of a flag.</summary>
public static class GfHooks
{
    /// <summary>Round boundary: fatigue clears (action sequences → Fatigue); every Caster(X) gets X tokens, capped at 6, on the table or not (p.13).</summary>
    public static IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet props)
    {
        var changes = new List<StateChange>();
        foreach (var u in state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Fatigued)))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Fatigued, false));
        // p.13 gives tokens "even if they are not on the table" and says nothing of casualties; a dead model's tokens are never spent because ReactionsAt only counts AliveModels.
        foreach (var m in state.Projection.Models.Values)
            if (Lookup.ModelParameter(state, m.Id, GfNames.Caster) is { } x && x > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + x)));
        return changes;
    }

    /// <summary>turn_ended: the wounds-this-activation counters and moved clear (whole-design review → Gaps filled); an activation spent idle stops the unit being Shaken (p.10).</summary>
    public static IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet props, Scheduler.TurnContext turn)
    {
        var changes = state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0)).ToList();
        // moved is "moved this activation", which is what p.14 Indirect's "shooting after moving" reads.
        changes.AddRange(state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Moved)).Select(u => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Moved, false)));
        // p.10: "Shaken units must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation."
        // p.13: an Aircraft "may only use Advance actions … (even if Shaken)" and "their mandatory move doesn't break the staying idle requirement
        // to stop being Shaken" — so a Shaken Aircraft's Advance counts as having idled.
        if (turn.Activation is { } act && state.Projection.Units[act.UnitId].Conditions.Contains(GfNames.Shaken)
            && (act.Action == Scheduler.ActivationOption.Idle || (act.Action == GfNames.Advance && Lookup.UnitHas(state, props, act.UnitId, GfNames.Aircraft))))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, act.UnitId), GfNames.Shaken, false));
        return changes;
    }

    /// <summary>p.10: at the end of an activation, a unit that took wounds leaving it at half or less tests; units that fought this melee compared wounds instead. The actor bounds "this activation".</summary>
    public static IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet props, Guid? actor) =>
        UnitsToTestAtEndOfActivation(state, props, actor).Select(u => new SequenceStart(GfNames.MoraleTest, new SequenceContext(u, [], [], "wounds"))).ToList();

    public static IReadOnlyList<Guid> UnitsToTestAtEndOfActivation(GameState state, PropertySet props, Guid? actor) =>
        state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0 && AtHalfOrLess(state, props, u.Id) && !FoughtMeleeThisActivation(state, actor, u.Id) && Lookup.AliveModels(state, u.Id).Count > 0)
            .Select(u => u.Id).ToList();

    public static int StartingSize(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models.Count;

    /// <summary>Half or less of starting size, or of the Tough value for a single-model unit (p.10). Counted over the models still in play: a unit inside a transport has not lost a model by being carried.</summary>
    public static bool AtHalfOrLess(GameState state, PropertySet props, Guid unitId)
    {
        var start = StartingSize(state, unitId);
        if (start == 1)
        {
            var model = state.Projection.Units[unitId].Definition.Models[0];
            var tough = Lookup.ModelParameter(state, model.Id, GfNames.Tough) ?? 1;
            var remaining = state.Projection.Models[model.Id].Location is not OnShelf ? tough - state.Projection.Models[model.Id].Wounds : 0;
            return remaining * 2 <= tough;
        }
        return Lookup.AliveModels(state, unitId).Count * 2 <= start;
    }

    /// <summary>A melee sequence started since the actor's activation began, with this unit as charger or target. No activation frame in the log means no melee this activation — never a scan of the whole log, which would report a melee from an earlier round.</summary>
    public static bool FoughtMeleeThisActivation(GameState state, Guid? actor, Guid unitId)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        // Anchored on the four root sequences, which only the scheduler starts: nested frames inherit the action name in their context.
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && (actor is null || s.Context.ActorUnitId == actor));
        if (activation < 0) return false;
        return log.Skip(activation).Any(e => e.Payload is SequenceStarted { Sequence: GfNames.Melee } m && (m.Context.ActorUnitId == unitId || m.Context.TargetUnitIds.Contains(unitId)));
    }

    /// <summary>
    /// The one core window: before attacking, the acting unit's own team may cast if a model in it has tokens (p.13). Task 17 supplies the sequence.
    /// A Shaken unit casts nothing: 3.5.1 p.10 spells out that being idle means it "can't take any actions, or use any active special
    /// rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc.".
    /// </summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (point != GfNames.BeforeAttacking || frame.Context.ActorUnitId is not { } unit) return [];
        if (state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return [];   // p.10: no active special rules while Shaken
        var casters = Lookup.AliveModels(state, unit).Where(m => state.Projection.Models[m].SpellTokens > 0 && Lookup.ModelParameter(state, m, GfNames.Caster) is not null).ToList();   // p.15: a caster inside a container may still cast at what is inside with it
        if (casters.Count == 0 || Teams.Of(state, unit) is not { } team) return [];
        return [new Reaction(team, "cast", "Cast a spell", GfNames.CastingSeq, new SequenceContext(unit, [], [], null))];
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfRollsTests`; expect 16 passed. Commit: `Add Grimdark Future's roll specs, hooks, names and the battlefield reader`.**
