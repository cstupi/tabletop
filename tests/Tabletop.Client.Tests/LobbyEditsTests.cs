using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class LobbyEditsTests
{
    static readonly RulesOptionCatalog Catalog = new(
    [
        new RulesOption("a", "Option A", "p.1", [new RulesOptionValue("x", "X"), new RulesOptionValue("y", "Y")], "x"),
        new RulesOption("b", "Option B", "p.2", [new RulesOptionValue("on", "On"), new RulesOptionValue("off", "Off")], "on"),
    ], []);

    [Fact]
    public void Configure_trims_the_name_and_drops_default_options()
    {
        var chosen = new Dictionary<string, string> { ["a"] = "x", ["b"] = "off" };
        var edit = LobbyEdits.Configure(TableUx.Game, "  Friday  ", SchedulerShape.PlayerTurn, Enforcement.Strict, Catalog, chosen);
        Assert.Equal(("Friday", TableUx.Game.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Strict, false),
            (edit.Name, edit.PropertiesHash, edit.SchedulerShape, edit.Enforcement, edit.WaitsBetweenSteps));
        Assert.Equal(RulesOptions.MapOf(Catalog, chosen), edit.OptionalRules);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "off" }, edit.OptionalRules);
    }

    [Fact]
    public void Configure_sends_the_chosen_hash_or_the_games()
    {
        Assert.Equal("other", LobbyEdits.Configure(TableUx.Game, "n", SchedulerShape.PlayerTurn, Enforcement.Strict, Catalog, new Dictionary<string, string>(), "other").PropertiesHash);
        Assert.Equal(TableUx.Game.PropertiesHash, LobbyEdits.Configure(TableUx.Game, "n", SchedulerShape.PlayerTurn, Enforcement.Strict, Catalog, new Dictionary<string, string>()).PropertiesHash);
    }

    [Fact] public void Watch_is_pick_team_with_no_team() => Assert.Equal(new PickTeam(null), LobbyEdits.Watch());
    [Fact] public void Create_team_start_setup_and_end_setup_are_their_edits()
    {
        Assert.Equal(new CreateTeam(), LobbyEdits.CreateTeam());
        Assert.Equal(new StartSetup(), LobbyEdits.StartSetup());
        Assert.Equal(new EndSetup(), LobbyEdits.EndSetup());
    }

    [Fact]
    public void The_create_games_army_names_no_team()
    {
        var army = Guid.CreateVersion7();
        Assert.Equal(new PickArmy(army, null), LobbyEdits.ArmyFromCreate(army));
    }
}
