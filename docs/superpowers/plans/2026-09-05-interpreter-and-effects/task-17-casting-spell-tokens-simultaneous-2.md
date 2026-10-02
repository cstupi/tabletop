# Task 17: Casting — spell and tokens, the simultaneous spend, the roll, targets, effects, and once-grants (continued from part 1)

- [ ] **Step 2: Run; expect compile errors. Write `GfCasting.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfCasting
{
    public static Sequence Casting() => new(GfNames.CastingSeq, [new PickSpellStep(), new TokenSpendStep(), new RollStep(), new TargetsStep(), new EffectsStep()]);
    public static Sequence SpellHits() => new(GfNames.SpellHitsSeq, [new HitStep(), new GfAttack.PushStep("resolve", GfNames.ResolveHits)]);

    /// <summary>
    /// The in-play reading, matching GfHooks.ReactionsAt: a caster inside a container may still cast at what is inside with it (p.15),
    /// and a Shaken unit casts nothing at all (p.10, spelled out in 3.5.1).
    /// </summary>
    public static IReadOnlyList<Guid> Casters(GameState state, Guid unit) =>
        state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken) ? []
            : Lookup.AliveModels(state, unit).Where(m => Lookup.ModelParameter(state, m, GfNames.Caster) is not null && state.Projection.Models[m].SpellTokens > 0).ToList();

    /// <summary>
    /// p.13: a caster may "spend as many tokens as the spell's value to try casting one or more spells (only one try per spell)": one option per
    /// caster model and spell it can afford and has not tried this activation. Empty means nothing to cast, so GfHooks.ReactionsAt offers no window.
    /// </summary>
    public static IReadOnlyList<AskOption> SpellOptions(GameState state, PropertySet props, Guid unit)
    {
        var tried = TriedPicks(state, unit);
        return Casters(state, unit).SelectMany(m => props.Entries.Where(e => e.Kind == CatalogKind.Spell && !tried.Contains((e.Name, m)) && e.Cost <= state.Projection.Models[m].SpellTokens)
            .Select(e => new AskOption($"{e.Name}|{m}", $"{e.Name} ({e.Cost})", [m]))).ToList();
    }

    /// <summary>
    /// Spells picked by a closed pick-spell since this unit's activation began (OneTryPerSpellScope = activation). The activation is
    /// the unit's last root action sequence, which only the scheduler starts: nested frames inherit the action name.
    /// </summary>
    public static IReadOnlyList<string> TriedThisActivation(GameState state, Guid unit) =>
        TriedPicks(state, unit).Where(t => state.Projection.Models.TryGetValue(t.Caster, out var m) && m.UnitId == unit).Select(t => t.Spell).ToList();

    /// <summary>
    /// The (spell, caster model) of every closed pick-spell since this unit's activation began. p.13's "only one try per spell" is a
    /// caster's own, so pick-spell hides a spell from a model only when that model picked it; the log after the activation's start
    /// also holds other units' picks, which this leaves to their casters.
    /// </summary>
    static IReadOnlyList<(string Spell, Guid Caster)> TriedPicks(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s && s.Context.ActorUnitId == unit);
        return log.Skip(Math.Max(0, since)).Select(e => e.Payload).OfType<StepCompleted>().Where(s => s.Step == "pick-spell" && s.Answer is Chosen)
            .Select(s => Pick(((Chosen)s.Answer!).OptionId)).OfType<(string, Guid)>().ToList();
    }

    /// <summary>A pick-spell option id, "&lt;spell&gt;|&lt;caster model&gt;": a caster id never holds '|' and a spell name may, so the split is at the last one. Null when the id is not of that form.</summary>
    static (string Spell, Guid Caster)? Pick(string optionId)
    {
        var cut = optionId.LastIndexOf('|');
        return cut > 0 && Guid.TryParse(optionId.AsSpan(cut + 1), out var caster) ? (optionId[..cut], caster) : null;
    }

    /// <summary>
    /// p.13's caps, grouped by the team that owns them: every eligible model keyed to the tokens it holds, which is one team's slice of
    /// TokenSpend.MaxByModel, and whose keys are that team's own models for the spend step's ownership check. Called from spend-tokens,
    /// so the caster's entry is already net of the spell's cost.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> SpendCapacity(GameState state, Guid casterModel) =>
        EligibleSpenders(state, casterModel).GroupBy(m => Teams.Of(state, m.UnitId)).Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyDictionary<Guid, int>)g.ToDictionary(m => m.Id, m => m.SpellTokens));

    /// <summary>
    /// The per-model eligibility test itself, the caster first and the rest nearest the caster's unit next — an order the log and the Ask
    /// list their models in, and nothing more, since the player names its own payers. The caster's own model is an entry whenever it
    /// still holds a token: p.13 does not exclude it, and it is admitted without measurement, being a model of the caster's own unit.
    /// That is the one exception to a test positional on both ends, and it is deliberate: an occupant has no line of sight out (p.15),
    /// so for a caster inside a container `mine` is empty and no other model's tokens reach the roll, while its own still do. A model
    /// inside a container never spends on a cast outside.
    /// </summary>
    public static IReadOnlyList<ModelInPlay> EligibleSpenders(GameState state, Guid casterModel)
    {
        var self = state.Projection.Models[casterModel];
        var mine = Lookup.LivingModels(state, self.UnitId);
        var spenders = new List<ModelInPlay>();
        if (self.SpellTokens > 0) spenders.Add(self);
        var sights = new Dictionary<Guid, Sight>();                                                                // one per spending unit, not per pair
        Sight From(Guid unit) => sights.TryGetValue(unit, out var s) ? s : sights[unit] = Sight.From(state, unit);
        spenders.AddRange(state.Projection.Models.Values.Where(m => m.Id != casterModel && m.Location is OnTable && m.SpellTokens > 0
                && mine.Any(c => Battlefield.Distance(state, c, m.Id) <= GfNames.CastRange && From(m.UnitId).CanSee(m.Id, c)))
            .OrderBy(m => mine.Min(c => Battlefield.Distance(state, c, m.Id))));
        return spenders;
    }

    /// <summary>
    /// p.15 first: containment answers before range and sight, so no spell crosses a container's wall either way, and a caster inside
    /// one reaches the units in there with it and the container itself with nothing to measure. Otherwise a unit is a target when one
    /// of its models is within the spell's range of the caster model and in its line of sight (p.13). A model-grain spell offers those models.
    /// </summary>
    public static IReadOnlyList<Guid> SpellTargets(StepContext ctx, Guid casterModel, CatalogEntry spell)
    {
        var t = spell.Targeting!; var casterUnit = ctx.Projection.Models[casterModel].UnitId; var team = ctx.TeamOf(casterUnit);
        var candidates = ctx.Projection.Units.Values.Where(u => (ctx.TeamOf(u.Id) == team) == (t.Side == TargetSide.Friendly)).Select(u => u.Id).ToList();
        var result = new List<Guid>();
        var sight = new Lazy<Sight>(() => Sight.From(ctx.State, casterUnit));                                        // built once for every candidate model
        foreach (var unit in candidates)
        {
            var reaches = Battlefield.InsideReach(ctx.State, casterUnit, unit);
            var models = reaches switch
            {
                true => Lookup.AliveModels(ctx.State, unit),
                false => [],
                null => Lookup.LivingModels(ctx.State, unit).Where(m => Battlefield.Distance(ctx.State, casterModel, m) <= t.Range && (m == casterModel || sight.Value.CanSee(casterModel, m))).ToList(),
            };
            if (models.Count == 0) continue;
            if (t.Grain == TargetGrain.Unit) result.Add(unit); else result.AddRange(models);
        }
        return result;
    }

    /// <summary>pick-spell completes only on an offered option, which Pick always parses.</summary>
    static (string Spell, Guid Caster) Picked(FrameView casting) => Pick(((Chosen)casting.Frame.Steps[0].Answer!).OptionId) ?? throw new InvalidOperationException("pick-spell recorded an option it did not offer");

    sealed class PickSpellStep : IStep
    {
        public string Name => "pick-spell";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is { } closed)
            {
                // A pass, another type of answer or an id the Ask did not offer casts nothing and spends nothing.
                if (GfAnswers.Option(closed) is not { } picked || Pick(picked.Id) is not var (spellName, caster)) return new Finish([]);
                var cost = ctx.Properties.Entry(spellName)?.Cost ?? 0;
                // The cost is spent here, and spend-tokens computes the caps after this change is applied, so the caster is offered what is left.
                return new Complete([new SpellTokensSet(caster, Math.Max(0, ctx.Projection.Models[caster].SpellTokens - cost))]);
            }
            var options = SpellOptions(ctx.State, ctx.Properties, unit);
            return options.Count == 0 ? new Finish([]) : new OpenAsks([ctx.Ask("cast", "Cast which spell?", ctx.ActorTeam!.Value, new Options(options))]);
        }
    }

    sealed class TokenSpendStep : IStep
    {
        public string Name => "spend-tokens";
        public StepOutcome Run(StepContext ctx)
        {
            var (_, caster) = Picked(ctx.Frame);
            var caps = SpendCapacity(ctx.State, caster);   // after pick-spell's deduction: the caster's own cap is what the cost left it
            if (Interpreter.Windows.SimultaneousSpend(ctx, GfNames.CastingSeq, caps, "Spend tokens for or against the casting roll") is { } open) return open;
            var own = ctx.ActorTeam; var net = 0; var changes = new List<StateChange>();
            foreach (var (team, counts) in Interpreter.Windows.Spends(ctx, GfNames.CastingSeq, caps))
            {
                net += (team == own ? 1 : -1) * counts.Values.Sum();
                foreach (var (model, tokens) in counts) changes.Add(new SpellTokensSet(model, ctx.Projection.Models[model].SpellTokens - tokens));
            }
            return new Complete(changes, new Dictionary<string, int> { ["net_tokens"] = net });
        }
    }

    sealed class RollStep : IStep
    {
        public string Name => "roll";
        public StepOutcome Run(StepContext ctx)
        {
            var passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.CastingPlan(ctx, ctx.Frame.Value("net_tokens")), 1)) == 1;
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0 };
            return passed ? new Complete([], values) : new Finish([], values);
        }
    }

    sealed class TargetsStep : IStep
    {
        public string Name => "targets";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var (spellName, caster) = Picked(ctx.Frame);
            var spell = ctx.Properties.Entry(spellName)!;
            var eligible = SpellTargets(ctx, caster, spell);
            if (eligible.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("spell_targets", $"{spellName}: pick up to {spell.Targeting!.Count}", ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, Math.Min(spell.Targeting.Count, eligible.Count)))]);
        }
    }
```

