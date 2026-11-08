using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The ruleset's answers the client is told rather than reading a rule (in-game UX spec → Model facts, Sight question).</summary>
public class ModelFactsTests
{
    static readonly Weapon Rifle = Gun("Rifle", "12\"", 1), Pistol = Gun("Pistol", "6\"", 1);

    [Fact]
    public void Max_wounds_is_Tough_else_one()
    {
        var (state, observers, targets, _) = SightLineTests.Table(0);
        Assert.Equal(3, GrimdarkFutureRules.Instance.MaxWounds(state, targets.Models[0].Id));
        Assert.Equal(1, GrimdarkFutureRules.Instance.MaxWounds(state, observers.Models[0].Id));
    }

    [Fact]
    public void Who_can_shoot_is_line_of_sight_and_a_weapon_in_range()
    {
        var (state, observers, targets, _) = SightLineTests.Table(-3.5, Rifle);                         // the wall hides every target from observer 1
        Assert.Equal([observers.Models[1].Id], GrimdarkFutureRules.Instance.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
    }

    [Fact]
    public void Nobody_shoots_out_of_range_or_without_a_ranged_weapon()
    {
        var (near, nearObservers, nearTargets, _) = SightLineTests.Table(0, Pistol);                    // 8.74" edge to edge: past 6"
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(near, TestProperties.Core, nearObservers.Id, nearTargets.Id));
        var (bare, bareObservers, bareTargets, _) = SightLineTests.Table(0, Melee("Blade", 2));
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(bare, TestProperties.Core, bareObservers.Id, bareTargets.Id));
    }

    /// <summary>Shadowborn (3.6 army rule): "When a unit where most models have this rule is targeted by an enemy unit for shooting/charging, that
    /// enemy unit gets -4"/-2" range to a min. of 6"." The 12" Rifle reaches 8" against it, short of the 8.74" between the units.</summary>
    static (GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets) Shadowborn()
    {
        var observers = Unit("Observers", "4+", "4+", [], 2, [], Rifle, Melee("Blade", 1));
        var targets = Unit("Targets", "4+", "4+", [], 2, [P("Shadowborn")]);
        return (State([observers], [targets], x1: -5, x2: 5).Under(RuleSets.Gf360), observers, targets);
    }

    [Fact]
    public void Shot_ranges_are_each_ranged_weapons_range_against_the_target_with_the_rules_that_changed_it()
    {
        var (state, observers, targets) = Shadowborn();
        Assert.Empty(GrimdarkFutureRules.V360.Shooters(state, TestProperties.Core36, observers.Id, targets.Id));
        var range = Assert.Single(GrimdarkFutureRules.V360.ShotRanges(state, TestProperties.Core36, observers.Id, targets.Id));
        Assert.Equal(("Rifle", 12.0, 8.0), (range.Weapon, range.Printed, range.Inches));
        Assert.Equal(["Shadowborn"], range.By);
    }

    [Fact]
    public void A_printed_range_nothing_changes_names_no_rule()
    {
        var (state, observers, targets, _) = SightLineTests.Table(0, Rifle);
        var range = Assert.Single(GrimdarkFutureRules.Instance.ShotRanges(state, TestProperties.Core, observers.Id, targets.Id));
        Assert.Equal(("Rifle", 12.0, 12.0), (range.Weapon, range.Printed, range.Inches));
        Assert.Empty(range.By);
        Assert.Empty(((IGameRules)new StubRules()).ShotRanges(state, TestProperties.Core, observers.Id, targets.Id));
    }

    [Fact]
    public void An_unknown_unit_has_no_shooters()
    {
        var (state, observers, _, _) = SightLineTests.Table(0, Rifle);
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(state, TestProperties.Core, observers.Id, Guid.CreateVersion7()));
    }

    [Fact]
    public void A_ruleset_that_says_nothing_answers_one_wound_and_no_shooters()
    {
        var (state, observers, targets, _) = SightLineTests.Table(0, Rifle);
        IGameRules stub = new StubRules();
        Assert.Equal(1, stub.MaxWounds(state, targets.Models[0].Id));
        Assert.Empty(stub.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
    }

    [Fact]
    public void A_wrapped_Grimdark_Future_still_answers_max_wounds_and_shooters()
    {
        var (state, observers, targets, _) = SightLineTests.Table(-3.5, Rifle);
        foreach (IGameRules wrapped in new IGameRules[] { new WithSequences(GrimdarkFutureRules.Instance), FakeRules.Gf() })
        {
            Assert.Equal(3, wrapped.MaxWounds(state, targets.Models[0].Id));
            Assert.Equal([observers.Models[1].Id], wrapped.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
            Assert.Equal("Rifle", Assert.Single(wrapped.ShotRanges(state, TestProperties.Core, observers.Id, targets.Id)).Weapon);
        }
    }
}
