using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The corner's line: the game's name, which may be clipped, the status chip with the game's real phase, and a reconnect tail
/// that always shows.</summary>
public class CornerLineTests
{
    [Fact]
    public void The_chip_names_the_phase_the_game_is_in()
    {
        Assert.Equal(new CornerStatus("Friday", "Lobby", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game with { Status = GameStatus.Lobby }, 0, false));
        Assert.Equal(new CornerStatus("Friday", "Setup", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game with { Status = GameStatus.Setup }, 0, false));
        Assert.Equal(new CornerStatus("Friday", "Deployment", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game, 0, false));
        Assert.Equal(new CornerStatus("Friday", "Round 3", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game, 3, false));
        Assert.Equal(new CornerStatus("Friday", "Ended", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game with { Status = GameStatus.Ended }, 4, false));
        Assert.Equal(new CornerStatus("Friday", "Watching", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game, 3, watching: true));
        Assert.Equal(new CornerStatus("Friday", "Replay", ""), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game, 3, watching: true, replaying: true));
    }

    [Fact]
    public void A_reconnect_is_the_tail_after_the_chip()
    {
        var game = Samples.Game with { Name = "suite 01a147e31cf1734f93dd8d2859d33687 special-aircraft-overhead" };
        Assert.Equal(new CornerStatus(game.Name, "Round 2", "reconnecting…"), CornerLine.Of(SessionPhase.Reconnecting, 1, game, 2, watching: false));
        Assert.Equal(new CornerStatus("Friday", "Round 2", "reconnecting, attempt 3; Home stops it…"), CornerLine.Of(SessionPhase.Reconnecting, 3, Samples.Game, 2, watching: false));
    }

    [Fact]
    public void Before_the_game_arrives_the_line_says_what_the_socket_is_doing()
    {
        Assert.Equal(new CornerStatus("Connecting…", null, ""), CornerLine.Of(SessionPhase.Connecting, 0, null, 0, watching: false));
        Assert.Equal(new CornerStatus("Reconnecting…", null, ""), CornerLine.Of(SessionPhase.Reconnecting, 1, null, 0, watching: false));
    }
}
