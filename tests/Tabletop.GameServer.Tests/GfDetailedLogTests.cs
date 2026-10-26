using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Detailed-log spec → What the server adds: what a suppression took away (RollRecord.Ignored, ThresholdBasis.Ignored,
/// StepCompleted.Ignored) and the rule behind a forced reroll (DieReplacement.By). None of it changes a result.</summary>
public class GfDetailedLogTests
{
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality");

    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    static LiveSet Shooting(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, double range, string action = GfNames.Hold, PropertySet? props = null)
    {
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        return LiveProperties.Settle(new Interaction(state, props ?? TestProperties.Core, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons), Party.Whole(state, target.Id),
            AttackKind.Shooting, action, range, []));
    }

    [Fact]
    public void Unstoppable_records_the_stealth_penalty_it_took_away()
    {
        var live = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Unstoppable"))), Unit("T", "4+", "4+", [], 2, [P("Stealth")]), 12);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", 4, [], D6, null, "hit");
        Assert.Empty(plan.Modifiers);
        Assert.Equal([new IgnoredEffect("Stealth", "Unstoppable", -1)], plan.Ignored!);
    }

    [Fact]
    public void A_plan_nothing_took_anything_from_records_no_ignored_list()
    {
        var live = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("T", "4+", "4+", [], 2, [P("Stealth")]), 12);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", 4, [], D6, null, "hit");
        Assert.Equal(["Stealth"], plan.Modifiers.Select(m => m.Source));
        Assert.Null(plan.Ignored);
    }

    [Fact]
    public void Quick_readjustment_is_recorded_as_taking_away_indirects_penalty_after_an_advance()
    {
        var live = Shooting(Unit("A", "4+", "4+", [], 1, [P("Quick Readjustment")], Gun("Mortar", "36\"", 1, P("Indirect"))), Unit("B", "4+", "4+", [], 1, []), 12, GfNames.Advance);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", 4, [], D6, null, "hit");
        Assert.Empty(plan.Modifiers);
        Assert.Equal([new IgnoredEffect("Indirect", "Quick Readjustment", -1)], plan.Ignored!);

        var held = Shooting(Unit("A", "4+", "4+", [], 1, [P("Quick Readjustment")], Gun("Mortar", "36\"", 1, P("Indirect"))), Unit("B", "4+", "4+", [], 1, []), 12);
        Assert.Null(RollEffects.Plan(held, Side.Attacker, "hit", 4, [], D6, null, "hit").Ignored);   // Indirect's -1 needs an advance: nothing would have applied
    }

    static EffectDocument Row(EffectKind kind, int? amount = null, string? entry = null, StatRef? stat = null, string? replacement = null) =>
        new(EffectSubject.Self, 10, kind, null, stat, entry, null, null, null, null, amount, false, null, null, replacement, EffectDuration.Interaction, [], []);
    static EntryDocument Rule(string name, params EffectDocument[] rows) => new(name, CatalogKind.Model, false, Aggregation.All, null, null, $"{name}.", null, rows);

    [Fact]
    public void A_stat_row_of_a_removed_rule_is_on_the_basis_as_ignored()
    {
        var props = TestProperties.WithSpells(
            Rule("Clumsy", Row(EffectKind.ModifyStat, amount: 1, stat: Quality)),
            Rule("Veteran", Row(EffectKind.ReplaceStat, stat: Quality, replacement: "3+")),
            Rule("Steady", Row(EffectKind.SuppressProperty, entry: "Clumsy"), Row(EffectKind.SuppressProperty, entry: "Veteran")));
        var live = Shooting(Unit("A", "4+", "4+", [], 1, [P("Clumsy"), P("Veteran"), P("Steady")], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, []), 12, props: props);
        var read = StatReader.TargetNumber(live, Side.Attacker, Quality);
        Assert.Equal(4, read.Value);
        Assert.Empty(read.Basis!.Changes);
        Assert.Equal([new IgnoredEffect("Veteran", "Steady", 0, 3), new IgnoredEffect("Clumsy", "Steady", 1)], read.Basis.Ignored!);

        var plain = Shooting(Unit("A", "4+", "4+", [], 1, [P("Clumsy")], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, []), 12, props: props);
        Assert.Null(StatReader.TargetNumber(plain, Side.Attacker, Quality).Basis!.Ignored);
    }

    [Fact]
    public void The_hit_record_carries_what_unstoppable_took_away()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Unstoppable")));
        var target = Unit("T", "4+", "6+", [], 2, [P("Stealth")]);
        var sim = Sim.Gf(State([shooter], [target], x1: -6, x2: 6), 4, 2);
        sim.Start(GfNames.AttackSequence, Attack(shooter, target));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal([new IgnoredEffect("Stealth", "Unstoppable", -1)], hit.Ignored!);
    }

    [Fact]
    public void A_bad_shot_unit_firing_an_unstoppable_weapon_records_bad_shot_as_ignored_by_unstoppable()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [P("Bad Shot")], Gun("Beam", "24\"", 1, P("Unstoppable")));
        var target = Unit("T", "4+", "6+", [], 1, []);
        var sim = Sim.Gf(State([shooter], [target], x1: -6, x2: 6), 4, 2);
        sim.Start(GfNames.AttackSequence, Attack(shooter, target));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Empty(hit.Modifiers);
        Assert.Equal([new IgnoredEffect("Bad Shot", "Unstoppable", -1)], hit.Ignored!);
        Assert.True(hit.Dice.Single().Success);
    }

    [Fact]
    public void A_deal_wounds_step_lists_regeneration_as_ignored_by_rending()
    {
        var shooter = Unit("S", "2+", "4+", [], 1, [], Gun("Saw", "12\"", 1, P("Rending")));
        var target = Unit("T", "4+", "6+", [], 1, [P("Regeneration")]);
        var sim = Sim.Gf(State([shooter], [target], x1: -4, x2: 4), 5, 1);
        sim.Start(GfNames.AttackSequence, Attack(shooter, target));
        Assert.Contains(new IgnoredEffect("Regeneration", "Rending"), sim.Steps("assign").SelectMany(s => s.Ignored ?? []));
        Assert.All(sim.Emitted.OfType<StepCompleted>().Where(s => s.Step != "assign"), s => Assert.Null(s.Ignored));
    }

    [Fact]
    public void A_forced_reroll_names_the_rule_that_forced_it()
    {
        var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Bane")));
        var sim = Sim.Gf(State([shooter], [Unit("B", "4+", "4+", [], 1, [])], x1: -3, x2: 3), 5, 6, 1);
        sim.Start(GfNames.AttackSequence, Attack(shooter, UnitOfState(sim, 1)));
        var reroll = Assert.Single(sim.Emitted.OfType<DiceRerolled>());
        Assert.Equal("Bane", Assert.Single(reroll.Replaced).By);
    }

    static Tabletop.Content.Unit UnitOfState(Sim sim, int ordinal) =>
        sim.State.Projection.Units.Values.Select(u => u.Definition).OrderBy(u => u.Name).ElementAt(ordinal);
}
