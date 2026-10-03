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

public class GrammarAdditionTests
{
    static EffectDocument HitMod(int amount, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, when, []);
    static EntryDocument Rule(string name, params EffectDocument[] rows) => new(name, CatalogKind.Model, false, Aggregation.All, null, null, $"{name}.", null, rows);

    static readonly PropertySet Props = TestProperties.WithSpells(
        Rule("Base Rule"),
        Rule("Boosted", HitMod(1, new ConditionDocument(ConditionKind.HoldsProperty, EffectSubject.Self, Comparison.Eq, null, ["Base Rule"]))),
        Rule("Giant Slayer", HitMod(2, new ConditionDocument(ConditionKind.MostModels, EffectSubject.Opponent, Comparison.Gte, 3, ["Tough"]))),
        Rule("Armour Breaker", HitMod(2, new ConditionDocument(ConditionKind.MostModels, EffectSubject.Opponent, Comparison.Lte, 3, ["Defense"]))),
        Rule("Long Charger", HitMod(1, new ConditionDocument(ConditionKind.ActionTaken, null, Comparison.Eq, null, ["Charge"]), new ConditionDocument(ConditionKind.Range, null, Comparison.Gt, 9, []))),
        Rule("Steady", new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "morale", null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction,
            [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting", "melee"])], [])));

    static LiveSet Shot(GameState s, Guid a, Guid b) => LiveProperties.Settle(new Interaction(s, Props, Party.Whole(s, a), Party.Whole(s, b), AttackKind.Shooting, null, 12, []));
    static int HitBonus(LiveSet live) => live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Holds_property_reads_the_base_rule_on_the_holders_side()
    {
        var with = Unit("A", "4+", "4+", [], 2, [P("Boosted"), P("Base Rule")]); var without = Unit("C", "4+", "4+", [], 2, [P("Boosted")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([with, without], [b]);
        Assert.Equal(1, HitBonus(Shot(s, with.Id, b.Id)));
        Assert.Equal(0, HitBonus(Shot(s, without.Id, b.Id)));
    }

    [Fact]
    public void Most_models_compares_a_rules_parameter_or_a_stats_target_number_over_more_than_half()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Giant Slayer"), P("Armour Breaker")]);
        var big = Unit("Big", "4+", "3+", [], 3, [P("Tough", 3)]);
        var mixed = Unit("Mixed", "4+", "5+", [], 2, []);
        mixed = mixed with { Models = [mixed.Models[0] with { Properties = [P("Tough", 6)] }, mixed.Models[1]] };   // one of two: not most
        var s = State([a], [big, mixed]);
        Assert.Equal(4, HitBonus(Shot(s, a.Id, big.Id)));
        Assert.Equal(0, HitBonus(Shot(s, a.Id, mixed.Id)));
    }

    [Fact]
    public void A_charge_measures_its_range_from_where_it_started()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Long Charger")], Melee("Claws", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 12), GrimdarkFutureRules.Instance, Props, new ScriptedDice(6, 6, 6, 6, 6, 6));
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Equal(1074, sim.Steps("charge-target").Last().Values![GfNames.ChargeRangeKey]);          // 12" between centres, less two 0.63" radii; the range predicate floors 10.74 to 10, over 9
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = 10.74 } }))]));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "move" }) sim.Answer(Samples.Team2, new Passed());
        Assert.Contains(sim.Steps("roll-to-hit").First().Rolls.SelectMany(r => r.Modifiers), x => x.Source == "Long Charger");
    }

    [Fact]
    public void A_morale_test_knows_whether_shooting_melee_or_a_spell_caused_it()
    {
        var u = Unit("U", "4+", "4+", [], 1, [P("Steady")]);
        foreach (var (action, expected) in new[] { ("melee", true), ("spell", false) })
        {
            var sim = new Sim(State([u], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new ScriptedDice(3));
            sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], action));
            var roll = sim.Steps("test").Single().Rolls.Single();
            Assert.Equal(expected, roll.Modifiers.Any(x => x.Source == "Steady"));
        }
    }
}
