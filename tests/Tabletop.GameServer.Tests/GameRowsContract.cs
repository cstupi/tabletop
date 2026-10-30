using Tabletop.Geometry;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>IGameRows as the game server relies on it, run against InMemoryGameRows (unit) and PostgresGameRows (E2E).</summary>
public abstract class GameRowsContract
{
    protected abstract IRowSeeder Seeder { get; }
    IGameRows Rows => Seeder.Rows;
    static CancellationToken Ct => CancellationToken.None;

    async Task<GameStatus> StatusOf(Guid gameId) => (await Rows.ReadGameAsync(gameId, Ct))!.Status;

    [Fact]
    public async Task Reads_a_lobby_game_with_teams_and_participants()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 1, spectators: 1);
        var game = await Rows.ReadGameAsync(seeded.GameId, Ct);
        Assert.NotNull(game);
        Assert.Equal((GameStatus.Lobby, "Friday", seeded.CreatorAccountId, "grimdark-future", SchedulerShape.AlternatingActivation, Enforcement.Warn),
            (game.Status, game.Name, game.CreatedBy, game.GameRules, game.SchedulerShape, game.Enforcement));
        Assert.False(string.IsNullOrEmpty(game.RuleSet));   // the properties row's name
        Assert.Equal(seeded.RngSeed, game.RngSeed);      // the stored dice seed arrives with the rest of the row; this server never writes it
        Assert.Equal([1, 2], game.Teams.Select(t => t.Ordinal));
        Assert.Equal(3, game.Participants.Count);
        Assert.Equal(game.Participants.Select(p => p.Id).Order(), game.Participants.Select(p => p.Id));
        Assert.Contains(game.Participants, p => p.TeamId is null && p.Name == "Watcher 0");
        Assert.Contains(game.Participants, p => p.AccountId == seeded.CreatorAccountId && p.Name == "Creator" && p.TeamId == seeded.TeamIds[0]);
        Assert.Null(await Rows.ReadDraftAsync(seeded.GameId, Ct));
        Assert.Empty(await Rows.ReadSnapshotsAsync(seeded.GameId, Ct));
        Assert.True(await Rows.PropertiesExistAsync(seeded.PropertiesHash, Ct));
        Assert.False(await Rows.PropertiesExistAsync("sha256:nope", Ct));
    }

    [Fact]
    public async Task Finds_a_participant_by_game_and_account()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        Assert.Equal(seeded.Participants[0].ParticipantId, await Rows.FindParticipantAsync(seeded.GameId, seeded.CreatorAccountId, Ct));
        Assert.Null(await Rows.FindParticipantAsync(seeded.GameId, Guid.CreateVersion7(), Ct));
        Assert.Null(await Rows.ReadGameAsync(Guid.CreateVersion7(), Ct));
        Assert.Null(await Rows.ReadArmyAsync(Guid.CreateVersion7(), Ct));
    }

    [Fact]
    public async Task An_account_holds_one_seat_per_team_and_its_oldest_row_is_the_one_found()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var first = seeded.Participants[0].ParticipantId;
        var second = Session.GameSession.SeatIdAfter(first);
        await Rows.AddSeatAsync(second, seeded.GameId, seeded.CreatorAccountId, seeded.TeamIds[1], Ct);
        Assert.Equal([seeded.TeamIds[0], seeded.TeamIds[1]], (await Rows.ReadGameAsync(seeded.GameId, Ct))!.Participants
            .Where(p => p.AccountId == seeded.CreatorAccountId).Select(p => p.TeamId));
        Assert.Equal(first, await Rows.FindParticipantAsync(seeded.GameId, seeded.CreatorAccountId, Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => Rows.AddSeatAsync(Guid.CreateVersion7(), seeded.GameId, seeded.CreatorAccountId, seeded.TeamIds[1], Ct));   // a team once per account
        await Rows.RemoveSeatAsync(second, Ct);
        Assert.Single((await Rows.ReadGameAsync(seeded.GameId, Ct))!.Participants, p => p.AccountId == seeded.CreatorAccountId);
    }

    [Fact]
    public async Task An_army_joins_a_playing_game_once()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Playing, teams: 2);
        var army = await Seeder.SeedArmyAsync(seeded.Participants[1].AccountId);
        var snapshot = ArmySnapshot.Of(army, Guid.CreateVersion7());
        await Rows.JoinArmyAsync(seeded.GameId, seeded.Participants[1].ParticipantId, army.Id, snapshot, Ct);
        var joined = Assert.Single(await Rows.ReadSnapshotsAsync(seeded.GameId, Ct));
        Assert.Equal((seeded.Participants[1].ParticipantId, snapshot.Id), (joined.ParticipantId, joined.Snapshot.Id));
        Assert.Equal(army.Id, (await Rows.ReadGameAsync(seeded.GameId, Ct))!.Participants.Single(p => p.Id == seeded.Participants[1].ParticipantId).ArmyId);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Rows.JoinArmyAsync(seeded.GameId, seeded.Participants[1].ParticipantId, army.Id, snapshot, Ct));
        var lobby = await Seeder.SeedGameAsync(GameStatus.Lobby);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Rows.JoinArmyAsync(lobby.GameId, lobby.Participants[0].ParticipantId, army.Id, snapshot, Ct));
    }

    [Fact]
    public async Task Reads_an_army_tree_back_to_the_sample()
    {
        var owner = await Seeder.SeedAccountAsync("Owner");
        var seeded = await Seeder.SeedArmyAsync(owner);
        var owned = await Rows.ReadArmyAsync(seeded.Id, Ct);
        Assert.NotNull(owned);
        Assert.Equal(owner, owned.OwnerId);
        Assert.Equal(ContentJson.Serialize(seeded), ContentJson.Serialize(owned.Army));
    }

    [Fact]
    public async Task A_read_army_carries_its_edition()
    {
        var seeded = await Seeder.SeedArmyAsync(await Seeder.SeedAccountAsync("Owner"), ArmyTests.Sample() with { Edition = ArmyEditions.V36 });
        Assert.Equal("3.6", (await Rows.ReadArmyAsync(seeded.Id, Ct))!.Army.Edition);
    }

    [Fact]
    public async Task Weapons_and_properties_read_back_in_import_order_not_id_or_name_order()
    {
        var sample = ArmyTests.Sample();
        var unit = sample.Units[0]; var slot = unit.Models[0];
        Weapon W(string id, string name) => new(Guid.Parse(id), name, new Dictionary<string, string>(), [new("Zeta", null), new("Alpha", 2), new("Mid", null)]);
        // Fresh ids per run: weapon ids are primary keys, and the E2E run shares one database.
        var tail = Guid.CreateVersion7().ToString("N")[20..];
        var weapons = new[] { W($"ffffffff-0000-7000-8000-{tail}", "Second"), W($"80000000-0000-7000-8000-{tail}", "First"), W($"10000000-0000-7000-8000-{tail}", "Third") };
        var army = sample with { Units = [unit with { Models = [slot with { Weapons = weapons }, .. unit.Models.Skip(1)] }, .. sample.Units.Skip(1)] };
        var seeded = await Seeder.SeedArmyAsync(await Seeder.SeedAccountAsync("Owner"), army);
        var read = (await Rows.ReadArmyAsync(seeded.Id, Ct))!.Army.Units[0].Models[0].Weapons;
        Assert.Equal(["Second", "First", "Third"], read.Select(w => w.Name));
        Assert.All(read, w => Assert.Equal(["Zeta", "Alpha", "Mid"], w.Properties.Select(p => p.Name)));
    }

    [Fact]
    public async Task A_slots_army_book_reads_back()
    {
        var sample = ArmyTests.Sample();
        var unit = sample.Units[0];
        var army = sample with { Units = [unit with { Models = [unit.Models[0] with { ArmyBook = "z65fgu0l29i4lnlu" }, .. unit.Models.Skip(1)] }, .. sample.Units.Skip(1)] };
        var seeded = await Seeder.SeedArmyAsync(await Seeder.SeedAccountAsync("Owner"), army);
        var read = (await Rows.ReadArmyAsync(seeded.Id, Ct))!.Army;
        Assert.Equal("z65fgu0l29i4lnlu", read.Units[0].Models[0].ArmyBook);
        Assert.Equal(ContentJson.Serialize(seeded), ContentJson.Serialize(read));
    }

    [Fact]
    public async Task Only_a_playing_game_is_marked_ended()
    {
        var lobby = await Seeder.SeedGameAsync(GameStatus.Lobby);
        await Rows.EndGameAsync(lobby.GameId, Ct);
        Assert.Equal(GameStatus.Lobby, await StatusOf(lobby.GameId));
        var setup = await Seeder.SeedGameAsync(GameStatus.Setup);
        await Rows.EndGameAsync(setup.GameId, Ct);
        Assert.Equal(GameStatus.Setup, await StatusOf(setup.GameId));
        var playing = await Seeder.SeedGameAsync(GameStatus.Playing);
        await Rows.EndGameAsync(playing.GameId, Ct);
        await Rows.EndGameAsync(playing.GameId, Ct);                       // a second writer of the same value changes nothing
        Assert.Equal(GameStatus.Ended, await StatusOf(playing.GameId));
        var ended = await Seeder.SeedGameAsync(GameStatus.Ended);           // management's /end wrote the row first
        await Rows.EndGameAsync(ended.GameId, Ct);
        Assert.Equal(GameStatus.Ended, await StatusOf(ended.GameId));
        await Rows.EndGameAsync(Guid.CreateVersion7(), Ct);                 // no row: nothing to guard, nothing thrown
    }

    [Fact]
    public async Task Repinning_moves_every_game_but_an_ended_one_to_the_set_named()
    {
        var current = await Seeder.SeedPropertiesAsync();
        foreach (var status in new[] { GameStatus.Lobby, GameStatus.Setup, GameStatus.Playing })
        {
            var game = await Seeder.SeedGameAsync(status);
            await Rows.RepinPropertiesAsync(game.GameId, current, Ct);
            Assert.Equal((current, status), ((await Rows.ReadGameAsync(game.GameId, Ct))!.PropertiesHash, await StatusOf(game.GameId)));
        }
        var ended = await Seeder.SeedGameAsync(GameStatus.Ended);
        await Rows.RepinPropertiesAsync(ended.GameId, current, Ct);
        Assert.Equal(ended.PropertiesHash, (await Rows.ReadGameAsync(ended.GameId, Ct))!.PropertiesHash);
        await Rows.RepinPropertiesAsync(Guid.CreateVersion7(), current, Ct);         // no row: nothing written, nothing thrown
    }

    [Fact]
    public async Task Starting_setup_writes_the_draft_and_flips_lobby_to_setup()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        var draft = TableStateTests.Sample();
        await Rows.StartSetupAsync(seeded.GameId, draft, Ct);
        Assert.Equal(GameStatus.Setup, await StatusOf(seeded.GameId));
        AssertSameTree(draft, await Rows.ReadDraftAsync(seeded.GameId, Ct));
    }

    [Theory]
    [InlineData(GameStatus.Setup)]
    [InlineData(GameStatus.Playing)]
    public async Task Starting_setup_outside_the_lobby_throws_and_writes_nothing(GameStatus status)
    {
        var seeded = await Seeder.SeedGameAsync(status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rows.StartSetupAsync(seeded.GameId, TableStateTests.Sample(), Ct));
        Assert.Equal(status, await StatusOf(seeded.GameId));
        Assert.Null(await Rows.ReadDraftAsync(seeded.GameId, Ct));
    }

    [Fact]
    public async Task Writing_the_draft_replaces_the_tree_whole()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        var draft = TableStateTests.Sample();
        await Rows.StartSetupAsync(seeded.GameId, draft, Ct);
        var next = TableStateTests.Sample() with { Id = draft.Id, Name = "Renamed", Size = new TableSize(44, 30), Markers = [] };
        await Rows.WriteDraftAsync(seeded.GameId, next, Ct);
        AssertSameTree(next, await Rows.ReadDraftAsync(seeded.GameId, Ct));
        Assert.Equal(GameStatus.Setup, await StatusOf(seeded.GameId));
    }

    [Fact]
    public async Task Writing_a_draft_the_game_has_no_row_for_throws()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rows.WriteDraftAsync(seeded.GameId, TableStateTests.Sample(), Ct));
        Assert.Null(await Rows.ReadDraftAsync(seeded.GameId, Ct));
    }

    [Fact]
    public async Task A_draft_cannot_take_terrain_ids_another_table_already_holds()
    {
        var layout = await Seeder.SeedLayoutAsync(await Seeder.SeedAccountAsync("Owner"));
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        var draft = TableStateTests.Sample();
        await Rows.StartSetupAsync(seeded.GameId, draft, Ct);
        await Assert.ThrowsAnyAsync<Exception>(() => Rows.WriteDraftAsync(seeded.GameId, layout with { Id = draft.Id }, Ct));
        AssertSameTree(draft, await Rows.ReadDraftAsync(seeded.GameId, Ct));
    }

    [Fact]
    public async Task Ending_setup_writes_the_tree_and_snapshots_and_flips_setup_to_playing()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        var draft = TableStateTests.Sample();
        await Rows.StartSetupAsync(seeded.GameId, draft, Ct);
        var army = await Seeder.SeedArmyAsync(seeded.CreatorAccountId);
        var snapshot = ArmySnapshot.Of(army, Guid.CreateVersion7());
        var final = draft with { Terrain = [] };
        await Rows.EndSetupAsync(seeded.GameId, final,
            new Dictionary<Guid, ArmySnapshot?> { [seeded.Participants[0].ParticipantId] = snapshot, [seeded.Participants[1].ParticipantId] = null }, Ct);
        Assert.Equal(GameStatus.Playing, await StatusOf(seeded.GameId));
        AssertSameTree(final, await Rows.ReadDraftAsync(seeded.GameId, Ct));
        var inPlay = Assert.Single(await Rows.ReadSnapshotsAsync(seeded.GameId, Ct));
        Assert.Equal(seeded.Participants[0].ParticipantId, inPlay.ParticipantId);
        // army_snapshot is jsonb, which keeps no key order, so the tree is compared and not the text.
        Assert.Equal((snapshot.Id, snapshot.Name), (inPlay.Snapshot.Id, inPlay.Snapshot.Name));
        Assert.Equal(snapshot.Units.SelectMany(u => u.Models).Select(m => (m.Id, m.Name, m.Weapons.Count)), inPlay.Snapshot.Units.SelectMany(u => u.Models).Select(m => (m.Id, m.Name, m.Weapons.Count)));
        Assert.Equal(snapshot.Units.Select(u => u.Stats.OrderBy(s => s.Key)), inPlay.Snapshot.Units.Select(u => u.Stats.OrderBy(s => s.Key)));
    }

    [Fact]
    public async Task Ending_setup_outside_setup_throws_and_writes_nothing()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Playing);
        var tableId = await Seeder.SeedTableStateAsync(seeded.GameId);
        var before = await Rows.ReadDraftAsync(seeded.GameId, Ct);
        var army = await Seeder.SeedArmyAsync(seeded.CreatorAccountId);
        var final = TableStateTests.Sample() with { Id = tableId };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rows.EndSetupAsync(seeded.GameId, final,
            new Dictionary<Guid, ArmySnapshot?> { [seeded.Participants[0].ParticipantId] = ArmySnapshot.Of(army, Guid.CreateVersion7()) }, Ct));
        Assert.Equal(GameStatus.Playing, await StatusOf(seeded.GameId));
        AssertSameTree(before!, await Rows.ReadDraftAsync(seeded.GameId, Ct));
        Assert.Empty(await Rows.ReadSnapshotsAsync(seeded.GameId, Ct));
    }

    [Fact]
    public async Task A_new_team_takes_the_next_ordinal()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var third = await Rows.CreateTeamAsync(seeded.GameId, Ct);
        Assert.Equal(3, third.Ordinal);
        var empty = await Seeder.SeedGameAsync(GameStatus.Lobby, teams: 0, participantsPerTeam: 0);
        Assert.Equal(1, (await Rows.CreateTeamAsync(empty.GameId, Ct)).Ordinal);
        Assert.Equal([1, 2, 3], (await Rows.ReadGameAsync(seeded.GameId, Ct))!.Teams.Select(t => t.Ordinal));
        Assert.Contains((await Rows.ReadGameAsync(seeded.GameId, Ct))!.Teams, t => t.Id == third.Id);
    }

    [Fact]
    public async Task A_participants_team_and_army_are_set_and_cleared()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await Seeder.SeedArmyAsync(seeded.CreatorAccountId);
        var p = seeded.Participants[0].ParticipantId;
        await Rows.SetParticipantTeamAsync(p, seeded.TeamIds[1], Ct);
        await Rows.SetParticipantArmyAsync(p, army.Id, Ct);
        Participant Read(GameInfo g) => g.Participants.Single(x => x.Id == p);
        Assert.Equal((seeded.TeamIds[1], (Guid?)army.Id), (Read((await Rows.ReadGameAsync(seeded.GameId, Ct))!).TeamId, Read((await Rows.ReadGameAsync(seeded.GameId, Ct))!).ArmyId));
        Assert.Equal(army.Name, Read((await Rows.ReadGameAsync(seeded.GameId, Ct))!).ArmyName);         // the lobby names each seat's army
        await Rows.SetParticipantTeamAsync(p, null, Ct);
        await Rows.SetParticipantArmyAsync(p, null, Ct);
        var cleared = Read((await Rows.ReadGameAsync(seeded.GameId, Ct))!);
        Assert.Null(cleared.TeamId);
        Assert.Null(cleared.ArmyId);
        Assert.Null(cleared.ArmyName);
    }

    [Fact]
    public async Task Configuration_rewrites_the_six_columns_and_game_rules_follows_the_properties_row()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby, rngSeed: 42);
        var hash = await Seeder.SeedPropertiesAsync();
        await Rows.SetConfigurationAsync(seeded.GameId, new SetConfiguration("Saturday", hash, SchedulerShape.PlayerTurn, Enforcement.Strict, true, new Dictionary<string, string> { ["line_of_sight"] = "hybrid_true" }), Ct);
        var game = (await Rows.ReadGameAsync(seeded.GameId, Ct))!;
        Assert.Equal(("Saturday", hash, "grimdark-future", SchedulerShape.PlayerTurn, Enforcement.Strict, true, 42L, GameStatus.Lobby),
            (game.Name, game.PropertiesHash, game.GameRules, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps, game.RngSeed, game.Status));
        Assert.Equal([new("line_of_sight", "hybrid_true")], game.OptionalRules);
    }

    [Fact]
    public async Task A_seeded_game_has_no_optional_rules_and_a_configuration_replaces_the_map_whole()
    {
        var seeded = await Seeder.SeedGameAsync(GameStatus.Lobby);
        Assert.Empty((await Rows.ReadGameAsync(seeded.GameId, Ct))!.OptionalRules);
        var hash = await Seeder.SeedPropertiesAsync();
        await Rows.SetConfigurationAsync(seeded.GameId, new SetConfiguration("a", hash, SchedulerShape.PlayerTurn, Enforcement.Strict, false, new Dictionary<string, string> { ["melee_separation"] = "off" }), Ct);
        await Rows.SetConfigurationAsync(seeded.GameId, new SetConfiguration("a", hash, SchedulerShape.PlayerTurn, Enforcement.Strict, false, new Dictionary<string, string> { ["unit_sight_blocking"] = "perimeter" }), Ct);
        Assert.Equal([new("unit_sight_blocking", "perimeter")], (await Rows.ReadGameAsync(seeded.GameId, Ct))!.OptionalRules);
    }

    /// <summary>The tree as the reads hand it back: terrain, volumes and markers by id, zones by team ordinal.</summary>
    static void AssertSameTree(TableState expected, TableState? actual)
    {
        Assert.NotNull(actual);
        static TableState Ordered(TableState t) => t with
        {
            Terrain = t.Terrain.OrderBy(x => x.Id).Select(x => x with { Volumes = x.Volumes.OrderBy(v => v.Id).ToList() }).ToList(),
            Markers = t.Markers.OrderBy(m => m.Id).ToList(),
            Zones = t.Zones.OrderBy(z => z.TeamOrdinal).ToList(),
        };
        Assert.Equal(ContentJson.Serialize(Ordered(expected)), ContentJson.Serialize(actual));
    }
}

public class InMemoryGameRowsTests : GameRowsContract
{
    protected override IRowSeeder Seeder { get; } = new InMemorySeeder();
}

[Collection("Postgres")]
[Trait("Category", "E2E")]
public class PostgresGameRowsTests(PostgresFixture pg) : GameRowsContract
{
    protected override IRowSeeder Seeder => pg;
}
