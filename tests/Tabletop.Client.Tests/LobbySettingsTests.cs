using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class LobbySettingsTests
{
    static readonly RulesOptionCatalog Catalog = new(
    [
        new RulesOption("a", "Option A", [new RulesOptionValue("x", "X"), new RulesOptionValue("y", "Y")], "x"),
    ], []);
    static readonly GameInfo Game = TableUx.Game with { Status = GameStatus.Lobby, SchedulerShape = SchedulerShape.AlternatingActivation, Enforcement = Enforcement.Warn };

    [Fact]
    public void Untouched_it_reads_the_games_values_and_sends_nothing()
    {
        var s = new LobbySettings();
        Assert.Equal((Game.Name, SchedulerShape.AlternatingActivation, Enforcement.Warn), (s.NameOf(Game), s.ShapeOf(Game), s.EnforcementOf(Game)));
        Assert.Equal("x", s.ChosenOf(Game, Catalog)["a"]);
        Assert.Null(s.Pending(Game, Catalog));
    }

    [Fact]
    public void An_edit_is_the_configure_start_setup_sends_first()
    {
        var s = new LobbySettings { Shape = SchedulerShape.PlayerTurn, Name = "  Saturday " };
        s.Choose("a", "y");
        var edit = s.Pending(Game, Catalog)!;
        Assert.Equal(("Saturday", Game.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Warn), (edit.Name, edit.PropertiesHash, edit.SchedulerShape, edit.Enforcement));
        Assert.Equal(new Dictionary<string, string> { ["a"] = "y" }, edit.OptionalRules);
        s.Clear();
        Assert.Null(s.Pending(Game, Catalog));
    }

    [Fact]
    public void Edits_back_to_the_games_values_send_nothing()
    {
        var s = new LobbySettings { Name = $" {Game.Name} ", Enforcement = Enforcement.Warn };
        s.Choose("a", "x");
        Assert.Null(s.Pending(Game, Catalog));
    }

    [Fact]
    public void A_rule_set_applies_alone_with_the_games_other_values()
    {
        var s = new LobbySettings { Shape = SchedulerShape.PlayerTurn };
        var edit = LobbySettings.RuleSet(Game with { OptionalRules = new Dictionary<string, string> { ["a"] = "y" } }, Catalog, "other");
        Assert.Equal(("other", Game.Name, SchedulerShape.AlternatingActivation), (edit.PropertiesHash, edit.Name, edit.SchedulerShape));
        Assert.Equal(new Dictionary<string, string> { ["a"] = "y" }, edit.OptionalRules);
        Assert.Equal(SchedulerShape.PlayerTurn, s.Shape);
    }

    [Fact]
    public void The_fold_reads_its_three_values_on_one_line() =>
        Assert.Equal("Grimdark Future v3.6.0 · Alternating · Warn", LobbySettings.FoldTitle("Grimdark Future v3.6.0", SchedulerShape.AlternatingActivation, Enforcement.Warn));
}
