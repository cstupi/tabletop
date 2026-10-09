using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class ConditionsTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static EffectCondition Opponent(ConditionKind kind, Comparison cmp, int? number, params string[] values) => new(Guid.NewGuid(), kind, EffectSubject.Opponent, cmp, number, values);

    // A lone interaction (charge reach, a move, a resolve check) has no defender: a condition on the opponent cannot hold, as position_terrain already reads.
    [Fact]
    public void An_opponent_condition_is_false_with_no_defender_and_a_slayer_is_still_offered_a_charge()
    {
        var slayers = Unit("Slayers", "4+", "4+", [], 1, [P("Slayer")], Melee("Blade", 1));
        var ogres = Unit("Ogres", "4+", "4+", [], 2, [P("Tough", 3)]);
        var state = State([slayers], [ogres], x1: -4, x2: 4);
        var ix = new Interaction(state, Core, Party.Whole(state, slayers.Id), null, AttackKind.Melee, GfNames.Charge, null, []);
        var slayer = Core.Entry("Slayer")!;
        var holder = new LiveProperty(Side.Attacker, slayer, null, slayers.Models[0].Id, null, null);
        Assert.False(Conditions.Hold(ix, Side.Attacker, slayer.Effects[1], holder, null));                           // most_models on the opponent
        Assert.False(Conditions.Holds(ix, Side.Attacker, Opponent(ConditionKind.HoldsProperty, Comparison.Eq, null, "Tough"), holder, null));
        Assert.False(Conditions.Holds(ix, Side.Attacker, Opponent(ConditionKind.ActorState, Comparison.Eq, null, "fatigued"), holder, null));
        Assert.False(Conditions.Holds(ix, Side.Attacker, Opponent(ConditionKind.ActorState, Comparison.Neq, null, "fatigued"), holder, null));
        Assert.Contains(ogres.Id, GfMelee.ChargeTargets(state, Core, slayers.Id));
    }

    // p.14 Stealth: "shot from over 9\" away"; p.5 measures base to base, so 9.41" is over 9".
    [Fact]
    public void Range_compares_the_measured_distance_unrounded()
    {
        var s = Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var t = Unit("T", "4+", "4+", [], 1, [P("Stealth")]);
        var state = State([s], [t]);
        Interaction Ix(double range) => new(state, Core, Party.Whole(state, s.Id), Party.Whole(state, t.Id), AttackKind.Shooting, GfNames.Hold, range, []);
        var stealth = Core.Entry("Stealth")!;
        var holder = new LiveProperty(Side.Defender, stealth, null, t.Models[0].Id, null, null);
        Assert.True(Conditions.Hold(Ix(9.41), Side.Defender, stealth.Effects[0], holder, null));
        Assert.True(Conditions.Hold(Ix(9.001), Side.Defender, stealth.Effects[0], holder, null));
        Assert.False(Conditions.Hold(Ix(9.0), Side.Defender, stealth.Effects[0], holder, null));
        Assert.False(Conditions.Hold(Ix(8.99), Side.Defender, stealth.Effects[0], holder, null));
    }

    [Fact]
    public void A_roll_result_may_also_name_the_dies_success()
    {
        var s = Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var t = Unit("T", "4+", "4+", [], 1, []);
        var state = State([s], [t]);
        var ix = new Interaction(state, Core, Party.Whole(state, s.Id), Party.Whole(state, t.Id), AttackKind.Shooting, GfNames.Hold, 12, []);
        var holder = new LiveProperty(Side.Attacker, Core.Entry("Scrapper")!, null, null, null, null);
        EffectCondition Face(params string[] values) => new(Guid.NewGuid(), ConditionKind.RollResult, null, Comparison.Eq, 5, values);
        Assert.True(Conditions.Holds(ix, Side.Attacker, Face("defense", "raw", "success"), holder, new DieContext("defense", 5, 4, true)));
        Assert.False(Conditions.Holds(ix, Side.Attacker, Face("defense", "raw", "success"), holder, new DieContext("defense", 5, 3, false)));
        Assert.True(Conditions.Holds(ix, Side.Attacker, Face("defense", "raw", "fail"), holder, new DieContext("defense", 5, 3, false)));
        Assert.False(Conditions.Holds(ix, Side.Attacker, Face("defense", "raw", "fail"), holder, new DieContext("defense", 5, 4, true)));
        Assert.True(Conditions.Holds(ix, Side.Attacker, Face("defense", "raw"), holder, new DieContext("defense", 5, 3, false)));   // no third value: either
    }

    [Fact]
    public void A_roll_result_outcome_is_success_or_fail_and_a_forced_reroll_takes_success_only()
    {
        var doc = TestProperties.CoreDocument;
        IReadOnlyList<string> With(string outcome) => PropertiesJson.Validate(doc with { Entries = doc.Entries.Select(e => e.Name != "Scrapper Boost" ? e : e with
        {
            Effects = e.Effects.Select(f => f with { Conditions = f.Conditions.Select(c => c.Kind == ConditionKind.RollResult ? c with { Values = ["defense", "raw", outcome] } : c).ToList() }).ToList(),
        }).ToList() });
        Assert.Empty(With(Conditions.Success));
        Assert.Contains(With("maybe"), p => p.Contains("roll_result outcome is 'maybe'"));
        Assert.Contains(With(Conditions.Fail), p => p.Contains("force_reroll re-rolls a successful face only"));
    }

    // "must re-roll successful unmodified defense results of 5-6": under AP(2) a raw 5 against Defense 4+ is a 3 and fails, so it is not re-rolled;
    // against Defense 3+ it is a success and is. The raw 6 is Scrapper's own and Mischievous's own.
    [Theory]
    [InlineData("Scrapper", "4+", new[] { 1 })]
    [InlineData("Scrapper", "3+", new[] { 0, 1 })]
    [InlineData("Mischievous", "4+", new[] { 1 })]
    [InlineData("Mischievous", "3+", new[] { 0, 1 })]
    public void A_boost_rerolls_only_successful_unmodified_fives(string rule, string defense, int[] rerolled)
    {
        var a = Unit("A", "2+", "4+", [], 1, [P(rule), P($"{rule} Boost")], Gun("Rifle", "24\"", 2, P("AP", 2)));
        var b = Unit("B", "4+", defense, [], 3, []);
        var sim = new Sim(State([a], [b]), GrimdarkFutureRules.Instance, Core, new ScriptedDice(2, 2, 5, 6, 1, 1, 1, 1));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(rerolled, sim.Emitted.OfType<DiceRerolled>().SelectMany(r => r.Replaced).Select(r => r.Index).Order());
    }

    // "take extra wounds on failed defense rolls of 1-2": against Defense 2+ a raw 2 blocks, so only the raw 1 adds a wound (one failure plus one extra).
    [Theory]
    [InlineData("Warbound")]
    [InlineData("Infected")]
    public void A_boost_adds_wounds_only_on_failed_twos(string rule)
    {
        var a = Unit("A", "2+", "4+", [], 1, [P(rule), P($"{rule} Boost")], Gun("Rifle", "24\"", 2));
        var b = Unit("B", "4+", "2+", [], 5, []);
        var sim = new Sim(State([a], [b]), GrimdarkFutureRules.Instance, Core, new ScriptedDice(2, 2, 2, 1));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
    }
}
