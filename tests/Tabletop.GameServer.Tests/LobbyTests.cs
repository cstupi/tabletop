using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class LobbyTests(PostgresFixture pg)
{
    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Other)> LobbyAsync()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var creator = await TestClient.OpenAsync(factory); var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        return (factory, seeded, creator, other);
    }
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    [Fact]
    public async Task Creating_a_team_and_picking_it_writes_rows_and_broadcasts_the_game_info()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new CreateTeam()));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal([1, 2, 3], info.Teams.Select(t => t.Ordinal));
        await other.ExpectAsync<GameInfoMessage>();
        var third = info.Teams[2].Id;
        await other.SendAsync(Edit(new PickTeam(third)));
        Assert.Equal(third, (await creator.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.Participants[1].AccountId).TeamId);
        await other.SendAsync(Edit(new PickTeam(null)));
        Assert.Null((await creator.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.Participants[1].AccountId).TeamId);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where id = $1 and team_id is null", g.Participants[1].ParticipantId));
        await other.SendAsync(Edit(new PickTeam(Guid.CreateVersion7())));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Picking_an_army_requires_owning_it()
    {
        var (f, g, creator, other) = await LobbyAsync();
        var mine = await pg.SeedArmyAsync(g.CreatorAccountId);
        await creator.SendAsync(Edit(new PickArmy(mine.Id)));
        Assert.Equal(mine.Id, (await other.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.CreatorAccountId).ArmyId);
        await other.SendAsync(Edit(new PickArmy(mine.Id)));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await other.SendAsync(Edit(new PickArmy(Guid.CreateVersion7())));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Configuration_changes_need_a_known_properties_hash()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new SetConfiguration("Saturday", g.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Strict, true)));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal(("Saturday", SchedulerShape.PlayerTurn, Enforcement.Strict, true), (info.Name, info.SchedulerShape, info.Enforcement, info.WaitsBetweenSteps));
        await other.SendAsync(Edit(new SetConfiguration("x", "sha256:nope", SchedulerShape.PlayerTurn, Enforcement.Strict, true)));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Only_the_creator_starts_setup_and_it_creates_the_draft_row()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new StartSetup()));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await creator.SendAsync(Edit(new StartSetup()));
        Assert.Equal(GameStatus.Setup, (await other.ExpectAsync<GameInfoMessage>()).Game.Status);
        var board = (await other.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal((72.0, 48.0, "grass"), (board.Table.Size.Width, board.Table.Size.Length, board.Table.Texture.Builtin));
        Assert.Empty(board.Armies);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.table_state where game_id = $1", g.GameId));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1 and status = 'setup'", g.GameId));
        await creator.SendAsync(Edit(new StartSetup()));
        Assert.Equal(RefusalReason.WrongStatus, (await creator.ExpectAsync<Refused>()).Reason);
        await other.SendAsync(Edit(new CreateTeam()));                       // still editable during setup
        Assert.Equal(3, (await creator.ExpectAsync<GameInfoMessage>()).Game.Teams.Count);
    }

    [Fact]
    public async Task Lobby_edits_are_refused_from_playing_on()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing);
        await pg.SeedTableStateAsync(seeded.GameId);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.SendAsync(Edit(new CreateTeam()));
        Assert.Equal(RefusalReason.WrongStatus, (await client.ExpectAsync<Refused>()).Reason);
    }

    /// <summary>Management's join inserts the participant row whenever it likes, and hydration read the rows once. A
    /// participant the session has never seen re-reads them on connect, so Welcome carries their row and their first
    /// lobby edit finds them instead of throwing out of the handshake.</summary>
    [Fact]
    public async Task A_participant_who_joins_after_hydration_is_in_their_welcome_and_can_pick_a_team()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        await using var creator = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);                 // hydrates the session from the rows as they stand
        var joined = await pg.SeedParticipantAsync(seeded.GameId, await pg.SeedAccountAsync("Latecomer"), null);

        await using var late = await TestClient.OpenAsync(factory);
        var welcome = await late.ConnectAsync(joined.AccountId, seeded.GameId);
        Assert.Equal(joined.ParticipantId, welcome.ParticipantId);
        Assert.Contains(welcome.Game.Participants, p => p.Id == joined.ParticipantId);

        await late.SendAsync(Edit(new PickTeam(seeded.TeamIds[1])));
        Assert.Equal(seeded.TeamIds[1], (await late.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.Id == joined.ParticipantId).TeamId);
    }

    // The team list is locked at the flip to playing (game flow → Teams are frozen at the flip, and a team is a slot),
    // and a participant who reconnects during play is returned to the slot their participant row already names.
    [Fact]
    public async Task Teams_are_locked_from_playing_on_but_a_rejoining_participant_keeps_their_team()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing, teams: 2, spectators: 1);
        await pg.SeedTableStateAsync(seeded.GameId);
        await using var player = await TestClient.OpenAsync(factory);
        var welcome = await player.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[1], welcome.Game.Participants.Single(p => p.Id == welcome.ParticipantId).TeamId);
        await player.SendAsync(Edit(new PickTeam(seeded.TeamIds[0])));
        Assert.Equal(RefusalReason.WrongStatus, (await player.ExpectAsync<Refused>()).Reason);   // no re-picking during play
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where id = $1 and team_id = $2", seeded.Participants[1].ParticipantId, seeded.TeamIds[1]));

        await using var again = await TestClient.OpenAsync(factory);                             // the same account, a new socket
        var back = await again.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[1], back.Game.Participants.Single(p => p.Id == back.ParticipantId).TeamId);

        await using var watcher = await TestClient.OpenAsync(factory);                           // never had a slot, so watches
        var watching = await watcher.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId);
        Assert.Null(watching.Game.Participants.Single(p => p.Id == watching.ParticipantId).TeamId);
    }
}
