using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Rulebook p.50 Team Games: Clay and Cid share Team 1, each with their own army; on the team's turn each is offered only their own units,
/// and an Ask about one of Cid's units is Cid's.</summary>
public class SeatPicksTests
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

    static Ask Activate => new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate a unit", [Samples.Team1],
        new Options([new AskOption(Clays.ToString("N") + "/Hold", "Clay's: Hold", [Clays]), new AskOption(Cids.ToString("N") + "/Hold", "Cid's: Hold", [Cids])]), false, false, null, null) { OwnUnitsOnly = true };

    [Fact]
    public void On_my_teams_turn_I_am_offered_only_my_own_units()
    {
        var p = With(Activate);
        var mine = AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>(), Samples.P1)!;
        Assert.Equal([Clays], mine.Highlights);
        Assert.Equal(["Clay's: Hold"], ((Options)mine.Ask.Legal).Choices.Select(c => c.Label));
        Assert.Equal(["Cid's: Hold"], ((Options)AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>(), Cid)!.Ask.Legal).Choices.Select(c => c.Label));
    }

    [Fact]
    public void A_pick_offering_only_a_teammates_units_is_theirs_and_the_header_names_them()
    {
        var ask = Activate with { Legal = new PieceSelection([Cids], 1, 1), Question = AskQuestions.Deploy };
        var p = With(ask);
        Assert.Null(AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>(), Samples.P1));
        Assert.Equal("Team 1 (Cid): deploy", HeaderView.Waiting(p, new Names(Game, p), [Samples.Team1], Samples.P1, Game));
        Assert.Equal("Your move: deploy", HeaderView.Waiting(p, new Names(Game, p), [Samples.Team1], Cid, Game));
    }

    [Fact]
    public void An_ask_about_a_teammates_unit_is_theirs_alone()
    {
        var ask = new Ask(Guid.CreateVersion7(), "remove_casualties", "Assign 1 wound(s)", [Samples.Team1], new Allocation([CidsArmy.Units[0].Models[0].Id], 1), false, false, null, Guid.CreateVersion7()) { Seat = Cid };
        var p = With(ask);
        Assert.Null(AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>(), Samples.P1));
        Assert.NotNull(AskViewModel.Mine(p, Game, Samples.Team1, new HashSet<string>(), Cid));
        Assert.Equal("Team 1 (Cid): remove casualties", HeaderView.Waiting(p, new Names(Game, p), [Samples.Team1], Samples.P1, Game));
    }

    [Fact]
    public void Only_the_player_who_picked_a_unit_to_deploy_places_it()
    {
        var ask = Activate with { Question = AskQuestions.Deploy, Legal = new PieceSelection([Clays, Cids], 1, 1) };
        var p = With(ask, new AskAnswered(ask.Id, Samples.Team1, new SelectedPieces([Cids]), false), new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Cids]))]));
        var log = new[] { new GameEvent(1, null, new AskOpened(ask), null), new GameEvent(3, Cid, new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Cids]))]), null) };
        Assert.Null(DeployView.MidDeploy(p, log, Samples.Team1, Game, Samples.P1));
        Assert.Equal(Cids, DeployView.MidDeploy(p, log, Samples.Team1, Game, Cid)!.UnitId);
    }
}