```csharp
    /// <summary>One target per run (Repeat): hits push spell-hits; once-effects write grants. Each target gets the effect independently (spell effects → Target selection).</summary>
    sealed class EffectsStep : IStep
    {
        public string Name => "effects";
        public StepOutcome Run(StepContext ctx)
        {
            var (spellName, _) = Picked(ctx.Frame);
            var spell = ctx.Properties.Entry(spellName)!;
            var casterUnit = ctx.Frame.Context.ActorUnitId!.Value;
            var chosen = GfAnswers.Pieces(ctx.State, ctx.Frame, "targets", "spell_targets");
            var index = ctx.Frame.Value("target_index");
            if (index >= chosen.Count) return new Complete([]);
            var target = chosen[index];
            var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
            var holding = Holding(ctx, casterUnit, targetUnit, spell);
            var hits = holding.Where(e => e.Kind == EffectKind.AddHit && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).ToList();
            if (hits.Count > 0 && ctx.History.Children.Count == 0) return new Push(GfNames.SpellHitsSeq, new SequenceContext(casterUnit, [targetUnit], [], null));
            var changes = holding.Where(e => e.Duration == EffectDuration.Once)
                .Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, e.TargetEntry ?? spell.Name, casterUnit, 1))).ToList();
            return new Complete(changes, new Dictionary<string, int> { ["target_index"] = index + 1 }, Repeat: index + 1 < chosen.Count);
        }

        /// <summary>The spell's effects whose own conditions hold for this target, in order; both the effects step and the hit step read it.</summary>
        internal static List<Effect> Holding(StepContext ctx, Guid casterUnit, Guid targetUnit, CatalogEntry spell) =>
            spell.Effects.Where(e => Holds(ctx, casterUnit, targetUnit, spell, e)).OrderBy(e => e.Ordinal).ToList();

        /// <summary>The spell's own conditions, judged with the caster as the holder and the target unit opposite (no roll, so a per-die condition waits for its die).</summary>
        static bool Holds(StepContext ctx, Guid casterUnit, Guid targetUnit, CatalogEntry spell, Effect effect)
        {
            if (effect.Conditions.Count == 0) return true;
            var ix = GfAttack.AttackInteraction(ctx, casterUnit, targetUnit, [], null, null);
            return effect.Conditions.All(c => c.Kind == ConditionKind.RollResult || Conditions.Holds(ix, Side.Attacker, c, new LiveProperty(Side.Attacker, spell, null, null, null, null), null));
        }
    }

    /// <summary>A hit roll that cannot fail: one die per hit on threshold 1, so "on rolls of 6" rules read a real die; the hits' nested grants become granted_* values.</summary>
    sealed class HitStep : IStep
    {
        public string Name => "hit";
        public StepOutcome Run(StepContext ctx)
        {
            var spell = ctx.Properties.Entry(Picked(ctx.Frame.Parent!).Spell)!;
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = 1 };
            var total = 0;
            var casterUnit = ctx.Frame.Context.ActorUnitId!.Value;
            foreach (var hit in EffectsStep.Holding(ctx, casterUnit, ctx.Frame.Context.TargetUnitIds[0], spell).Where(e => e.Kind == EffectKind.AddHit))
            {
                var n = hit.EffectiveAmount(null);
                var record = ctx.Roll(new Dice.RollPlan(GfNames.Hit, 1, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
                total += Dice.RollOutcome.Successes(record);
                foreach (var grant in hit.Children.Where(c => c.Kind == EffectKind.GrantProperty)) { var key = $"granted_{grant.TargetEntry}_{grant.Amount}"; values[key] = values.GetValueOrDefault(key) + n; }
            }
            return total == 0 ? new Finish([], values) : new Complete([], values);
        }
    }
}
```

