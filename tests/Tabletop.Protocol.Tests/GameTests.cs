using Tabletop.Content;

namespace Tabletop.Protocol.Tests;

public class GameTests
{
    [Fact]
    public void Scheduler_shape_uses_the_hyphenated_wire_names()
    {
        var json = ContentJson.Serialize(SchedulerShape.AlternatingActivation);
        Assert.Equal("\"alternating-activation\"", json);
        Assert.Equal(SchedulerShape.PlayerTurn, ContentJson.Deserialize<SchedulerShape>("\"player-turn\""));
    }

    [Fact]
    public void Location_is_one_of_three_places()
    {
        Location[] all = [new OnTable(new(new(1, 2, 0), new(90))), new OnShelf(), new Inside(Guid.CreateVersion7())];
        Assert.Equal(3, all.Select(l => l.GetType()).Distinct().Count());
    }

    [Fact]
    public void Game_info_never_puts_the_rng_seed_on_the_wire()
    {
        // Every die is a pure function of the seed, so a client that knew it could predict every roll.
        var game = new GameInfo(Guid.CreateVersion7(), "Friday", GameStatus.Lobby, Guid.CreateVersion7(),
            "grimdark-future", "sha256:abc", SchedulerShape.PlayerTurn, Enforcement.Warn, false, -9223372036854775807L, [], []);
        var json = ContentJson.Serialize(game);
        Assert.DoesNotContain("rngSeed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9223372036854775807", json);
        Assert.Equal(0L, ContentJson.Deserialize<GameInfo>(json).RngSeed);
        Assert.Equal(0L, ContentJson.Deserialize<GameInfo>(json.Replace("\"teams\"", "\"rngSeed\":42,\"teams\"")).RngSeed);   // a sent one is ignored too
        Assert.DoesNotContain("rngSeed", WireJson.Serialize<SocketMessage>(new GameInfoMessage(game)));
    }

    [Fact]
    public void Version_check_constants() => Assert.Equal((1, 426), (ProtocolVersion.Current, ProtocolVersion.UpgradeRequiredStatusCode));
}
