using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The 3.6 hand part's same-effect renames (3.6.0 book texts): Counter in Melee, Crossing Strike, Unique Hero, and the renamed entries composing.</summary>
public class ArmyRules36Tests
{
    static readonly PropertySet Core36 = TestProperties.Core36;

    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Theory]
    [InlineData("Anti-Regen")]
    [InlineData("Anti-Cover")]
    [InlineData("Anti-Cover Aura")]
    [InlineData("Anti-Cover when Shooting")]
    [InlineData("Anti-Cover when Shooting Aura")]
    [InlineData("Bravery Aura")]
    [InlineData("Bravery Buff")]
    [InlineData("Crossing Strike")]
    [InlineData("Piercing Charge")]
    [InlineData("Counter in Melee")]
    [InlineData("Unique Hero")]
    public void A_renamed_rule_is_in_the_3_6_set_under_its_3_6_name(string name) => Assert.NotNull(Core36.Entry(name));

    [Theory]
    [InlineData("Ignores Regeneration")]
    [InlineData("Ignores Cover")]
    [InlineData("Courage Aura")]
    [InlineData("Strafing")]
    [InlineData("Piercing Assault")]
    [InlineData("Counter-Attack")]
    [InlineData("Unique")]
    [InlineData("Quick Readjustment")]
    public void A_3_5_name_no_3_6_book_uses_is_not_in_the_3_6_set(string name) => Assert.Null(Core36.Entry(name));

    /// <summary>3.6.0 "Counter in Melee": "This model gets Counter on its weapons in melee"; Counter (3.6.0 p.13): "May strike first with this weapon when
    /// charged, unless this unit is Fatigued". The defender's team is asked, and a strike puts the spear first.</summary>
    [Fact]
    public void Counter_in_melee_asks_the_defender_and_strikes_first_under_3_6()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P("Counter in Melee")], Melee("Spear", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), 1, 1);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var first = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal(defender.Id, first.Context.ActorUnitId);
    }

    /// <summary>"This model gets Counter on its weapons in melee": a joined hero's rule (Gift of Lust) gives its own weapons Counter, not the unit's.</summary>
    [Fact]
    public void A_joined_hero_with_counter_in_melee_strikes_first_with_its_own_weapons_only()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var squad = Unit("Squad", "4+", "4+", [], 2, [], Melee("Knife", 1));
        var hero = Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Counter in Melee")], Melee("Spear", 1)).Models[0] with { Ordinal = 3 };
        var joined = squad with { Models = [.. squad.Models, hero] };
        var sim = Sim.Gf36(State([charger], [joined], x1: -3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Start(GfNames.Melee, MeleeOf(charger, joined));
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var first = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal(joined.Id, first.Context.ActorUnitId);
        Assert.Equal(hero.Weapons.Select(w => w.Id), first.Context.WeaponIds);
    }

    [Fact]
    public void A_fatigued_defender_with_counter_in_melee_strikes_with_the_rest_under_3_6()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P("Counter in Melee")], Melee("Spear", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), 1, 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, defender.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        Assert.Equal(charger.Id, sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence).Context.ActorUnitId);
    }

    /// <summary>Elven Jesters' Asphyxiating Fog (3.6.0): "Pick one friendly unit within 12", which gets Counter in Melee once (next time the effect would apply)."
    /// The strike first spends it, and the spear does not strike again in the strike back.</summary>
    [Fact]
    public void A_granted_counter_in_melee_strikes_first_once_and_is_spent()
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [], Melee("Claw", 1));
        var defender = Unit("Defender", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var sim = Sim.Gf36(State([charger], [defender], x1: -3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Append([new GrantAdded(defender.Id, new Grant(Guid.CreateVersion7(), Core36.Entry("Asphyxiating Fog")!.Effects[0].Id, "Asphyxiating Fog", defender.Id, 1))]);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal("counter", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Empty(sim.Unit(defender.Id).Grants);
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                   // the spear struck first: no strike back to offer
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).Select(s => s.Context.ActorUnitId!.Value);
        Assert.Equal([defender.Id, charger.Id], attacks);
    }

    /// <summary>3.6.0 "Crossing Strike" (3.5 Strafing, same text): "when this model moves through enemy units, pick one of them and attack it with this weapon
    /// as if it was shooting. This weapon may only be used in this way."</summary>
    [Fact]
    public void Crossing_strike_attacks_a_crossed_unit_with_its_weapon_and_never_shoots()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Flying")], Gun("Gun", "24\"", 1), Gun("Strafer", "24\"", 2, P("Crossing Strike")));
        var e = Unit("E", "4+", "4+", [], 1, []);
        var strafer = a.Models[0].Weapons.Single(w => w.Name == "Strafer").Id;
        var sim = Sim.Gf36(State([a], [e], x1: -3, x2: 3), Enumerable.Repeat(6, 20).ToArray());
        Assert.DoesNotContain(strafer, GfShooting.Groups(sim.State, Core36, a.Id, null).SelectMany(g => g.WeaponIds));
        sim.Append([new ActionRecorded(a.Id, GfNames.Advance)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        var start = ((OnTable)sim.Model(a.Models[0].Id).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(a.Models[0].Id, sim.Model(a.Models[0].Id).Location, new OnTable(start with { Position = start.Position with { X = start.Position.X + 5.5 } }))]));
        while (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React or AskQuestions.Confirm or "move" }) sim.Answer(Samples.Team1, new Passed());
        var pick = sim.AskFor(Samples.Team1);
        Assert.Equal("crossing_target", pick.Question);
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        var attack = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal([strafer], attack.Context.WeaponIds);
    }

    /// <summary>3.6.0 "Unique Hero": "Counts as having Hero" — p.15's "heroes must be assigned wounds last", and a joined hero's Quality for the unit's morale.</summary>
    [Fact]
    public void Unique_hero_counts_as_having_hero()
    {
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Unique Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var state = State([Unit("A", "4+", "4+", [], 1, [])], [t]).Under(RuleSets.Gf360);
        Assert.Equal([t.Models[0].Id], GfAttack.EligibleForNextWound(state, Core36, t.Id));

        var squad = Unit("Squad", "5+", "5+", [], 2, []);
        var joined = squad with { Models = [squad.Models[0] with { Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Unique Hero")] }, squad.Models[1]] };
        var sim = Sim.Gf36(State([joined], [Unit("E", "4+", "4+", [], 1, [])]), 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(joined.Id, [], [], "wounds"));
        Assert.Equal(1, sim.Steps("consequence").Single().Values!["passed"]);
    }
}
