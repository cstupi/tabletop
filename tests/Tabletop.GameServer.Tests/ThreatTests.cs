using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Threat range plan: a unit's reach in its next activation (p.7 Advance, Rush, Charge; p.13–15 the rules that change them), both editions.</summary>
public class ThreatTests
{
    static readonly Weapon Rifle = Gun("Rifle", "24\"", 1), Pistol = Gun("Pistol", "12\"", 1);

    static (UnitThreat Threat, Unit Unit, GameState State) Of(string rules, Unit unit, Action<GameState>? change = null)
    {
        var state = State([unit], [Unit("B", "4+", "4+", [], 1, [], Rifle)]);
        if (rules == RuleSets.Gf360) state = state.Under(RuleSets.Gf360);
        change?.Invoke(state);
        var (gf, props) = rules == RuleSets.Gf360 ? (GrimdarkFutureRules.V360, TestProperties.Core36) : (GrimdarkFutureRules.Instance, TestProperties.Core);
        return (gf.Threats(state, props)[unit.Id], unit, state);
    }

    static Unit Plain(PropertyInstance[] modelProps, params Weapon[] weapons) => Unit("A", "4+", "4+", [], 2, modelProps, weapons);

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_plain_unit_charges_twelve_and_shoots_after_advancing_six(string rules)
    {
        var (t, u, _) = Of(rules, Plain([], Rifle, Pistol));
        Assert.Equal((12.0, 6.0), (t.Charge, t.ShootMove));
        Assert.Equal(u.Models.ToDictionary(m => m.Id, _ => 24.0), t.Ranges);       // each model's longest range
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Fast_moves_two_more_on_advance_and_four_more_on_charge(string rules)
    {
        var (t, _, _) = Of(rules, Plain([P("Fast")], Rifle));
        Assert.Equal((16.0, 8.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Slow_moves_two_less_on_advance_and_four_less_on_charge(string rules)
    {
        var (t, _, _) = Of(rules, Plain([P("Slow")], Rifle));
        Assert.Equal((8.0, 4.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Aircraft_never_charges_and_shoots_after_its_advance_plus_thirty(string rules)
    {
        var (t, _, _) = Of(rules, Unit("A", "4+", "4+", [P("Aircraft")], 1, [], Rifle));
        Assert.Equal((null, 36.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Flying_changes_no_distance(string rules)
    {
        var (t, _, _) = Of(rules, Plain([P("Flying")], Rifle));
        Assert.Equal((12.0, 6.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Immobile_only_holds(string rules)
    {
        var (t, _, _) = Of(rules, Plain([P("Immobile")], Rifle));
        Assert.Equal((null, 0.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, null, 0.0)]          // 3.5.1 p.13: "May only use Hold actions."
    [InlineData(RuleSets.Gf360, 12.0, 6.0)]          // 3.6.0 p.13: no restriction on actions
    public void Artillery_holds_only_under_3_5_1(string rules, double? charge, double move)
    {
        var (t, _, _) = Of(rules, Plain([P("Artillery")], Rifle));
        Assert.Equal((charge, move), (t.Charge, t.ShootMove));
    }

    [Fact]
    public void Cumbersome_under_3_6_never_charges_and_shoots_only_standing()
    {
        var (t, _, _) = Of(RuleSets.Gf360, Plain([P("Cumbersome")], Rifle));
        Assert.Equal((null, 0.0), (t.Charge, t.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_shaken_unit_threatens_nothing(string rules)
    {
        var unit = Plain([], Rifle);
        var (t, _, _) = Of(rules, unit, s => s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, unit.Id), GfNames.Shaken, true), null)));
        Assert.Equal((null, (double?)null), (t.Charge, t.ShootMove));
    }

    [Fact]
    public void A_shaken_aircraft_moves_but_shoots_nothing()
    {
        var unit = Unit("A", "4+", "4+", [P("Aircraft")], 1, [], Rifle);
        var (t, _, _) = Of(RuleSets.Gf351, unit, s => s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, unit.Id), GfNames.Shaken, true), null)));
        Assert.Equal((null, (double?)null), (t.Charge, t.ShootMove));
    }

    [Fact]
    public void Quick_shot_shoots_after_a_rush()
    {
        var (t, _, _) = Of(RuleSets.Gf351, Plain([P("Quick Shot")], Rifle));
        Assert.Equal((12.0, 12.0), (t.Charge, t.ShootMove));
    }

    [Fact]
    public void A_range_rule_that_holds_when_shooting_lengthens_the_range()
    {
        var (t, u, _) = Of(RuleSets.Gf351, Plain([P("Increased Shooting Range")], Rifle));
        Assert.All(u.Models, m => Assert.Equal(30.0, t.Ranges[m.Id]));
    }

    [Fact]
    public void A_melee_only_unit_charges_and_has_no_range()
    {
        var (t, _, _) = Of(RuleSets.Gf351, Plain([], Melee("Blade", 2)));
        Assert.Equal((12.0, 6.0), (t.Charge, t.ShootMove));
        Assert.Empty(t.Ranges);
    }

    [Fact]
    public void A_unit_off_the_table_has_no_threat_and_a_ruleset_that_says_nothing_answers_none()
    {
        var reserve = Plain([], Rifle) with { Reserve = new UnitReserve("Spawn", Guid.CreateVersion7()) };
        var state = State([reserve, Plain([], Rifle)], [Unit("B", "4+", "4+", [], 1, [], Rifle)]);
        Assert.DoesNotContain(reserve.Id, GrimdarkFutureRules.Instance.Threats(state, TestProperties.Core).Keys);
        Assert.Empty(((IGameRules)new StubRules()).Threats(state, TestProperties.Core));
        Assert.Equal(2, new WithSequences(GrimdarkFutureRules.Instance).Threats(state, TestProperties.Core).Count);
        Assert.Equal(2, FakeRules.Gf().Threats(state, TestProperties.Core).Count);
    }
}
