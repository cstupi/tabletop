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

public class GrantRowTests
{
    static ConditionDocument Shooting => new(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"]);
    static EffectDocument Row(EffectKind kind, EffectSubject subject, string? entry = null, string? action = null, int? amount = null, params ConditionDocument[] when) =>
        new(subject, 10, kind, null, null, entry, action, null, null, null, amount, false, null, null, null, EffectDuration.Once, when, []);
    static EntryDocument Spell(string name, TargetSide side, params EffectDocument[] rows) =>
        new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(side, TargetGrain.Unit, 1, 18), rows);

    static readonly EntryDocument RendingShots = Spell("Rending Shots", TargetSide.Friendly, Row(EffectKind.GrantProperty, EffectSubject.Self, "Rending", when: [Shooting]));
    static readonly EntryDocument RelentlessAgainst = Spell("Marked", TargetSide.Enemy, Row(EffectKind.GrantProperty, EffectSubject.Opponent, "Relentless"));
    static readonly EntryDocument ShockSpeed = Spell("Shock Speed", TargetSide.Friendly,
        Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Advance", amount: 2), Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Rush", amount: 4), Row(EffectKind.ModifyStat, EffectSubject.Self, action: "Charge", amount: 4));
    static readonly PropertySet Props = TestProperties.WithSpells(RendingShots, RelentlessAgainst, ShockSpeed);

    /// <summary>Every once-row of the spell as a grant on the unit, the shape the effects step writes.</summary>
    static void Grant(GameState state, Guid unit, string spell, Guid caster)
    {
        foreach (var row in Props.Entry(spell)!.Effects.Where(e => e.Duration == EffectDuration.Once))
            state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new GrantAdded(unit, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), row.Id, spell, caster, 1)), null));
    }

    static LiveSet Attack(GameState state, Guid attacker, Guid defender, AttackKind kind) =>
        LiveProperties.Settle(new Interaction(state, Props, Party.Whole(state, attacker), Party.Whole(state, defender), kind, null, 12, []));

    [Fact]
    public void A_conditional_grant_keeps_its_condition()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Grant(state, a.Id, "Rending Shots", a.Id);
        Assert.True(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Rending"));
        Assert.False(Attack(state, a.Id, b.Id, AttackKind.Melee).Has(Side.Attacker, "Rending"));
    }

    [Fact]
    public void An_against_grant_gives_the_casters_side_the_rule_and_the_marked_unit_nothing()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Grant(state, b.Id, "Marked", a.Id);
        Assert.True(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
        Assert.False(Attack(state, b.Id, a.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
        Assert.False(Lookup.UnitHas(state, Props, b.Id, "Relentless"));
    }

    [Fact]
    public void An_against_grant_reaches_no_opponent_off_the_casters_team()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []); var c = Unit("C", "4+", "4+", [], 1, []);
        var state = State([a], [b, c]);
        Grant(state, b.Id, "Marked", c.Id);                    // written by a caster on the marked unit's own team
        Assert.False(Attack(state, a.Id, b.Id, AttackKind.Shooting).Has(Side.Attacker, "Relentless"));
    }

    [Fact]
    public void A_three_row_spell_moves_two_inches_more_on_advance_not_six()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var state = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        Grant(state, a.Id, "Shock Speed", a.Id);
        var live = LiveProperties.Settle(new Interaction(state, Props, Party.Whole(state, a.Id), null, null, "Advance", null, []));
        Assert.Equal(8, StatReader.ActionDistance(live, Side.Attacker, "Advance"));
        Assert.Equal(16, StatReader.ActionDistance(live, Side.Attacker, "Rush"));
    }

    [Fact]
    public void A_second_cast_of_the_same_spell_on_the_same_unit_writes_no_grant()
    {
        // Two casters, because one caster tries a spell once per activation (GfCasting.TriedPicks).
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var adept = Unit("Adept", "4+", "4+", [], 1, [P("Caster", 6)]);
        var sim = new Sim(State([wizard, adept], [Unit("B", "4+", "4+", [], 1, [])], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(6, 6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new SpellTokensSet(adept.Models[0].Id, 6)]);
        foreach (var caster in new[] { wizard, adept })
        {
            sim.Start(GfNames.CastingSeq, new SequenceContext(caster.Id, [], [], null));
            sim.Answer(Samples.Team1, new Chosen($"Shock Speed|{caster.Models[0].Id}"));
            sim.Answer(Samples.Team1, new Passed());
            sim.Answer(Samples.Team1, new SelectedPieces([wizard.Id]));
        }
        Assert.Equal(3, sim.Unit(wizard.Id).Grants.Count);
        Assert.All(sim.Unit(wizard.Id).Grants, g => Assert.Equal("Shock Speed", g.EntryName));
    }

    static readonly EntryDocument Deadly3 = Spell("Deadly Touch", TargetSide.Friendly,
        Row(EffectKind.Multiply, EffectSubject.Self, amount: 3) with { TargetQuantity = TargetQuantity.Wounds });
    static readonly PropertySet DeadlyProps = TestProperties.WithSpells(Deadly3);

    [Fact]
    public void A_speed_grant_is_consumed_by_the_next_move_and_its_three_rows_go_together()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new ScriptedDice(4));
        Grant(sim.State, a.Id, "Shock Speed", a.Id);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.Equal(8, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 7 } }))]));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void Offering_charge_reads_a_speed_grant_without_consuming_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b], x1: 0, x2: 15);                        // 15" apart: out of a 12" charge, inside 16"
        Grant(state, a.Id, "Shock Speed", a.Id);
        Assert.Contains(b.Id, GfMelee.ChargeTargets(state, Props, a.Id));
        Assert.Equal(3, state.Projection.Units[a.Id].Grants.Count);
    }

    [Fact]
    public void A_per_die_grant_is_consumed_by_the_next_attack_whatever_the_dice()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 3, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, new ScriptedDice(2, 2, 2));
        Grant(sim.State, a.Id, "Rending Shots", a.Id);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Empty(sim.Unit(a.Id).Grants);                               // a miss still spends "next time the effect would apply"
    }

    [Fact]
    public void A_grant_whose_conditions_never_hold_is_never_consumed()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Sword", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, new ScriptedDice(5, 5, 5, 5));
        Grant(sim.State, a.Id, "Rending Shots", a.Id);                     // shooting only
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [b.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Assert.Single(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_granted_wound_multiplier_is_spent_when_the_wounds_are_applied()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, DeadlyProps, new ScriptedDice(5, 2));
        sim.Append([new GrantAdded(a.Id, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), DeadlyProps.Entry("Deadly Touch")!.Effects[0].Id, "Deadly Touch", a.Id, 1))]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void Consuming_a_grant_on_a_unit_with_no_alive_models_neither_throws_nor_reads_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Grant(state, a.Id, "Rending Shots", a.Id);
        var shelf = a.Models.Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnShelf())).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(shelf), null));
        var live = Attack(state, a.Id, b.Id, AttackKind.Shooting);
        Assert.Empty(live.GrantsRead);
        Assert.Empty(RollEffects.Consume(live));
    }
}

