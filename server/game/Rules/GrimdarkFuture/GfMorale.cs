using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMorale
{
    /// <summary>Under 3.6 (GfEdition.ReinforcementOnShaken) Reinforcement's removal offer follows the consequence.</summary>
    public static Sequence MoraleTest(GfEdition edition) => new(GfNames.MoraleTest,
        edition.ReinforcementOnShaken ? [new TestStep(), new OverrideStep(), new ConsequenceStep(), new GfReserves.OfferRemovalStep()] : [new TestStep(), new OverrideStep(), new ConsequenceStep()]);

    /// <summary>What caused a general morale test: the kind of the last attack at the unit in the effective log, a spell's hits being Spell and a
    /// strike a melee pushed being Melee (3.6.0 p.13 Assault strikes with a weapon that has a Range).
    /// Null when nothing attacked it.</summary>
    public static AttackKind? CauseOf(GameState state, Guid unit) => Cause(state, unit).Kind;

    /// <summary>CauseOf with the attacking unit: the actor of that attack (none for a spell's hits).</summary>
    static (AttackKind? Kind, Guid? By, double? Range) Cause(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        for (var i = log.Count - 1; i >= 0; i--)
            if (log[i].Payload is SequenceStarted s && s.Context.TargetUnitIds.Contains(unit))
            {
                if (s.Sequence == GfNames.SpellHitsSeq) return (AttackKind.Spell, null, null);
                if (s.Sequence == GfNames.AttackSequence && s.Context.WeaponIds.Count > 0)
                {
                    var kind = GfAttack.KindOf(state, s.Context.WeaponIds) is AttackKind.Shooting && GfEdition.Of(state).HasAssault && GfAttack.StartedInMelee(state, s.FrameId) ? AttackKind.Melee : GfAttack.KindOf(state, s.Context.WeaponIds);
                    // The range the attack measured when it was made (determine-attacks' range_hundredths), not where the units stand now: the test comes at
                    // the end of the activation, after any move the shooter made since.
                    var range = log.Skip(i + 1).Select(e => e.Payload).OfType<StepCompleted>().FirstOrDefault(c => c.FrameId == s.FrameId && c.Step == "determine-attacks")
                        ?.Values?.TryGetValue(GfAttack.RangeKey, out var h) == true ? h / 100.0 : (double?)null;
                    return (kind, s.Context.ActorUnitId, range);
                }
            }
        return (null, null, null);
    }

    /// <summary>The morale test's own die and its override dice, as they now stand: a test step's record named `morale`, and every record
    /// the override step rolled.</summary>
    static bool Passed(FrameView frame)
    {
        var steps = frame.Frame.Steps;
        var test = steps.Where(s => s.Step == "test").SelectMany(s => s.Rolls).FirstOrDefault(r => r.Roll == GfNames.Morale);
        if (test is not null && Dice.RollOutcome.Successes(test) == 1) return true;
        return steps.Where(s => s.Step == "override").SelectMany(s => s.Rolls).Any(r => Dice.RollOutcome.Successes(r) > 0);
    }

    /// <summary>
    /// The test's interaction: the testing unit is the attacker side, the unit whose attack caused the test the defender side. "When shot/charged from
    /// over 9"/6" away" (3.6.0 Hive Bond, Hold the Line) reads the range and the opponent's action: after a melee, the distance the charge-target step
    /// recorded and, for the unit charged, the charger's Charge; after shooting, the range the attack measured (determine-attacks' `range_hundredths`,
    /// GfEdition.AttackRangeRecorded; none under 3.5.1). A spell's test
    /// and a test with no attack behind it carry neither.
    /// </summary>
    static Interaction Ix(StepContext ctx, Guid unit)
    {
        var terrain = Battlefield.TerrainUnder(ctx.State, unit);
        if (ctx.Frame.Context.Action == "spell")
            return new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, AttackKind.Spell, ctx.Frame.Context.Action, null, terrain);
        if (ctx.Frame.Context.Action == "melee" && MeleeOf(ctx.Frame) is { } melee)
        {
            var (charger, defender) = (melee.Frame.Context.ActorUnitId, melee.Frame.Context.TargetUnitIds.FirstOrDefault());
            var opponent = unit == defender ? charger : defender;
            var charged = unit == defender && melee.Frame.Context.Action == GfNames.Charge;
            return new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), Opponent(ctx.State, opponent), AttackKind.Melee,
                charged ? GfNames.Charge : ctx.Frame.Context.Action, GfAttack.ChargeRange(ctx.Frame), terrain);
        }
        var (cause, by, range) = ctx.Frame.Context.Action == "melee" ? (AttackKind.Melee, null, null) : Cause(ctx.State, unit);
        return new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), cause == AttackKind.Shooting ? Opponent(ctx.State, by) : null, cause, ctx.Frame.Context.Action,
            cause == AttackKind.Shooting ? range : null, terrain);
    }

    /// <summary>The other unit of the melee frame.</summary>
    static Guid? Opponent(FrameView melee, Guid unit) =>
        unit == melee.Frame.Context.TargetUnitIds.FirstOrDefault() ? melee.Frame.Context.ActorUnitId : melee.Frame.Context.TargetUnitIds.FirstOrDefault();

    static FrameView? MeleeOf(FrameView frame)
    {
        for (var f = frame.Parent; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.Melee) return f;
        return null;
    }

    /// <summary>The opposing unit as a whole party, while it still has a model in play.</summary>
    static Party? Opponent(GameState state, Guid? unit) =>
        unit is { } u && state.Projection.Units.ContainsKey(u) && Lookup.AliveModels(state, u).Count > 0 ? Party.Whole(state, u) : null;

    static IReadOnlyList<(LiveProperty Source, Effect Roll)> OverrideRolls(LiveSet live) =>
        RollEffects.OwnRolls(live, Side.Attacker, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.MoraleTest });

    /// <summary>
    /// p.10: one Quality test; a Shaken unit "always fails", so no die is rolled for it. Whether it passed is read by the steps after
    /// this one, off the die as it now stands (opr-sequences → Dice edits).
    /// </summary>
    const string HeldPrefix = "held_";

    sealed class TestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lookup.AliveModels(ctx.State, unit).Count == 0) return new Finish([]);             // in play, not positioned: a unit carried in a transport still takes its test
            var ix = Ix(ctx, unit);
            var live = ctx.Settle(ix);
            if (!ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) ctx.Roll(GfRolls.MoralePlan(ctx, live), 1);
            var spent = RollEffects.Consume(live);
            var left = GfDeferred.Leave(spent, ix, l => OverrideRolls(l));
            // What this step read but left for `override` (a spell's morale modifier beside its Fearless), keyed so override spends it on either path.
            var held = spent.Except(left).OfType<GrantRemoved>().ToDictionary(g => $"{HeldPrefix}{g.UnitId:N}_{g.GrantId:N}", _ => 1);
            return new Complete(left, held);
        }
    }

    /// <summary>p.13 Fearless: "fails a morale test, roll one die. On a 4+ it counts as passed instead", which a Shaken unit's automatic failure
    /// also triggers. Every kind-5 roll overriding a morale test's result rolls when the test die as it now stands failed.</summary>
    sealed class OverrideStep : IStep
    {
        public string Name => "override";
        public StepOutcome Run(StepContext ctx)
        {
            if (Passed(ctx.Frame)) return new Complete(Held(ctx));
            var live = ctx.Settle(Ix(ctx, ctx.Frame.Context.ActorUnitId!.Value));
            foreach (var (source, roll) in OverrideRolls(live))
                ctx.Roll(new Dice.RollPlan(source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Morale).Convention, null, new HashSet<int>()), roll.EffectiveAmount(source.Parameter));
            return new Complete([.. ctx.Consume(live).Union(Held(ctx))]);
        }

        /// <summary>The grants `test` read and held back, still on their unit: spent here whether or not the override rolls.</summary>
        static IReadOnlyList<StateChange> Held(StepContext ctx)
        {
            var held = new List<StateChange>();
            foreach (var key in ctx.Frame.Values.Keys.Where(k => k.StartsWith(HeldPrefix, StringComparison.Ordinal)))
            {
                var ids = key[HeldPrefix.Length..].Split('_');
                if (Guid.TryParseExact(ids[0], "N", out var unit) && Guid.TryParseExact(ids[1], "N", out var grant)
                    && ctx.Projection.Units.TryGetValue(unit, out var u) && u.Grants.Any(g => g.Id == grant))
                    held.Add(new GrantRemoved(unit, grant));
            }
            return held;
        }
    }

    /// <summary>
    /// p.10's consequence, from the test and override dice as they now stand. A failed test after a melee routs a unit at half or less of its
    /// starting size (every model to the shelf) and shakes it otherwise; after wounds it only shakes.
    /// </summary>
    sealed class ConsequenceStep : IStep
    {
        const string SplitKeptKey = "split_kept";
        public string Name => "consequence";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var passed = Passed(ctx.Frame);
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0, ["routed"] = 0 };
            if (passed) return new Complete([], values);
            var routs = ctx.Frame.Context.Action == "melee" && GfHooks.AtHalfOrLess(ctx.State, ctx.Properties, unit);
            // No Retreat's "fails a Morale Test that causes it to be Shaken or Routed": a unit already Shaken that does not rout is caused nothing.
            if ((routs || !ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken) || ctx.Frame.Value("no_retreat") == 1) && NoRetreat(ctx, unit) is { } holding) return holding;
            if (routs)
            {
                values["routed"] = 1;
                var living = Lookup.AliveModels(ctx.State, unit);
                // p.10: "the unit Routs and counts as destroyed": destroyed by the unit it fought (GfMarkers.OnDestroyed: Frenzies, Vengeance).
                var destroyed = MeleeOf(ctx.Frame) is { } melee && Opponent(melee, unit) is { } by ? GfMarkers.OnDestroyed(ctx, by, unit) : [];
                IReadOnlyList<StateChange> Removed() => [new ModelsMoved(living.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList()), .. destroyed];
                // Split: "When this unit is fully destroyed, you may place a new unit of X fully within 6" of it before removing the last model": the others
                // leave first (a repeat), then place-reserve anchors on the last model, which goes once it is done.
                if (ctx.History.Children.Count > 0) return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(Removed(), values) : new OpenAsks([]);
                if (GfReserves.KeepsLastModel(ctx.State, unit) && GfReserves.Waiting(ctx.State, GfReserves.Split, unit) is { } reserve && living.Count > 0)
                {
                    if (ctx.Frame.Value(SplitKeptKey) == 0)
                        return new Complete([new ModelsMoved(living.Skip(1).Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())],
                            new Dictionary<string, int>(values) { [SplitKeptKey] = 1 }, Repeat: true);
                    return new Push(GfReserves.PlaceReserveSeq, new SequenceContext(unit, [reserve], [], GfReserves.Split));
                }
                return new Complete(Removed(), values);
            }
            return new Complete([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true)], values);
        }

        /// <summary>
        /// No Retreat: "When a unit where most models have this rule fails a morale test that causes it to be Shaken or Routed, the test counts as passed
        /// instead. Then, roll as many dice as the number of wounds it would take to fully destroy it, and for each result of 1-3 the unit takes one wound,
        /// which can't be ignored." The roll (Repeat), then the wounds through deal-wounds, which rolls no ignore die under this Action. Null when the
        /// unit does not hold it.
        /// </summary>
        static StepOutcome? NoRetreat(StepContext ctx, Guid unit)
        {
            var held = new Dictionary<string, int> { ["passed"] = 1, ["routed"] = 0, ["no_retreat"] = 1 };
            if (ctx.Frame.Value("no_retreat") == 1)
                return ctx.History.Children.Count == 0 && ctx.Frame.Value("wounds") > 0 && Lookup.AliveModels(ctx.State, unit).Count > 0
                    ? new Push(GfNames.DealWounds, new SequenceContext(null, [unit], [], GfNames.NoRetreat)) : new Complete([], held);
            var live = ctx.Settle(Ix(ctx, unit));
            if (!live.Use(Side.Attacker, GfNames.NoRetreat)) return null;
            var toDestroy = Lookup.AliveModels(ctx.State, unit).Sum(m => Math.Max(0, (Lookup.ModelParameter(ctx.State, m, GfNames.Tough) ?? 1) - ctx.Projection.Models[m].Wounds));
            var record = ctx.Roll(new Dice.RollPlan(GfNames.NoRetreat, null, [], ctx.Rules.RollSpec(GfNames.Morale).Convention, null, new HashSet<int>()), Math.Min(toDestroy, GfRuleHits.MaxDealt));
            var wounds = record.Dice.Count(d => d.Raw <= 3);
            return new Complete(ctx.Consume(live), new Dictionary<string, int>(held) { ["wounds"] = wounds }, Repeat: true);
        }
    }
}
