using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMelee
{
    /// <summary>Outline labels: p.9 ("must pick one valid target"; Charge moves; the target's models "must move by up to 3”"; The melee sequence).</summary>
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new GfMoves.PointWindowStep(GfNames.ActivationStart, "activation-start"), new ChargeTargetStep().Labelled("Pick target"), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true).Labelled("Charge move"), new GfRuleHits.CrossingStep(), new DefenderClosesStep().Labelled("Target moves"), new FightStep().Labelled("Melee")]);
    /// <summary>Outline labels: p.9 ("Once all charging models have attacked"; Return strikes) and p.10 (Melee resolution; Consolidation moves). The Counter, Impact and Unwieldy strikes and the after-melee window are internal.
    /// 3.5.1 resolves Counter before Impact; 3.6.0 p.14 rolls Impact "before either unit attacks" (GfEdition.ImpactBeforeCounter). 3.5.1 Ravage rolls after each side's
    /// strikes are chosen (GfRuleHits.RavageStep); 3.6 Ravage and Regenerative Strength roll before each side strikes (GfEdition.StrikeTurnRolls, GfStrikeRolls36).</summary>
    public static Sequence Melee(GfEdition e) => new(GfNames.Melee, [new MeleeStartStep(), .. e.ImpactBeforeCounter ? (IStep[])[new ImpactStep(e), new CounterStep(e)] : [new CounterStep(e), new ImpactStep(e)], .. e.StrikeTurnRolls
        ? (IStep[])[new GfStrikeRolls36.StrikeTurnStep("charger-strike-rolls", charger: true), new ChargerAttacksStep().Labelled("Charger attacks"), new StrikeChoiceStep(), new GfStrikeRolls36.StrikeTurnStep("defender-strike-rolls", charger: false), new StrikeBackStep(e).Labelled("Strike back")]
        : [new GfRuleHits.RavageStep("charger-ravage", charger: true), new ChargerAttacksStep().Labelled("Charger attacks"), new StrikeBackStep(e).Labelled("Strike back"), new GfRuleHits.RavageStep("defender-ravage", charger: false)], new UnwieldyStep(), new GfRuleHits.SelfDestructStep(), new CompareStep().Labelled("Melee resolution"), new ConsolidateStep().Labelled("Consolidation"), new GfMoves.PointWindowStep(GfNames.AfterMelee, "after-melee")]);

    /// <summary>The unit's melee weapons. A Limited weapon fired before the melee under way (or, outside one, ever) is left out (p.14 "may only be used once
    /// per game"), so the list is the same on each run of a melee step.</summary>
    public static IReadOnlyList<Guid> MeleeWeapons(GameState state, Guid unit, Func<Weapon, bool>? filter = null)
    {
        var used = GfAttack.FiredLimited(state, state.Projection.Stack.LastOrDefault(f => f.Sequence == GfNames.Melee)?.Id);
        return Lookup.LivingModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons)
            .Where(w => IsMelee(state, w) && !used.Contains(w.Id) && (filter?.Invoke(w) ?? true)).Select(w => w.Id).ToList();
    }

    /// <summary>A melee weapon has no Range (Army.cs); 3.6.0 p.13 Assault "Counts as both a ranged weapon and a melee weapon", so under 3.6 a
    /// ranged weapon carrying Assault is one too (GfEdition.HasAssault). It still shoots: GfShooting.Groups reads Range alone.</summary>
    public static bool IsMelee(GameState state, Weapon w) =>
        !w.Stats.ContainsKey(GfNames.Range) || (GfEdition.Of(state).HasAssault && w.Properties.Any(p => p.Name == GfNames.Assault));

    const double Touching = 1e-6;   // "not in base contact": farther apart than float noise
    const string CounterInMeleeKey = "counter_in_melee";
    /// <summary>The defender's strike-first test, settled once: p.13 Counter's weapons, and every melee weapon of a defender with Counter-Attack ("Strikes
    /// first when charged"). 3.6.0 Counter in Melee ("This model gets Counter on its weapons in melee") makes a holding model's weapons Counter weapons, and
    /// every model's when granted to the unit; a granted one is spent by the counter step, so every later step reads what that step recorded. A Counter
    /// the counter step held back (3.6.0 p.13: passed, or lost to fatigue) strikes with the rest.</summary>
    static Func<Weapon, bool> StrikesFirst(StepContext ctx)
    {
        var live = DefenderLive(ctx);
        if (CounterAttacks(ctx, live)) return _ => true;
        if (CounterHeld(ctx)) return _ => false;
        var holders = live.On(Side.Attacker).Where(p => p.Entry.Name == GfNames.CounterInMelee).ToList();
        var everyModel = holders.Any(p => p.ModelId is null) || ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(CounterInMeleeKey) == 1);
        var carried = holders.Select(p => p.ModelId).OfType<Guid>().SelectMany(m => ctx.Projection.Models[m].Slot.Weapons.Select(w => w.Id)).ToHashSet();
        return w => everyModel || carried.Contains(w.Id) || w.Properties.Any(p => p.Name == "Counter");
    }
    const string CounterHeldKey = "counter_held";
    static bool CounterHeld(StepContext ctx) => ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(CounterHeldKey) == 1);
    const string CounterAttackKey = "counter_attack";
    /// <summary>The defender's Counter-Attack, innate or granted. A granted one is spent by the counter step, so every later step reads what that step recorded.</summary>
    static bool CounterAttacks(StepContext ctx, LiveSet? live = null) =>
        ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(CounterAttackKey) == 1) || (live ?? DefenderLive(ctx)).Has(Side.Attacker, GfNames.CounterAttack);
    static LiveSet DefenderLive(StepContext ctx) => ctx.Settle(GfAttack.AttackInteraction(ctx, Defender(ctx), Charger(ctx), [], AttackKind.Melee, null));
    static LiveSet ChargerLive(StepContext ctx) => ctx.Settle(GfAttack.AttackInteraction(ctx, Charger(ctx), Defender(ctx), [], AttackKind.Melee, GfNames.Charge));
    /// <summary>Unwieldy: "Strikes last when charging": the charger's weapon, or every weapon of a charger the rule was granted to.</summary>
    static bool StrikesLast(StepContext ctx, Weapon w) => w.Properties.Any(p => p.Name == GfNames.Unwieldy) || ChargerLive(ctx).Has(Side.Attacker, GfNames.Unwieldy);
    static bool CounterStruck(StepContext ctx) => ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(StruckKey) == 1);
    /// <summary>The defender took its turn to attack: it struck first with Counter, or it struck back (strike-back recorded its groups).</summary>
    internal static bool DefenderStruck(StepContext ctx) => CounterStruck(ctx) || ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && (s.Values?.ContainsKey(StepRepeat.Key) ?? false));
    static Guid Charger(StepContext ctx) => ctx.Frame.Context.ActorUnitId!.Value;
    static Guid Defender(StepContext ctx) => ctx.Frame.Context.TargetUnitIds[0];
    /// <summary>By step name, not by index: charge's first step is the activation's dangerous terrain test (p.11).</summary>
    static Guid? Target(StepContext ctx) => GfAnswers.Pieces(ctx.State, ctx.Frame, "charge-target", "charge_target") is [var target, ..] ? target : null;
    /// <summary>A model of a within striking reach of a model of b: 3.5.1 p.9 "within 2” horizontally and 4” vertically"; 3.6.0 p.9 "within 3” of an enemy
    /// model from the target unit (horizontally or vertically)". The bound is the edition's (GfEdition.MeleeHorizontal, MeleeVertical).</summary>
    static bool InReach(GameState state, Guid a, Guid b) => Lookup.LivingModels(state, a).Any(m => Lookup.LivingModels(state, b).Any(t => GfAttack.WithinStrike(state, m, t)));
    static StepOutcome AfterChild(StepContext ctx, Func<StepOutcome> push, IReadOnlyList<StateChange>? onDone = null) =>
        ctx.History.Children.Count == 0 ? push() : ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(onDone ?? []) : new OpenAsks([]);

    /// <summary>The melee weapon groups of the given weapon ids (p.9's pool is one attack per weapon type, as p.8's shooting is), in GfGroups' order, each
    /// struck as one attack per carrier signature (GfGroups.Split). Read from every model's slot, so the list is the same on each run of a step.</summary>
    static IReadOnlyList<WeaponGroup> GroupsOf(StepContext ctx, IReadOnlyList<Guid> ids) =>
        GfGroups.Split(ctx.State, ctx.Properties, GfGroups.Of(ctx.State, ctx.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => ids.Contains(w.Id))));

    /// <summary>
    /// p.9: "Sum the Attack value from the weapons of all models that can strike ... how many attacks the unit has in total for this
    /// melee." Two phases, as `fire-groups`: the first run records every group's attacks per Quality value (`attacks_&lt;i&gt;_q&lt;n&gt;`, with
    /// `attacks_&lt;i&gt;` the total) and the defending unit's living models (`defender_models_&lt;i&gt;`, p.14's Hero clause) and repeats; the
    /// repeat pushes the groups, and determine-attacks reads the recorded counts, so an earlier group's casualties do not shorten a
    /// later one. `values` rides both completions.
    /// </summary>
    static StepOutcome Strike(StepContext ctx, string step, IReadOnlyList<WeaponGroup> groups, Guid attacker, Guid target, string? action, IReadOnlyList<StateChange>? onDone = null, IReadOnlyDictionary<string, int>? values = null)
    {
        if (Recorded(ctx, step))
        {
            var recorded = ctx.Frame.Frame.Steps[^1].Values!;
            var declined = recorded.GetValueOrDefault(TakedownStrikeDeclinedKey) == 1;
            return EachGroup(ctx, [.. groups.Where(g => !(declined && IsTakedownStrike(ctx.State, g)) && !recorded.ContainsKey(LimitedSavedKey + g.Name))], attacker, target, action, onDone, values);
        }
        if (groups.Count == 0) return new Complete(onDone ?? [], values);
        var settled = values is null ? new Dictionary<string, int>() : new Dictionary<string, int>(values);
        // Takedown Strike: "Once per game, when it's this model's turn to strike in melee, it may make X attacks …": asked when its weapon could strike;
        // declined, the weapon does not strike and stays unused (Limited).
        if (groups.Where(g => IsTakedownStrike(ctx.State, g)).ToList() is { Count: > 0 } strikes
            && strikes.Any(g => GfAttack.EligibleModels(ctx.State, LiveProperties.Settle(GfAttack.AttackInteraction(ctx, attacker, target, g.WeaponIds, AttackKind.Melee, action)),
                attacker, target, g.WeaponIds, AttackKind.Melee).Count > 0))
        {
            if (ctx.History.LastClosed(TakedownStrikeQuestion) is not { } closed)
                return new OpenAsks([ctx.Ask(TakedownStrikeQuestion, "Use Takedown Strike?", ctx.TeamOf(attacker)!.Value,
                    new Options([new AskOption("yes", "Yes", [attacker]), new AskOption("no", "No", [])]), allowPass: true)]);
            if (closed.Single is not Chosen { OptionId: "yes" })
            {
                settled[TakedownStrikeDeclinedKey] = 1;
                groups = [.. groups.Except(strikes)];
                if (groups.Count == 0) return new Complete(onDone ?? [], values);
            }
        }
        // p.14 Limited: "Weapons with this rule may only be used once per game", so the player picks the melee it is used in. Each Limited group
        // that could strike is asked; saved, it does not strike and stays unused.
        foreach (var g in groups.Where(g => IsLimited(ctx.State, g) && !IsTakedownStrike(ctx.State, g)).ToList())
        {
            if (GfAttack.EligibleModels(ctx.State, LiveProperties.Settle(GfAttack.AttackInteraction(ctx, attacker, target, g.WeaponIds, AttackKind.Melee, action)),
                    attacker, target, g.WeaponIds, AttackKind.Melee).Count == 0) continue;
            if (ctx.History.LastClosed(LimitedQuestion + g.Name) is not { } use)
                return new OpenAsks([ctx.Ask(LimitedQuestion + g.Name, $"Use {g.Name}?", ctx.TeamOf(attacker)!.Value,
                    new Options([new AskOption("yes", "Use it", [attacker]), new AskOption("no", "Save it", [])]), allowPass: true)]);
            if (use.Single is Chosen { OptionId: "yes" }) continue;
            settled[LimitedSavedKey + g.Name] = 1;
            groups = [.. groups.Where(x => x.Name != g.Name)];
            if (groups.Count == 0) return new Complete(onDone ?? [], values);
        }
        var defenderModels = Lookup.LivingModels(ctx.State, target).Count;
        foreach (var (g, i) in groups.Select((g, i) => (g, i)))
        {
            GfAttack.ListRuleWeapons(ctx, g.WeaponIds);
            var byQuality = GfAttack.AttacksAgainst(ctx, attacker, target, g.WeaponIds, AttackKind.Melee, action);
            settled[$"attacks_{i}"] = byQuality.Sum(x => x.Attacks);
            foreach (var (quality, attacks) in byQuality) settled[$"attacks_{i}_q{quality}"] = attacks;
            settled[$"defender_models_{i}"] = defenderModels;
        }
        return new Complete([], settled, Repeat: true);
    }

    /// <summary>The question of a Limited group's "Use it?" in melee, followed by the group's name: one Ask per group.</summary>
    public const string LimitedQuestion = "use_limited:";
    const string LimitedSavedKey = "limited_saved:";
    /// <summary>A group of a Limited weapon (p.14): every weapon of a group is the same weapon, so one carries the rule for all.</summary>
    static bool IsLimited(GameState state, WeaponGroup g) => g.WeaponIds.Count > 0 && WeaponOf(state, g.WeaponIds[0]).Properties.Any(p => p.Name == "Limited");

    public const string TakedownStrikeQuestion = "takedown_strike";
    const string TakedownStrikeDeclinedKey = "takedown_strike_declined";

    /// <summary>A group of the weapon the import adds for Takedown Strike, carried by a model holding the rule.</summary>
    static bool IsTakedownStrike(GameState state, WeaponGroup g) =>
        state.Projection.Models.Values.Any(m => m.Slot.Properties.Any(p => p.Name == GfNames.TakedownStrike)
            && m.Slot.Weapons.Any(w => g.WeaponIds.Contains(w.Id) && w.Name == GfNames.TakedownStrike));

    /// <summary>The step's last completion on this frame was its own recording run: the groups are being pushed.</summary>
    static bool Recorded(StepContext ctx, string step) =>
        ctx.Frame.Frame.Steps is [.., var last] && last.Step == step && (last.Values?.ContainsKey(StepRepeat.Key) ?? false);

    /// <summary>
    /// Pushes one attack-sequence per group in turn (the pushed count is the number of children, so the step is stateless and replayable),
    /// then completes with `onDone`. A target that no model survives ends the run: later groups have nothing to strike.
    /// </summary>
    static StepOutcome EachGroup(StepContext ctx, IReadOnlyList<WeaponGroup> groups, Guid attacker, Guid target, string? action, IReadOnlyList<StateChange>? onDone, IReadOnlyDictionary<string, int>? values)
    {
        var pushed = ctx.History.Children.Count;
        if (pushed > 0 && !(ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned)) return new OpenAsks([]);
        if (pushed < groups.Count && (pushed == 0 || Lookup.LivingModels(ctx.State, target).Count > 0))
            return new Push(GfNames.AttackSequence, new SequenceContext(attacker, [target], groups[pushed].WeaponIds, action));
        return new Complete(onDone ?? [], values);
    }
    static IReadOnlyList<StateChange> Fatigue(Guid unit) => [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Fatigued, true)];

    /// <summary>p.11 "Units may not move through impassable terrain": the volumes a close-in or a consolidation lists, which AnswerBreach judges; none for a
    /// Flying mover (p.13, GfMoves.Flies), whose landing AnswerBreach still judges.</summary>
    static IReadOnlyList<Guid> Impassable(StepContext ctx, Guid mover) => GfMoves.ImpassableFor(ctx.State, GfMoves.Flies(ctx.State, ctx.Properties, mover));

    /// <summary>The settled move answer: every model that went on the table lands on the surface. Shared by the defender's close-in and consolidation.</summary>
    static StepOutcome Settled(StepContext ctx, AskRecord closed)
    {
        var moves = GfAnswers.Moves(ctx.State, closed).Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, m.ModelId, ((OnTable)m.To).Placement)))).ToList();
        return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
    }

    /// <summary>Wounds the target took since melee-start, plus Fear(X) over the attacker's models when it attacked (p.13).</summary>
    public static int WoundsCaused(StepContext ctx, Guid by, Guid to, string beforeKey, bool attacked)
    {
        var wounds = ctx.Projection.Units[to].WoundsThisActivation - ctx.Frame.Value(beforeKey);
        var fear = attacked ? Lookup.LivingModels(ctx.State, by).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0) : 0;
        if (fear > 0) ctx.Applied.Add("Fear");
        return wounds + fear;
    }

    /// <summary>3.6.0 p.10: the wounds the unit took during this activation (its WoundsThisActivation, every deal-wounds frame on it since the activation
    /// began, the charge move's dangerous-terrain test included), plus 3.6.0 p.14 Fear(X) over the enemy's models ("enemies count as having taken +X
    /// wounds when checking who won"), whether or not the enemy attacked, plus the wounds a count_wounds row made it count as having taken in this melee's
    /// resolve-hits frames on it (3.6 Scourge: "enemies count as having taken 2 extra wounds when checking who won melee"), from CountedInMelee.</summary>
    public static int WoundsTaken(StepContext ctx, Guid unit, Guid enemy, IReadOnlyDictionary<Guid, (int Count, IReadOnlyList<string> Rules)> counted)
    {
        var fear = Lookup.LivingModels(ctx.State, enemy).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0);
        if (fear > 0) ctx.Applied.Add("Fear");
        var (count, rules) = counted.GetValueOrDefault(unit, (0, []));
        foreach (var rule in rules) ctx.Applied.Add(rule);
        return ctx.Projection.Units[unit].WoundsThisActivation + fear + count;
    }

    /// <summary>The count_wounds wounds each unit took in this melee's resolve-hits frames (GfAttack.CountedWounds). Run from the melee frame.</summary>
    public static IReadOnlyDictionary<Guid, (int Count, IReadOnlyList<string> Rules)> CountedInMelee(StepContext ctx)
    {
        var log = EventApplier.EffectiveLog(ctx.State.Applier.Log).ToList();
        return GfAttack.CountedWounds(log, log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == ctx.Frame.Id), shootingAndSpells: false);
    }

    /// <summary>
    /// The enemy units this unit could charge: p.7 "Units may only take a Charge action if at least one model is within charge distance of the
    /// target unit (including any additional movement from special rules)". Read by ActionsFor to offer Charge at all, and by charge-target.
    /// p.13: Aircraft "can't be charged", so an Aircraft unit is never a target. A charging model reaches a target model only at a landing in base
    /// contact within p.9's vertical strike reach of it (Movement.ContactInReach, GfEdition.MeleeVertical; owner ruling: "The fight cant happen if no
    /// one can strike so the charge should be rejected if nothing would reach the 3" threshold."), the landing resting where a move sent at the
    /// target's level settles it. The path goes round the other models as the charge's move is judged (p.7 "may never move through other models or
    /// units ... even if they are using a Charge action"): every unit but the charger and the target as its perimeter, those two as their bases;
    /// none for a Flying charger.
    /// </summary>
    /// <summary>The charge distance at a target: the charger's own, and what the target's rules take from it ("Enemies get -3\" movement to a min. of 6\" when trying
    /// to charge units where all models have this rule").</summary>
    public static double ChargeDistance(GameState state, PropertySet props, Guid unit, Guid target) =>
        GfMoves.SlowestDistance(state, unit, GfNames.Charge, p => new Interaction(state, props, p, Party.Whole(state, target), AttackKind.Melee, GfNames.Charge, null, []));

    /// <remarks>`difficult` false leaves the difficult cap out (3.5.1 p.11, 3.6.0 p.12: "If any model in a unit moves in or through Difficult terrain at any
    /// point of its move, then all models in the unit may not move more than 6” for that movement"), which ActivationChoices reads to say why no unit is
    /// offered. With it, a charger that does not ignore difficult terrain (GfMoves.ReachTerrain: Strider, Flying, the Banner of Eternal Flame) reaches
    /// a target past 6" only on a path round the difficult and steep ground, and a unit that counts as in difficult terrain, or has a model standing in it (GfMoves.StandsIn), reaches no farther than 6",
    /// as the charge move holds it.</remarks>
    public static IReadOnlyList<Guid> ChargeTargets(GameState state, PropertySet props, Guid unit, bool difficult = true)
    {
        var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
        // p.13 Flying "may move through units and terrain": a Flying charger's reach is not blocked by impassable terrain.
        var impassable = live.Has(Side.Attacker, "Flying") ? [] : Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        var slow = difficult ? GfMoves.ReachTerrain(state, props, unit, live) : ((IReadOnlyList<WorldVolume> Difficult, IReadOnlyList<WorldVolume> Steep, double? Cap, bool InDifficult)?)null;
        var team = Teams.Of(state, unit);
        // p.15: "Units may use Charge actions to disembark". An occupant has no position, so its reach is the exit radius from
        // the container it is inside — anything it could be placed fully within 6" of is a unit it could then be in contact with.
        var inside = Lookup.ContainerOf(state, unit);
        var own = Lookup.LivingModels(state, unit);
        var terrain = Battlefield.Terrain(state);
        var vertical = GfEdition.Of(state).MeleeVertical;
        bool? standsIn = null;
        var standing = live.Has(Side.Attacker, "Flying") ? GfMoves.Others.None
            : GfMoves.Others.Except(state, state.Projection.Models.Values.Where(m => m.UnitId == unit).Select(m => m.Id).ToHashSet());
        return state.Projection.Units.Values.Where(u => Teams.Of(state, u.Id) != team && !Lookup.UnitHas(state, props, u.Id, GfNames.Aircraft)).Select(u => u.Id).Where(t =>
        {
            var enemies = Lookup.LivingModels(state, t);
            var distance = ChargeDistance(state, props, unit, t);
            // A unit counted as in difficult terrain, or with a model standing in it (which moves in it as soon as it moves, capping the whole unit; a charge
            // whose other models leave it behind in coherency past the cap is the move's to find, not the offer's), reaches no farther than the cap.
            if (slow is { InDifficult: true } || (slow is { Cap: not null } terms && (standsIn ??= GfMoves.StandsIn(state, unit, terms.Difficult)))) distance = Math.Min(distance, GfNames.DifficultCap);
            // p.9: "within charge distance of one model from the target unit, and has a clear path to reach it", and "charge moves don't have to be in a
            // straight line": a path to base contact round every impassable volume (Movement.ContactDistance). The straight distance is a floor on the
            // path, and cheap: search only the pairs already within it.
            if (inside is { } container) return enemies.Any(e => Battlefield.Distance(state, container, e) <= GfNames.ExitWithin);
            return own.Any(m => enemies.Any(e => Battlefield.Placed(state, m) is { } from && Battlefield.Placed(state, e) is { } to
                && Battlefield.Distance(state, m, e) <= distance
                && Movement.ContactInReach(from, to, distance, impassable, state.Board.Table.Size, terrain, vertical, slow?.Difficult, slow?.Cap, slow?.Steep,
                    standing.Models, standing.For(unit, null, [t]))));
        }).ToList();
    }

    sealed class ChargeTargetStep : IStep
    {
        public string Name => "charge-target";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is { } answered)
            {
                var picked = GfAnswers.Pieces(answered.Single, answered.Ask);
                if (picked.Count == 0) return new Finish([]);   // an answer that names no offered unit is no target
                var range = Battlefield.UnitDistance(ctx.State, Charger(ctx), picked[0]) ?? 0;
                return new Complete([], new Dictionary<string, int> { [GfNames.ChargeRangeKey] = (int)Math.Round(range * 100) });
            }
            var unit = Charger(ctx);
            var team = ctx.ActorTeam;
            var targets = ChargeTargets(ctx.State, ctx.Properties, unit);
            // Instinctive: "it must immediately attack the closest valid target".
            if (ctx.Projection.Units[unit].Conditions.Contains(GfNames.InstinctiveCondition)) targets = GrimdarkFutureRules.Closest(ctx.State, unit, targets);
            // p.7: "if being within charge distance becomes impossible (due to killed models, special rules, etc.)" after the action was taken.
            if (targets.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("charge_target", "Charge which unit?", team!.Value, new PieceSelection(targets, 1, 1))]);
        }
    }

    /// <summary>p.9: the target's models not in base contact move up to 3" into contact — their models, their Ask.</summary>
    sealed class DefenderClosesStep : IStep
    {
        public string Name => "defender-closes";
        public StepOutcome Run(StepContext ctx)
        {
            if (Target(ctx) is not { } target) return new Finish([]);
            var charger = Charger(ctx);
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var free = Lookup.LivingModels(ctx.State, target).Where(t => Lookup.LivingModels(ctx.State, charger).All(m => Battlefield.Distance(ctx.State, m, t) > Touching)).ToList();
            if (free.Count == 0 || ctx.TeamOf(target) is not { } team) return new Complete([]);
            return new OpenAsks([ctx.Ask("move", $"Move up to {GfNames.ChargeCloseIn}\" into contact", team, new MoveRegion(free, GfNames.ChargeCloseIn, Impassable(ctx, target), [], null), allowPass: true)]);   // a close-in: impassable terrain, no difficult cap
        }
    }

    sealed class FightStep : IStep
    {
        public string Name => "fight";
        public StepOutcome Run(StepContext ctx)
        {
            if (Target(ctx) is not { } target) return new Finish([]);
            var charger = Charger(ctx);
            if (ctx.History.Children.Count == 0 && !InReach(ctx.State, charger, target)) return new Finish([]);
            return AfterChild(ctx, () => new Push(GfNames.Melee, new SequenceContext(charger, [target], MeleeWeapons(ctx.State, charger), GfNames.Charge)));
        }
    }

    sealed class MeleeStartStep : IStep
    {
        public string Name => "melee-start";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            var values = new Dictionary<string, int> { ["charger_before"] = ctx.Projection.Units[charger].WoundsThisActivation, ["defender_before"] = ctx.Projection.Units[defender].WoundsThisActivation };
            GfAttack.RollAttackStart(ctx, charger, [defender], AttackKind.Melee, values);
            GfAttack.RollAttackStart(ctx, defender, [charger], AttackKind.Melee, values);
            return new Complete([], values);
        }
    }

    const string StruckKey = "struck";

    /// <summary>3.5.1 p.13 Counter: "Strikes first with this weapon when charged". 3.6.0 p.13: "May strike first with this weapon when charged, unless this
    /// unit is Fatigued" (GfEdition.CounterOptional, CounterLostWhenFatigued; a Shaken unit counts as Fatigued, p.10): the defender's team is asked
    /// (`counter`), and a pass or a fatigued defender records counter_held, so the Counter weapons strike with the rest in strike-back. Counter-Attack
    /// ("Strikes first when charged") puts every weapon first and is not asked.</summary>
    sealed class CounterStep(GfEdition edition) : IStep
    {
        public string Name => "counter";
        public StepOutcome Run(StepContext ctx)
        {
            var defender = Defender(ctx);
            var counters = MeleeWeapons(ctx.State, defender, StrikesFirst(ctx));
            if (counters.Count == 0) return new Complete([]);
            if (!Recorded(ctx, Name) && !CounterAttacks(ctx) && (edition.CounterOptional || edition.CounterLostWhenFatigued))
            {
                var held = new Dictionary<string, int> { [CounterHeldKey] = 1 };
                if (edition.CounterLostWhenFatigued && ctx.Projection.Units[defender].Conditions is var c && (c.Contains(GfNames.Fatigued) || c.Contains(GfNames.Shaken)))
                    return new Complete([], held);
                if (edition.CounterOptional)
                {
                    if (ctx.History.LastClosed("counter") is not { } closed)
                        return new OpenAsks([ctx.Ask("counter", "Strike first with Counter?", ctx.TeamOf(defender)!.Value, new Options([new AskOption("strike", "Strike first", [defender]), new AskOption("pass", "Strike with the rest", [])]))]);
                    if (closed.Single is not Chosen { OptionId: "strike" }) return new Complete([], held);
                }
            }
            // 3.6 Ravage, Regenerative Strength: its turn to strike is now, so its strike rolls come before the Counter strike (GfStrikeRolls36).
            if (edition.StrikeTurnRolls && !Recorded(ctx, Name) && ctx.History.Children.Count == 0 && GfStrikeRolls36.Due(ctx, defender, Charger(ctx)) is { } rolls) return rolls;
            if (edition.StrikeTurnRolls && !Recorded(ctx, Name) && ctx.History.Children is [.., var rolled] && !(rolled.Finished || rolled.Abandoned)) return new OpenAsks([]);
            // p.13: Counter moves the weapon's strike first; the defender's fatigue is set once, at the end of strike-back. A granted Counter-Attack
            // is spent when the strike is done and recorded here, because strike-back then reads it from the frame.
            var live = DefenderLive(ctx);
            var values = new Dictionary<string, int> { [StruckKey] = 1 };
            if (!Recorded(ctx, Name) && MeleeWeapons(ctx.State, Defender(ctx), w => w.Properties.Any(p => p.Name == "Counter")).Count > 0) ctx.Applied.Add("Counter");
            if (live.Use(Side.Attacker, GfNames.CounterAttack)) values[CounterAttackKey] = 1;
            if (live.On(Side.Attacker).Any(p => p.Entry.Name == GfNames.CounterInMelee && p.ModelId is null)) values[CounterInMeleeKey] = 1;   // granted to the unit
            live.Use(Side.Attacker, GfNames.CounterInMelee);
            return Strike(ctx, Name, GroupsOf(ctx, counters), Defender(ctx), Charger(ctx), null, ctx.Consume(live), values);
        }
    }

    /// <summary>p.14's `Impact(X)`: X dice per Impact(X) model, unless fatigued; hits at the effect's threshold. Under 3.5.1 (GfEdition.CounterReducesImpact) p.13's
    /// `Counter` takes one die per defending model with Counter; 3.6.0 p.14 drops that. Two phases: roll (Repeat), then push resolve-hits when the dice as they then
    /// stand hold a hit (ImpactHits), so an edit between the two counts. Values["hits"] is the hits as rolled.</summary>
    sealed class ImpactStep(GfEdition edition) : IStep
    {
        public string Name => "impact";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("impact_phase") == 1)
                return ctx.History.Children.Count == 0 && ImpactHits(ctx.Frame) == 0 ? new Complete([])
                    : AfterChild(ctx, () => new Push(GfNames.ResolveHits, new SequenceContext(charger, [defender], [], GfNames.Charge)));
            var live = ctx.Settle(GfAttack.AttackInteraction(ctx, charger, defender, [], AttackKind.Melee, GfNames.Charge));
            var impacts = RollEffects.OwnRolls(live, Side.Attacker, c => c.Kind == EffectKind.AddHit);   // Impact's conditions (charging, not fatigued) were judged in pass one's read
            if (impacts.Count == 0) return new Complete([], new Dictionary<string, int> { ["hits"] = 0 });
            var counterModels = !edition.CounterReducesImpact ? 0 : Lookup.LivingModels(ctx.State, defender).Count(m => ctx.Projection.Models[m].Slot.Weapons.Any(w => w.Properties.Any(p => p.Name == "Counter")));
            var brace = edition.ImpactBeforeCounter ? Brace(ctx, charger, defender) : 0;
            // Heavy Impact: "Counts as having Impact(X) with hits that have AP(1)": its models' dice roll as their own pool, so only their hits get
            // AP(1) (roll-to-block, HeavyImpactHits). The reductions come off the other dice first.
            var heavy = impacts.Where(x => live.GrantorOf(x.Source) == GfNames.HeavyImpact).ToList();
            var plainDice = impacts.Except(heavy).Sum(x => x.Roll.EffectiveAmount(x.Source.Parameter));
            var heavyDice = heavy.Sum(x => x.Roll.EffectiveAmount(x.Source.Parameter));
            var total = plainDice + heavyDice;
            var dice = Math.Max(0, total - counterModels - brace);
            heavyDice = Math.Min(heavyDice, dice); plainDice = dice - heavyDice;
            if (counterModels > 0) ctx.Applied.Add("Counter");
            if (brace > 0 && total > 0) ctx.Applied.Add(GfNames.Brace);
            Dice.RollPlan Plan(string name, Effect roll) => new(name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>());
            var hits = 0;
            if (plainDice > 0) hits += Dice.RollOutcome.Successes(ctx.Roll(Plan(impacts.Except(heavy).First().Source.Entry.Name, impacts.Except(heavy).First().Roll), plainDice));
            if (heavyDice > 0) hits += Dice.RollOutcome.Successes(ctx.Roll(Plan(GfNames.HeavyImpact, heavy[0].Roll), heavyDice));
            return new Complete([], new Dictionary<string, int> { ["hits"] = hits, ["impact_phase"] = 1 }, Repeat: dice > 0);
        }

        /// <summary>Brace: "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued." X read off the defender's melee weapons, one
        /// instance per weapon type; a Shaken unit counts as Fatigued (3.6.0 p.10).</summary>
        static int Brace(StepContext ctx, Guid charger, Guid defender)
        {
            if (ctx.Projection.Units[defender].Conditions is var c && (c.Contains(GfNames.Fatigued) || c.Contains(GfNames.Shaken))) return 0;
            var ix = GfAttack.AttackInteraction(ctx, charger, defender, [], AttackKind.Melee, GfNames.Charge)
                with { Defender = Party.Of(ctx.State, defender, Lookup.LivingModels(ctx.State, defender), MeleeWeapons(ctx.State, defender)) };
            return Math.Max(0, LiveProperties.Settle(ix).Parameter(Side.Defender, GfNames.Brace) ?? 0);
        }
    }

    /// <summary>The successes of the frame's Heavy Impact pool as its dice now stand: the Impact hits that have AP(1).</summary>
    public static int HeavyImpactHits(FrameView frame) =>
        frame.Frame.Steps.Where(s => s.Step == "impact").SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.HeavyImpact).Sum(Dice.RollOutcome.Successes);

    /// <summary>The successes of the frame's Impact roll as its dice now stand; null when the frame made none.</summary>
    public static int? ImpactHits(FrameView frame) =>
        frame.Frame.Steps.Where(s => s.Step == "impact").SelectMany(s => s.Rolls).ToList() is { Count: > 0 } rolls ? rolls.Sum(Dice.RollOutcome.Successes) : null;

    sealed class ChargerAttacksStep : IStep
    {
        public string Name => "charger-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var weapons = ctx.Frame.Context.WeaponIds.Where(id => !StrikesLast(ctx, WeaponOf(ctx.State, id))).ToList();
            var lastOnes = ctx.Frame.Context.WeaponIds.Count > weapons.Count;
            return Strike(ctx, Name, GroupsOf(ctx, weapons), Charger(ctx), Defender(ctx), GfNames.Charge, lastOnes ? [] : Fatigue(Charger(ctx)));
        }
    }

    /// <summary>Unwieldy's strike, after the strike back: the charger's strike-last weapons, which fatigue it when they are done.</summary>
    sealed class UnwieldyStep : IStep
    {
        public string Name => "unwieldy";
        public StepOutcome Run(StepContext ctx)
        {
            var weapons = ctx.Frame.Context.WeaponIds.Where(id => StrikesLast(ctx, WeaponOf(ctx.State, id))).ToList();
            if (weapons.Count == 0 && !Recorded(ctx, Name)) return new Complete([]);
            // A granted Unwieldy is spent here, where it put the charger's weapons last, whether or not they get to strike.
            var live = ChargerLive(ctx);
            live.Use(Side.Attacker, GfNames.Unwieldy);
            if (weapons.Count > 0 && !Recorded(ctx, Name)) ctx.Applied.Add(GfNames.Unwieldy);
            var spent = ctx.Consume(live);
            if (Lookup.LivingModels(ctx.State, Charger(ctx)).Count == 0) return new Complete(spent);
            // The charger's other weapons may have destroyed the defender: nothing is struck, but the fatigue charger-attacks deferred is still owed.
            if (!Recorded(ctx, Name) && Lookup.LivingModels(ctx.State, Defender(ctx)).Count == 0) return new Complete([.. spent, .. Fatigue(Charger(ctx))]);
            return Strike(ctx, Name, GroupsOf(ctx, weapons), Charger(ctx), Defender(ctx), GfNames.Charge, [.. spent, .. Fatigue(Charger(ctx))]);
        }
    }

    static Weapon WeaponOf(GameState state, Guid id) => state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == id);

    /// <summary>
    /// p.9: the defender may strike back (its Ask), and doing so fatigues it; a destroyed defender cannot. A **Shaken** defender may strike
    /// back too, and it is fatigued for doing so: 3.5.1 p.10 reads "must stay idle, but may strike back counting as fatigued"
    /// (GfRolls.Fatigued reads the Shaken condition as fatigue). No Shaken guard belongs here.
    /// </summary>
    /// <summary>3.6 (GfEdition.StrikeTurnRolls): the defender's "Strike back?" asked before its strike rolls, recorded as `strike_back_chosen`, which
    /// `defender-strike-rolls` and `strike-back` read. Nothing to strike with, or a side gone, records 0.</summary>
    sealed class StrikeChoiceStep : IStep
    {
        public string Name => "strike-choice";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            var first = StrikesFirst(ctx);
            static Complete Chosen(bool strike) => new([], new Dictionary<string, int> { [StrikeBackChosenKey] = strike ? 1 : 0 });
            if (Lookup.LivingModels(ctx.State, defender).Count == 0 || Lookup.LivingModels(ctx.State, charger).Count == 0 || MeleeWeapons(ctx.State, defender, w => !first(w)).Count == 0) return Chosen(false);
            if (ctx.History.LastClosed("strike_back") is { } closed) return Chosen(closed.Single is Chosen { OptionId: "strike" });
            return new OpenAsks([StrikeBackAsk(ctx, defender)]);
        }
    }

    public const string StrikeBackChosenKey = "strike_back_chosen";
    static Ask StrikeBackAsk(StepContext ctx, Guid defender) =>
        ctx.Ask("strike_back", "Strike back?", ctx.TeamOf(defender)!.Value, new Options([new AskOption("strike", "Strike back", [defender]), new AskOption("pass", "Don't", [])]));

    sealed class StrikeBackStep(GfEdition edition) : IStep
    {
        public string Name => "strike-back";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            var counterStruck = CounterStruck(ctx);
            var first = StrikesFirst(ctx);
            var weapons = MeleeWeapons(ctx.State, defender, w => !first(w));   // p.13: the Counter weapon's strike already happened in `counter`
            // Striking under way comes before the living-model checks: a strike back that destroys the charger still fatigues (p.9).
            if (Recorded(ctx, Name)) return Strike(ctx, Name, GroupsOf(ctx, weapons), defender, charger, null, Fatigue(defender));
            if (Lookup.LivingModels(ctx.State, defender).Count == 0) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, charger).Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
            if (edition.StrikeTurnRolls)   // the choice was asked by strike-choice, before the strike rolls
                return ctx.Frame.Value(StrikeBackChosenKey) == 1 && weapons.Count > 0 ? Strike(ctx, Name, GroupsOf(ctx, weapons), defender, charger, null, Fatigue(defender)) : new Complete(counterStruck ? Fatigue(defender) : []);
            if (ctx.History.LastClosed("strike_back") is { } closed)
                return closed.Single is Chosen { OptionId: "strike" } ? Strike(ctx, Name, GroupsOf(ctx, weapons), defender, charger, null, Fatigue(defender)) : new Complete(counterStruck ? Fatigue(defender) : []);
            if (weapons.Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
            return new OpenAsks([StrikeBackAsk(ctx, defender)]);
        }
    }

    /// <summary>p.10: sum wounds caused each way; a destroyed unit always loses; the loser tests morale; a tie tests nobody. Values: charger_wounds, defender_wounds, loser (0 none, 1 charger, 2 defender).
    /// 3.6.0 p.10 (GfEdition.MeleeLossByWoundsTaken): "compare the number of wounds each unit took during this activation instead. The unit with the highest
    /// total loses": charger_wounds and defender_wounds hold the wounds each unit took (WoundsTaken), the charge move's dangerous-terrain wounds included.</summary>
    sealed class CompareStep : IStep
    {
        public string Name => "compare";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("compared") == 1)   // second run: the loser tests morale on the melee path
            {
                var loserUnit = ctx.Frame.Value("loser") == 1 ? charger : defender;
                return AfterChild(ctx, () => new Push(GfNames.MoraleTest, new SequenceContext(loserUnit, [], [], "melee")));
            }
            var struck = ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && s.Answer is Chosen { OptionId: "strike" }) || CounterStruck(ctx);
            var byTaken = GfEdition.Of(ctx.State).MeleeLossByWoundsTaken;
            var counted = byTaken ? CountedInMelee(ctx) : new Dictionary<Guid, (int, IReadOnlyList<string>)>();
            var chargerWounds = byTaken ? WoundsTaken(ctx, charger, defender, counted) : WoundsCaused(ctx, charger, defender, "defender_before", attacked: true);
            var defenderWounds = byTaken ? WoundsTaken(ctx, defender, charger, counted) : WoundsCaused(ctx, defender, charger, "charger_before", attacked: struck);
            var chargerDead = Lookup.LivingModels(ctx.State, charger).Count == 0; var defenderDead = Lookup.LivingModels(ctx.State, defender).Count == 0;
            var (chargerLosing, defenderLosing) = byTaken ? (chargerWounds > defenderWounds, defenderWounds > chargerWounds) : (defenderWounds > chargerWounds, chargerWounds > defenderWounds);
            var loser = defenderDead ? 2 : chargerDead ? 1 : defenderLosing ? 2 : chargerLosing ? 1 : 0;
            var values = new Dictionary<string, int> { ["charger_wounds"] = chargerWounds, ["defender_wounds"] = defenderWounds, ["loser"] = loser, ["compared"] = 1 };
            var testing = loser == 1 ? charger : loser == 2 ? defender : (Guid?)null;
            var spent = GfAttack.SpendAttackStart(ctx, charger, [defender], AttackKind.Melee).Concat(GfAttack.SpendAttackStart(ctx, defender, [charger], AttackKind.Melee)).Distinct().ToList();
            return new Complete(spent, values, Repeat: testing is not null && Lookup.LivingModels(ctx.State, testing.Value).Count > 0);
        }
    }

    /// <summary>p.10: a destroyed or routed unit lets the other move 3"; otherwise the charger moves back 1", unless the game plays without melee separation.</summary>
    sealed class ConsolidateStep : IStep
    {
        public string Name => "consolidate";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var (chargerAlive, defenderAlive) = (Lookup.LivingModels(ctx.State, charger), Lookup.LivingModels(ctx.State, defender));
            if (chargerAlive.Count == 0 && defenderAlive.Count == 0) return new Complete([]);
            // p.10: "players may agree not to separate units by 1"" (optional rule melee_separation off): both alive, nobody moves back.
            if (chargerAlive.Count > 0 && defenderAlive.Count > 0 && !GfOptions.Of(ctx.State).MeleeSeparation) return new Complete([]);
            var (mover, distance) = chargerAlive.Count == 0 ? (defender, GfNames.Consolidate) : defenderAlive.Count == 0 ? (charger, GfNames.Consolidate) : (charger, GfNames.FallBack);
            return new OpenAsks([ctx.Ask("move", $"Move up to {distance}\"", ctx.TeamOf(mover)!.Value, new MoveRegion(Lookup.LivingModels(ctx.State, mover), distance, Impassable(ctx, mover), [], null), allowPass: true)]);   // fall back / consolidate: impassable terrain, no difficult cap
        }
    }
}
