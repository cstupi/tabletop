using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The corner's status: the game's name, which may be clipped, and a tail that always shows.</summary>
public class CornerLineTests
{
    [Fact]
    public void The_name_is_apart_from_the_status_and_watching_tail()
    {
        var game = Samples.Game with { Name = "suite 01a147e31cf1734f93dd8d2859d33687 special-aircraft-overhead" };
        Assert.Equal(("suite 01a147e31cf1734f93dd8d2859d33687 special-aircraft-overhead", "— playing — watching"), CornerLine.Of(SessionPhase.Connected, 0, game, watching: true));
        Assert.Equal(("suite 01a147e31cf1734f93dd8d2859d33687 special-aircraft-overhead", "— playing (reconnecting…)"), CornerLine.Of(SessionPhase.Reconnecting, 1, game, watching: false));
        Assert.Equal(("Friday", "— playing (reconnecting, attempt 3; Home stops it…)"), CornerLine.Of(SessionPhase.Reconnecting, 3, Samples.Game, watching: false));
        Assert.Equal(("Friday", "— playing — replay"), CornerLine.Of(SessionPhase.Connected, 0, Samples.Game, watching: true, replaying: true));
    }

    [Fact]
    public void Before_the_game_arrives_the_line_says_what_the_socket_is_doing()
    {
        Assert.Equal(("Connecting…", ""), CornerLine.Of(SessionPhase.Connecting, 0, null, watching: false));
        Assert.Equal(("Reconnecting…", ""), CornerLine.Of(SessionPhase.Reconnecting, 1, null, watching: false));
    }
}
