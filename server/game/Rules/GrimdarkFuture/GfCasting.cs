using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfCasting
{
    public static Sequence Casting() => new(GfNames.CastingSeq, [new PickSpellStep(), new TokenSpendStep(), new RollStep(), new ResultStep(), new TargetsStep(), new EffectsStep()]);
    /// <summary>take_test (spell library, kind 14): the target's morale test with p.10's ordinary consequence, then the row's children when it failed.</summary>
    public static Sequence SpellTest() => new(GfNames.SpellTestSeq,
        [new GfAttack.PushStep("test", GfNames.MoraleTest, ctx => new SequenceContext(ctx.Frame.Context.TargetUnitIds[0], [], [], "spell")), new ConsequenceStep()]);
    public static Sequence SpellHits() => new(GfNames.SpellHitsSeq, [new HitStep(), new GfAttack.ScoreHitsStep(finishWithoutHits: true), new GfAttack.PushStep("resolve", GfNames.ResolveHits), new GfAttack.SelfWoundsStep()]);

    /// <summary>
    /// The in-play reading, matching GfHooks.ReactionsAt: a caster inside a container may still cast at what is inside with it (p.15),
    /// and a Shaken unit casts nothing at all (p.10, spelled out in 3.5.1).
    /// </summary>
    public static IReadOnlyList<Guid> Casters(GameState state, Guid unit) =>
        state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken) ? []
            : Lookup.AliveModels(state, unit).Where(m => Lookup.ModelParameter(state, m, GfNames.Caster) is not null && state.Projection.Models[m].SpellTokens > 0).ToList();

    /// <summary>
    /// p.13: a caster may "spend as many tokens as the spell's value to try casting one or more spells (only one try per spell)": one option per
    /// caster model and spell of that model's own army book
    /// (ModelSlot.ArmyBook; "the Caster's spells must be picked from their own faction") it can afford and has not tried this activation. Empty means nothing to cast, so GfHooks.ReactionsAt offers no window.
    /// </summary>
    public static IReadOnlyList<AskOption> SpellOptions(GameState state, PropertySet props, Guid unit)
    {
        var tried = TriedPicks(state, unit);
        return Casters(state, unit).SelectMany(m => props.SpellsOf(state.Projection.Models[m].Slot.ArmyBook)
                .Where(e => !tried.Contains((e.Name, m)) && e.Cost <= state.Projection.Models[m].SpellTokens)
            .Select(e => new AskOption($"{e.Name}|{m}", $"{e.Name} ({e.Cost})", [m]))).ToList();
    }

    /// <summary>
    /// Spells picked by a closed pick-spell since this unit's activation began (OneTryPerSpellScope = activation). The activation is
    /// the unit's last root action sequence, which only the scheduler starts (R-N3): nested frames inherit the action name.
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

    /// <summary>A spell's targets from its caster model (Targets).</summary>
    public static IReadOnlyList<Guid> SpellTargets(StepContext ctx, Guid casterModel, CatalogEntry spell) => Targets(ctx.State, casterModel, spell.Targeting!);

    /// <summary>
    /// p.15 first: containment answers before range and sight, so no spell crosses a container's wall either way, and a caster inside
    /// one reaches the units in there with it and the container itself with nothing to measure. Otherwise a unit is a target when one
    /// of its models is within range of the caster model and in its line of sight (p.13), or within range alone when the targeting
    /// says so (a Buff: "pick one friendly unit within 12""). A model-grain targeting offers those models. Spells and pick entries share it.
    /// </summary>
    public static IReadOnlyList<Guid> Targets(GameState state, Guid casterModel, SpellTargeting t)
    {
        var casterUnit = state.Projection.Models[casterModel].UnitId; var team = Teams.Of(state, casterUnit);
        var candidates = state.Projection.Units.Values.Where(u => (Teams.Of(state, u.Id) == team) == (t.Side == TargetSide.Friendly)).Select(u => u.Id).ToList();
        var result = new List<Guid>();
        var sight = new Lazy<Sight>(() => Sight.From(state, casterUnit));                                            // built once for every candidate model
        foreach (var unit in candidates)
        {
            var reaches = Battlefield.InsideReach(state, casterUnit, unit);
            var models = reaches switch
            {
                true => Lookup.AliveModels(state, unit),
                false => [],
                null => Lookup.LivingModels(state, unit).Where(m => Battlefield.Distance(state, casterModel, m) <= t.Range
                    && (m == casterModel || t.LineOfSight == false || sight.Value.CanSee(casterModel, m))).ToList(),
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
                ctx.Applied.Add(GfNames.Caster);
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
            var (_, caster) = Picked(ctx.Frame);
            var casterUnit = ctx.Projection.Models[caster].UnitId;
            var live = ctx.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, casterUnit), null, null, null, null, Battlefield.TerrainUnder(ctx.State, casterUnit)));
            ctx.Roll(GfRolls.CastingPlan(ctx, live, ctx.Frame.Value("net_tokens")), 1);
            return new Complete(RollEffects.Consume(live));
        }
    }

    /// <summary>p.13: "on 4+ resolve the effect", read off the casting die as it now stands, so an edit made after `roll` counts (opr-sequences →
    /// Dice edits). A failure ends the casting.</summary>
    sealed class ResultStep : IStep
    {
        public string Name => "result";
        public StepOutcome Run(StepContext ctx)
        {
            var die = ctx.Frame.Frame.Steps.SelectMany(s => s.Rolls).LastOrDefault(r => r.Roll == GfNames.Casting);
            var passed = die is not null && Dice.RollOutcome.Successes(die) == 1;
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
            if (holding.Any(e => e.Kind == EffectKind.TakeTest) && ctx.History.Children.Count == 0)
                return new Push(GfNames.SpellTestSeq, new SequenceContext(casterUnit, [targetUnit], [], null));
            // p.13: "Effects from multiple instances of the same special rule or spell don't stack": a target already holding this spell's grants gets none.
            var already = ctx.Projection.Units[targetUnit].Grants.Any(g => g.EntryName == spell.Name);
            // A once-row's conditions say when the grant applies ("Shred when shooting"), not whether it is written: every once-row is granted.
            var changes = already ? [] : spell.Effects.Where(e => e.Duration == EffectDuration.Once)
                .Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, spell.Name, casterUnit, 1))).ToList();
            if (changes.Count > 0 || ctx.History.Children.Count > 0) ctx.Applied.Add(spell.Name);   // a cast spell names itself where it reached this target
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

    /// <summary>A spell's weapon rules for the hit interaction: the child rules of its add_hit rows that hold for this target, settled into the attacker's
    /// instances before pass one. Empty outside a casting frame.</summary>
    internal static IReadOnlyList<(string Entry, int? Parameter)> WeaponRules(StepContext ctx, Guid caster, Guid target)
    {
        for (var f = ctx.Frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.CastingSeq)
                return ctx.Properties.Entry(Picked(f).Spell) is { } spell
                    ? EffectsStep.Holding(ctx, caster, target, spell).Where(e => e.Kind == EffectKind.AddHit)
                        .SelectMany(e => e.Children.Where(c => c.Kind == EffectKind.GrantProperty).Select(c => (c.TargetEntry!, c.Amount))).ToList() : [];
        return [];
    }

    /// <summary>A hit roll that cannot fail: one die per hit on threshold 1 with RequiredRaw 1 (no test, so p.5's natural 1 does not fail it, and an
    /// edit judges it the same way), so "on rolls of 6" rules read a real die. What the dice decide is `score-hits`' (opr-sequences → Dice edits).</summary>
    sealed class HitStep : IStep
    {
        public string Name => "hit";
        public StepOutcome Run(StepContext ctx)
        {
            var spell = ctx.Properties.Entry(Picked(ctx.Frame.Parent!).Spell)!;
            var casterUnit = ctx.Frame.Context.ActorUnitId!.Value;
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var modelTarget = GfAttack.TakedownModel(ctx.Frame) is { } m && Lookup.AliveModels(ctx.State, target).Contains(m);
            var values = new Dictionary<string, int> { ["hit_multiplier"] = 1, ["hits"] = 0 };
            // The spell's weapon rules are part of this settled set (AttackInteraction), so every hit carries them and their pass-one rows act.
            var ix = GfAttack.AttackInteraction(ctx, casterUnit, target, [], AttackKind.Spell, null);
            var live = ctx.Settle(ix);
            var multiplier = 1; var rolled = false;
            ctx.Applied.Add(spell.Name);
            foreach (var hit in EffectsStep.Holding(ctx, casterUnit, target, spell).Where(e => e.Kind == EffectKind.AddHit))
            {
                var n = hit.EffectiveAmount(null);
                if (hit.TargetRoll is null) values["hits"] += n;
                else
                {
                    // "Roll as many dice as hits to see if "on rolls of 6" effects trigger": every die is a hit, so the roll cannot fail. It is
                    // no test, so p.5's natural 1 does not judge it: RequiredRaw 1 succeeds on every face.
                    ctx.Roll(new Dice.RollPlan(GfNames.Hit, 1, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, RequiredRaw: 1, new HashSet<int>()), n,
                        d => RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit));
                    rolled = true;
                }
                // Blast's cap is "as many hits as models in the target unit"; a model target is a unit of one.
                multiplier = Math.Max(multiplier, RollEffects.HitMultiplier(live, Side.Attacker, modelTarget ? 1 : Lookup.LivingModels(ctx.State, target).Count));
            }
            values["hit_multiplier"] = multiplier;
            // A granted multiplier on the caster is used up by these hits; a per-die rule is left for score-hits, which reads it.
            var spent = rolled ? GfDeferred.Leave(RollEffects.Consume(live), ix, l => RollEffects.ReadPerDie(l, Side.Attacker, GfNames.Hit)) : RollEffects.Consume(live);
            return new Complete(spent, values);
        }
    }

    sealed class ConsequenceStep : IStep
    {
        public string Name => "consequence";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            if (!Failed(ctx.State, ctx.Frame.Id)) return new Complete([]);
            var spell = ctx.Properties.Entry(Picked(ctx.Frame.Parent!).Spell)!;
            var children = spell.Effects.Where(e => e.Kind == EffectKind.TakeTest).SelectMany(e => e.Children).ToList();
            if (children.Count > 0) ctx.Applied.Add(spell.Name);
            var changes = children.Where(c => c.Kind == EffectKind.SetCondition)
                .Select(c => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, target), c.TargetCondition!, true)).ToList();
            if (children.FirstOrDefault(c => c.Kind == EffectKind.Move) is not { } move) return new Complete(changes);
            // Owner ruling: "Spells move as however that unit moves": the region and the apply read the target's own terrain rules (GfMoves.TerrainFor).
            if (ctx.History.LastClosed("move") is { } moved)
            {
                var (forced, values) = GfMoves.ApplyForcedMove(ctx, target, move.EffectiveAmount(null), GfAnswers.Moves(ctx.State, moved));
                return new Complete([.. changes, .. forced], values.Count > 0 ? values : null);
            }
            return new OpenAsks([ctx.Ask("move", $"Move up to {move.EffectiveAmount(null)}\" in a straight line", ctx.ActorTeam!.Value,
                GfMoves.ForcedMoveRegion(ctx, target, move.EffectiveAmount(null)), allowPass: true)]);
        }

        /// <summary>The morale test this frame pushed failed: its consequence step recorded passed = 0. A test that finished with no model to test records nothing and does not count as failed.</summary>
        static bool Failed(GameState state, Guid frameId)
        {
            var log = EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).ToList();
            var mine = log.FindIndex(p => p is SequenceStarted s && s.FrameId == frameId);
            var child = log.Skip(mine + 1).OfType<SequenceStarted>().FirstOrDefault(s => s.Sequence == GfNames.MoraleTest);
            if (child is null) return false;
            var done = log.OfType<StepCompleted>().LastOrDefault(s => s.FrameId == child.FrameId && s.Step == "consequence");
            return done?.Values is { } v && v.GetValueOrDefault("passed", 1) == 0;
        }
    }
}
