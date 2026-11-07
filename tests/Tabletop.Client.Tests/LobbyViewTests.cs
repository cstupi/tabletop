using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class LobbyViewTests
{
    static readonly Guid Me = Guid.CreateVersion7(), Other = Guid.CreateVersion7();

    static GameInfo Lobby(Guid? myTeam, Guid? otherTeam, Guid? myArmy = null) => Samples.Game with
    {
        Status = GameStatus.Lobby,
        Participants = [new Participant(Me, Guid.CreateVersion7(), "Clay", myTeam, myArmy), new Participant(Other, Guid.CreateVersion7(), "Ana", otherTeam, null)],
    };

    [Fact]
    public void Army_label_is_name_and_edition()
    {
        Assert.Equal("Orcs (3.6)", LobbyView.ArmyLabel(new ArmySummary(Guid.Empty, "Orcs", "gf", "x", 1, 1, "3.6")));
        Assert.Equal("Orcs (3.5)", LobbyView.ArmyLabel(new ArmySummary(Guid.Empty, "Orcs", "gf", "x", 1, 1)));
    }

    [Fact]
    public void Army_options_list_the_games_edition_first_and_the_other_disabled_with_the_reason()
    {
        ArmySummary old = new(Guid.CreateVersion7(), "Orcs", "gf", "x", 1, 1, "3.5"), current = new(Guid.CreateVersion7(), "Elves", "gf", "y", 2, 3, "3.6");
        var in36 = LobbyView.ArmyOptions(Samples.Game with { GameRules = "grimdark-future-3.6" }, [old, current]);
        Assert.Equal([(current.Id, "Elves (3.6)", (string?)null), (old.Id, "Orcs (3.5)", "3.5 list: re-import it from Army Forge on the website")],
            in36.Select(o => (o.Army.Id, o.Label, o.DisabledReason)));
        Assert.Equal(["Elves (3.6)", "Orcs (3.5) · 3.5 list: re-import it from Army Forge on the website"], in36.Select(o => o.Text));

        var in35 = LobbyView.ArmyOptions("grimdark-future", [old, current]);
        Assert.Equal([(old.Id, (string?)null), (current.Id, "3.6 list: Army Forge no longer serves 3.5 lists")], in35.Select(o => (o.Army.Id, o.DisabledReason)));
        Assert.Equal([true, false], in35.Select(o => o.Enabled));
    }

    /// <summary>Self-play: one account on both teams, a row per team, an army picker per team.</summary>
    [Fact]
    public void One_account_on_both_teams_picks_an_army_for_each()
    {
        var account = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var army = Guid.CreateVersion7();
        var game = Samples.Game with { Status = GameStatus.Lobby, Participants =
            [new Participant(Me, account, "Clay", Samples.Team1, army), new Participant(second, account, "Clay", Samples.Team2, null)] };
        var armies = LobbyView.Armies(game, Me);
        Assert.Equal([("Your army for Team 1", (Guid?)army), ("Your army for Team 2", null)], armies.Select(a => (a.Label, a.ArmyId)));
        Assert.Equal(new PickArmy(army, Samples.Team2), armies[1].Pick(army));
        Assert.Equal([new ArmySeat(null, "Your army", null)], LobbyView.Armies(Lobby(null, null), Me));
    }

    [Fact]
    public void End_setup_counts_the_pieces_off_the_table_in_table_words()
    {
        var table = Tabletop.Content.Tests.TableStateTests.Sample();
        var piece = table.Terrain[0];
        Tabletop.Content.TableState With(params (double X, double Y)[] at) =>
            table with { Terrain = at.Select(p => piece with { Id = Guid.CreateVersion7(), Position = new Tabletop.Geometry.Point3(p.X, p.Y, 0) }).ToList() };
        Assert.Equal("End setup", LobbyView.EndSetupText(null));
        Assert.Equal("End setup", LobbyView.EndSetupText(With((72, 48), (10, 10))));      // the edge is on the table
        Assert.Equal("End setup (1 piece off the table will be removed)", LobbyView.EndSetupText(With((76, 4), (10, 10))));
        Assert.Equal("End setup (3 pieces off the table will be removed)", LobbyView.EndSetupText(With((76, 4), (-1, 4), (10, 49))));
    }

    /// <summary>Rulebook p.50 Team Games: two players on a side, each with their own army picker.</summary>
    [Fact]
    public void Two_players_on_one_team_each_pick_their_own_army()
    {
        var game = Lobby(Samples.Team1, Samples.Team1);
        Assert.Equal([new ArmySeat(Samples.Team1, "Your army", null)], LobbyView.Armies(game, Me));
        Assert.Equal([new ArmySeat(Samples.Team1, "Your army", null)], LobbyView.Armies(game, Other));
    }
}
