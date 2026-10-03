using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
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

    static EffectDocument Kind(EffectKind kind, EffectSubject subject, EffectDuration duration, int? amount = null, TargetTerrain? terrain = null, string? condition = null, string? roll = null, params EffectDocument[] children) =>
        new(subject, 10, kind, roll, null, null, null, null, null, null, amount, false, null, null, null, duration, [], children, terrain, condition);
    static EntryDocument EnemySpell(string name, params EffectDocument[] rows) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24), rows);
    static readonly PropertySet Kinds = TestProperties.WithSpells(
        EnemySpell("Mire", Kind(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once, terrain: TargetTerrain.Difficult)),
        EnemySpell("Spikes", Kind(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once, terrain: TargetTerrain.Dangerous)),
        EnemySpell("Fury", Kind(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: [Kind(EffectKind.SetCondition, EffectSubject.Self, EffectDuration.Interaction, condition: "fatigued")])),
        EnemySpell("Hypnosis", Kind(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: [Kind(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, amount: 6)])));

    static void GrantOn(GameState s, Guid unit, string spell, Guid caster) =>
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(unit, new Grant(Guid.CreateVersion7(), Kinds.Entry(spell)!.Effects[0].Id, spell, caster, 1)), null));

    [Fact]
    public void Counting_as_in_difficult_terrain_caps_the_next_move_at_six_and_is_spent_by_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(4));
        GrantOn(sim.State, a.Id, "Mire", a.Id);
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        Assert.Equal(6, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 5 } }))]));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void Counting_as_in_dangerous_terrain_tests_every_model_at_the_next_activation()
    {
        var a = Unit("A", "4+", "4+", [], 3, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(1, 6, 6));
        GrantOn(sim.State, a.Id, "Spikes", a.Id);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Equal(3, sim.Steps("test").Single().Rolls.Single().Dice.Count);
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_unit_holding_a_once_dangerous_terrain_grant_takes_its_test_at_an_idle_activation()
    {
        var a = Unit("A", "4+", "4+", [], 2, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(6, 6));
        GrantOn(sim.State, a.Id, "Spikes", a.Id);
        sim.Start(GfNames.IdleSeq, new SequenceContext(a.Id, [], [], "Idle"));
        Assert.Equal(2, sim.Steps("test").Single().Rolls.Single().Dice.Count);
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_failed_forced_test_leaves_the_target_shaken_and_fatigued_and_a_passed_one_neither()
    {
        foreach (var (die, failed) in new[] { (1, true), (6, false) })
        {
            var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
            var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6, die));
            sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
            sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
            sim.Answer(Samples.Team1, new Chosen($"Fury|{wizard.Models[0].Id}"));
            sim.Answer(Samples.Team1, new Passed());
            sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
            Assert.Equal((failed, failed), (sim.Unit(t.Id).Conditions.Contains(GfNames.Shaken), sim.Unit(t.Id).Conditions.Contains(GfNames.Fatigued)));
        }
    }

    [Fact]
    public void A_forced_test_at_a_shaken_unit_fails_without_a_roll_and_its_children_apply()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new ConditionSet(new Subject(SubjectKind.Unit, t.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fury|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Contains(GfNames.Fatigued, sim.Unit(t.Id).Conditions);
    }

    static Sim Hypnotised(out Guid unit, out Guid model)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var target = Unit("T", "4+", "4+", [], 1, []);
        var sim = new Sim(State([wizard], [target], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6, 1));
        foreach (var (u, x) in new[] { (wizard, 20.0), (target, 28.0) })      // onto the 72x48 table, where a forced move may be judged against its edge
        {
            var m = u.Models[0].Id;
            sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, 20, 0), new Yaw(0))))])]);
        }
        unit = target.Id; model = target.Models[0].Id;
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Hypnosis|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return sim;
    }

    [Fact]
    public void A_failed_hypnosis_lets_the_casters_team_move_the_target_up_to_six_inches()
    {
        var sim = Hypnotised(out _, out var m);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(6.0, region.Allowance);
        Assert.Equal([m], region.ModelIds);
        var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 5 } }))]));
        Assert.Equal(at.Position.X + 5, ((OnTable)sim.Model(m).Location).Placement.Position.X, 3);
    }

    [Theory]
    [InlineData(8.0)]      // over the 6" allowance
    [InlineData(-40.0)]    // off the table
    public void A_forced_move_over_the_allowance_or_off_the_table_is_rejected_and_moves_nothing(double dx)
    {
        var sim = Hypnotised(out _, out var m);
        var at = ((OnTable)sim.Model(m).Location).Placement;
        var before = sim.State.Applier.Log.Count(e => e.Payload is ModelsMoved);
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }))]));
        Assert.Equal(at.Position.X, ((OnTable)sim.Model(m).Location).Placement.Position.X, 3);
        Assert.Equal(before, sim.State.Applier.Log.Count(e => e.Payload is ModelsMoved));
    }
}
