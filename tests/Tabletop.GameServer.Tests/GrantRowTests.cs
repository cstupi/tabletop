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
}

