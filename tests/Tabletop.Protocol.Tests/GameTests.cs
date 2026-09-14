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
    public void Game_info_carries_the_stored_rng_seed_whole()
    {
        // long.MinValue + 1: signed, and past 2^53, so a JSON number that went through a double would come back changed.
        var game = new GameInfo(Guid.CreateVersion7(), "Friday", GameStatus.Lobby, Guid.CreateVersion7(),
            "grimdark-future", "sha256:abc", SchedulerShape.PlayerTurn, Enforcement.Warn, false, -9223372036854775807L, [], []);
        var json = ContentJson.Serialize(game);
        Assert.Contains("\"rngSeed\":-9223372036854775807", json);
        Assert.Equal(game.RngSeed, ContentJson.Deserialize<GameInfo>(json).RngSeed);
    }

    [Fact]
    public void Version_check_constants() => Assert.Equal((1, 426), (ProtocolVersion.Current, ProtocolVersion.UpgradeRequiredStatusCode));
}
