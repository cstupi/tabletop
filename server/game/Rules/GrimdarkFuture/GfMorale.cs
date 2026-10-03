using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMorale
{
    public static Sequence MoraleTest() => new(GfNames.MoraleTest, [new TestStep()]);

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

    /// <summary>
    /// p.10: one Quality test; a Shaken unit "always fails", so no die is rolled for it. p.13 Fearless: "fails a morale test, roll one die.
    /// On a 4+ it counts as passed instead", which a Shaken unit's automatic failure also triggers. A failed test after a melee routs a unit at
    /// half or less of its starting size (every model to the shelf) and shakes it otherwise; after wounds it only shakes.
    /// </summary>
    sealed class TestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var living = Lookup.AliveModels(ctx.State, unit);                                     // in play, not positioned: a unit carried in a transport still takes its test
            if (living.Count == 0) return new Finish([]);
            var cause = ctx.Frame.Context.Action switch { "melee" => AttackKind.Melee, "spell" => AttackKind.Spell, _ => CauseOf(ctx.State, unit) };
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, cause, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit)));
            var passed = false;
            if (!ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken))
                passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.MoralePlan(ctx, live), 1)) == 1;
            if (!passed)
                foreach (var (source, roll) in RollEffects.OwnRolls(live, Side.Attacker, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.MoraleTest }))
                    passed |= Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Morale).Convention, null, new HashSet<int>()), roll.EffectiveAmount(source.Parameter))) > 0;
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0, ["routed"] = 0 };
            if (passed) return new Complete(RollEffects.Consume(live), values);
            if (ctx.Frame.Context.Action == "melee" && GfHooks.AtHalfOrLess(ctx.State, ctx.Properties, unit))
            {
                values["routed"] = 1;
                return new Complete([.. RollEffects.Consume(live), new ModelsMoved(living.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())], values);
            }
            return new Complete([.. RollEffects.Consume(live), new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true)], values);
        }
    }
}
