using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Management's leave deletes a participant row without telling the game server (the client's Leave: `POST /leave`, then its socket closes).
/// The session reads the rows again where a lobby or setup reads its participants: a socket closing, a socket connecting, the end of setup.
/// </summary>
public class LobbyLeaveTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static readonly Ask Deploy = new(Guid.CreateVersion7(), AskQuestions.Deploy, "Deploy", [], new PieceSelection([], 1, 1), false, false, null, null);

    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Leaver)> LobbyAsync()
    {
        var factory = new GameServerFactory(db) { ConfigureTestServices = s => s.AddSingleton<IRulesEngineFactory>(new FixedAskEngineFactory(Deploy)) };
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var creator = await TestClient.OpenAsync(factory); var leaver = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId); await creator.ExpectAsync<AtRest>();
        await leaver.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId); await leaver.ExpectAsync<AtRest>();
        return (factory, seeded, creator, leaver);
    }

    [Fact]
    public async Task A_player_who_left_is_gone_from_the_lobby_when_their_socket_closes()
    {
        var (f, g, creator, leaver) = await LobbyAsync();
        using var _ = f; await using var __ = creator;
        db.GameRows.RemoveParticipant(g.GameId, g.Participants[1].AccountId);
        await leaver.DisposeAsync();
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.DoesNotContain(info.Participants, p => p.AccountId == g.Participants[1].AccountId);
    }

    [Fact]
    public async Task A_player_who_left_is_gone_from_the_lobby_whoever_connects_next()
    {
        var (f, g, creator, leaver) = await LobbyAsync();
        using var _ = f; await using var __ = creator; await using var ___ = leaver;
        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>();
        db.GameRows.RemoveParticipant(g.GameId, g.Participants[1].AccountId);
        await using var again = await TestClient.OpenAsync(f);
        var welcome = await again.ConnectAsync(g.CreatorAccountId, g.GameId);
        Assert.DoesNotContain(welcome.Game.Participants, p => p.AccountId == g.Participants[1].AccountId);
        Assert.DoesNotContain((await creator.ExpectAsync<GameInfoMessage>()).Game.Participants, p => p.AccountId == g.Participants[1].AccountId);
    }

    [Fact]
    public async Task A_player_who_left_brings_no_army_to_the_end_of_setup()
    {
        var (f, g, creator, leaver) = await LobbyAsync();
        using var _ = f; await using var __ = creator; await using var ___ = leaver;
        var mine = await db.SeedArmyAsync(g.CreatorAccountId);
        var theirs = await db.SeedArmyAsync(g.Participants[1].AccountId);
        await creator.SendAsync(Edit(new PickArmy(mine.Id))); await creator.ExpectAsync<GameInfoMessage>();
        await leaver.SendAsync(Edit(new PickArmy(theirs.Id))); await creator.ExpectAsync<GameInfoMessage>();
        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>();

        db.GameRows.RemoveParticipant(g.GameId, g.Participants[1].AccountId);       // the leave, with the leaver's socket still open
        await creator.SendAsync(Edit(new EndSetup()));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.DoesNotContain(info.Participants, p => p.AccountId == g.Participants[1].AccountId);
        var board = (await creator.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal([g.Participants[0].ParticipantId], board.Armies.Select(a => a.ParticipantId));
        Assert.Equal(0L, (await creator.ExpectAsync<AtRest>()).Seq);                  // team 2 has no player: deployment waits
    }
}