`spell-hits`'s `resolve` pushes `resolve-hits` with `ctx.Frame.Context` (caster unit, target unit, no weapons), and `RollToBlockStep` reads `HitsFrom(parent)` from the `spell-hits` frame's hit roll and `granted_AP_1` from its values; `AttackInteraction` with no weapons takes the caster unit's living models as the party, so the spell's hits carry the caster's unit properties and the granted `AP(1)`, nothing else. `GfRules._sequences` gains `GfCasting.Casting()` and `GfCasting.SpellHits()`.

- [ ] **Step 3: Grant consumption — add to `RollEffects.cs` and use it in `GfAttack`**

```csharp
    /// <summary>A once-grant is consumed the first time its effect applies: every granted instance whose entry contributed a modifier to this plan, with one use left.</summary>
    // A grant is matched to a modifier by entry name: the grant's entry and the modifier's source are the same string.
    public static IReadOnlyList<StateChange> ConsumeGrants(LiveSet live, Side side, RollPlan plan) =>
        live.On(side).Where(p => p.GrantId is not null && plan.Modifiers.Any(m => m.Source == p.Entry.Name))
            .Select(p => (StateChange)new GrantRemoved(live.Interaction.Of(side).UnitId, p.GrantId!.Value)).ToList();
```

`GfAttack` gains the one place a step's grants are collected, because a granted rule may sit on either unit and modify the other's roll, and the same instance contributes to every pool a step rolled:

