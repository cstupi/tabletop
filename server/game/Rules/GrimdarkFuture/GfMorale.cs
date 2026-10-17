using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMorale
{
    public static Sequence MoraleTest() => new(GfNames.MoraleTest, [new TestStep(), new OverrideStep(), new ConsequenceStep()]);

    /// <summary>What caused a general morale test: the kind of the last attack at the unit in the effective log, a spell's hits being Spell.
    /// Null when nothing attacked it.</summary>
    public static AttackKind? CauseOf(GameState state, Guid unit)
    {
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log).Reverse())
            if (e.Payload is SequenceStarted s && s.Context.TargetUnitIds.Contains(unit))
            {
                if (s.Sequence == GfNames.SpellHitsSeq) return AttackKind.Spell;
                if (s.Sequence == GfNames.AttackSequence && s.Context.WeaponIds.Count > 0) return GfAttack.KindOf(state, s.Context.WeaponIds);
            }
        return null;
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

    static Interaction Ix(StepContext ctx, Guid unit)
    {
        var cause = ctx.Frame.Context.Action switch { "melee" => AttackKind.Melee, "spell" => AttackKind.Spell, _ => CauseOf(ctx.State, unit) };
        return new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, cause, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit));
    }

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
            return new Complete([.. RollEffects.Consume(live).Union(Held(ctx))]);
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
        public string Name => "consequence";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var passed = Passed(ctx.Frame);
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0, ["routed"] = 0 };
            if (passed) return new Complete([], values);
            if (NoRetreat(ctx, unit) is { } holding) return holding;
            if (ctx.Frame.Context.Action == "melee" && GfHooks.AtHalfOrLess(ctx.State, ctx.Properties, unit))
            {
                values["routed"] = 1;
                var living = Lookup.AliveModels(ctx.State, unit);
                return new Complete([new ModelsMoved(living.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())], values);
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
            return new Complete(RollEffects.Consume(live), new Dictionary<string, int>(held) { ["wounds"] = wounds }, Repeat: true);
        }
    }
}
