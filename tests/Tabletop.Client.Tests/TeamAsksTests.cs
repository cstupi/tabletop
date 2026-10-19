using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Rulebook p.50 Team Games and the owner ruling ("Either player from the team can take the activation across any unit in their
/// team."): Clay and Cid share Team 1, each with their own army; every Ask to Team 1 is either player's, for any unit of the team.</summary>
public class TeamAsksTests
{
    static readonly Guid Cid = Guid.CreateVersion7();
    static readonly Army CidsArmy = ArmyTests.Sample();
    static readonly GameInfo Game = Samples.Game with { Participants =
        [new Participant(Samples.P1, Guid.CreateVersion7(), "Clay", Samples.Team1, null), new Participant(Cid, Guid.CreateVersion7(), "Cid", Samples.Team1, null)] };
    static readonly Board Board = Samples.Board with { Armies = [.. Samples.Board.Armies, new ArmyInPlay(Cid, ArmySnapshot.Of(CidsArmy, Guid.CreateVersion7()))] };
    static Guid Clays => Samples.Unit;
    static Guid Cids => CidsArmy.Units[0].Id;

    static Projection With(Ask ask, params EventPayload[] after)
    {
        var applier = new EventApplier(Board, Game);
        applier.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        foreach (var e in after) applier.Apply(new GameEvent(applier.Projection.LastSeq + 1, null, e, null));
        return applier.Projection;
    }

    static Ask Activate => new(Guid.CreateVersion7(), AskQuestions.Activate, "Activate a unit", [Samples.Team1],
        new Options([new AskOption(Clays.ToString("N") + "/Hold", "Clay's: Hold", [Clays]), new AskOption(Cids.ToString("N") + "/Hold", "Cid's: Hold", [Cids])]), false, false, null, null);

    [Fact]
    public void On_my_teams_turn_I_am_offered_every_unit_of_the_team()
    {
        var p = With(Activate);
        var mine = AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>())!;
        Assert.Equal([Clays, Cids], mine.Highlights);
        Assert.Equal(["Clay's: Hold", "Cid's: Hold"], ((Options)mine.Ask.Legal).Choices.Select(c => c.Label));
    }

    [Fact]
    public void A_pick_offering_only_a_teammates_units_is_still_my_move()
    {
        var ask = Activate with { Legal = new PieceSelection([Cids], 1, 1), Question = AskQuestions.Deploy };
        var p = With(ask);
        Assert.NotNull(AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>()));
        Assert.Equal("Your move: deploy", HeaderView.Waiting(p, new Names(Game, p), [Samples.Team1]));
    }

    [Fact]
    public void Either_player_places_the_teams_deploy_pick()
    {
        var ask = Activate with { Question = AskQuestions.Deploy, Legal = new PieceSelection([Clays, Cids], 1, 1) };
        var p = With(ask, new AskAnswered(ask.Id, Samples.Team1, new SelectedPieces([Cids]), false), new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Cids]))]));
        var log = new[] { new GameEvent(1, null, new AskOpened(ask), null), new GameEvent(3, Cid, new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Cids]))]), null) };
        Assert.Equal(Cids, DeployView.MidDeploy(p, log, Samples.Team1)!.UnitId);
    }
}