```csharp
    static IReadOnlyList<StateChange> Consumed(LiveSet live, IEnumerable<Dice.RollPlan> plans) =>
        plans.SelectMany(plan => new[] { Side.Attacker, Side.Defender }.SelectMany(side => RollEffects.ConsumeGrants(live, side, plan)))
            .Cast<GrantRemoved>().DistinctBy(c => c.GrantId).Cast<StateChange>().ToList();
```

`RollToHitStep` collects every pool's plan and returns `new Complete(Consumed(live, plans), values)`, and `RollToBlockStep` does the same over every defense roll it throws, so one `GrantRemoved` is written per instance however many pools it modified. `AssignStep` (Task 12) takes the one live `Regeneration` roll, a granted instance included, since instances of a rule without a parameter do not stack; it gains a `rolled` flag, set when that roll is taken, and `if (rolled && regen.Source?.GrantId is { } grantId) changes.Add(new GrantRemoved(target, grantId));` follows the `ModelsMoved` change, so a granted instance is spent when it rolls. `GrantAdded` carries no parameter; `Lookup.UnitProperties` reads the granting effect's `Amount` (Task 6), which is null for `Ward`'s `Regeneration` and `1` for a *"gets AP(1)"* grant.

- [ ] **Step 4: Gate the casting window on `SpellOptions`**

Task 11's `GfHooks.ReactionsAt` opened the window for any caster holding tokens; with no spell it can afford and has not tried, `pick-spell` finishes empty and the window reopens without end. Replace its caster check:

```csharp
    /// <summary>
    /// The one core window: before attacking, the acting unit's own team may cast if a model in it can afford a spell it has not tried (p.13). Task 17 supplies the sequence.
```

```csharp
        // p.13: casting is spending tokens on a spell, so the window opens only when some caster can afford a spell it has not tried.
        // p.15: a caster inside a container may still cast at what is inside with it.
        if (GfCasting.SpellOptions(state, props, unit).Count == 0 || Teams.Of(state, unit) is not { } team) return [];
```

Append to `GfRollsTests`:

```csharp
    [Fact]
    public void Cast_is_offered_only_when_a_caster_can_afford_a_spell_it_has_not_tried()
    {
        var fireball = new EntryDocument("Fireball", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits.",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), []);
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var state = State([wizard], [Unit("T", "4+", "4+", [], 1, [])]);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(wizard.Id, [], [], GfNames.Hold)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        state.Applier.Apply(new GameEvent(2, null, new SpellTokensSet(wizard.Models[0].Id, 3), null));
        Assert.Empty(GfHooks.ReactionsAt(state, Core, GfNames.BeforeAttacking, frame));                                      // tokens but no spell in the set
        Assert.Single(GfHooks.ReactionsAt(state, TestProperties.WithSpells(fireball), GfNames.BeforeAttacking, frame));
        state.Applier.Apply(new GameEvent(3, null, new SpellTokensSet(wizard.Models[0].Id, 1), null));
        Assert.Empty(GfHooks.ReactionsAt(state, TestProperties.WithSpells(fireball), GfNames.BeforeAttacking, frame));       // one token, a two-token spell
    }
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "GfCastingTests|GfRollsTests"`; expect 13 casting and 17 rolls passed, and the whole project still green. Commit: `Add casting: spell and tokens, the simultaneous spend, the roll, targets, hits that cannot miss, once-grants`.**
