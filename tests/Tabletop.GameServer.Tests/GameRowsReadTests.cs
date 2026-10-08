using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class GameRowsReadTests(PostgresFixture pg)
{
    readonly IGameRows _rows = new PostgresGameRows(pg.Db);

    [Fact]
    public async Task Reads_a_lobby_game_with_teams_and_participants()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 1, spectators: 1);
        var game = await _rows.ReadGameAsync(seeded.GameId, CancellationToken.None);
        Assert.NotNull(game);
        Assert.Equal((GameStatus.Lobby, "Friday", seeded.CreatorAccountId, "grimdark-future", SchedulerShape.AlternatingActivation, Enforcement.Warn),
            (game.Status, game.Name, game.CreatedBy, game.GameRules, game.SchedulerShape, game.Enforcement));
        Assert.Equal(seeded.RngSeed, game.RngSeed);      // the stored dice seed arrives with the rest of the row; this server never writes it
        Assert.Equal([1, 2], game.Teams.Select(t => t.Ordinal));
        Assert.Equal(3, game.Participants.Count);
        Assert.Contains(game.Participants, p => p.TeamId is null && p.Name == "Watcher 0");
        Assert.Contains(game.Participants, p => p.AccountId == seeded.CreatorAccountId && p.Name == "Creator" && p.TeamId == seeded.TeamIds[0]);
        Assert.Null(await _rows.ReadDraftAsync(seeded.GameId, CancellationToken.None));
        Assert.Empty(await _rows.ReadSnapshotsAsync(seeded.GameId, CancellationToken.None));
        Assert.True(await _rows.PropertiesExistAsync(seeded.PropertiesHash, CancellationToken.None));
        Assert.False(await _rows.PropertiesExistAsync("sha256:nope", CancellationToken.None));
    }

    [Fact]
    public async Task Finds_a_participant_by_game_and_account()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        Assert.Equal(seeded.Participants[0].ParticipantId, await _rows.FindParticipantAsync(seeded.GameId, seeded.CreatorAccountId, CancellationToken.None));
        Assert.Null(await _rows.FindParticipantAsync(seeded.GameId, Guid.CreateVersion7(), CancellationToken.None));
        Assert.Null(await _rows.ReadGameAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task Reads_an_army_tree_back_to_the_sample()
    {
        var owner = await pg.SeedAccountAsync("Owner");
        var seeded = await pg.SeedArmyAsync(owner);
        var owned = await _rows.ReadArmyAsync(seeded.Id, CancellationToken.None);
        Assert.NotNull(owned);
        Assert.Equal(owner, owned.OwnerId);
        Assert.Equal(Tabletop.Content.ContentJson.Serialize(seeded), Tabletop.Content.ContentJson.Serialize(owned.Army));
    }

    [Fact]
    public async Task Weapons_and_properties_read_back_in_import_order_not_id_or_name_order()
    {
        var sample = Tabletop.Content.Tests.ArmyTests.Sample();
        var unit = sample.Units[0]; var slot = unit.Models[0];
        Tabletop.Content.Weapon W(string id, string name) => new(Guid.Parse(id), name, new Dictionary<string, string>(), [new("Zeta", null), new("Alpha", 2), new("Mid", null)]);
        var weapons = new[] { W("ffffffff-0000-7000-8000-000000000001", "Second"), W("80000000-0000-7000-8000-000000000002", "First"), W("10000000-0000-7000-8000-000000000003", "Third") };
        var army = sample with { Units = [unit with { Models = [slot with { Weapons = weapons }, .. unit.Models.Skip(1)] }, .. sample.Units.Skip(1)] };
        var seeded = await pg.SeedArmyAsync(await pg.SeedAccountAsync("Owner"), army);
        var read = (await _rows.ReadArmyAsync(seeded.Id, CancellationToken.None))!.Army.Units[0].Models[0].Weapons;
        Assert.Equal(["Second", "First", "Third"], read.Select(w => w.Name));
        Assert.All(read, w => Assert.Equal(["Zeta", "Alpha", "Mid"], w.Properties.Select(p => p.Name)));
    }

    [Fact]
    public async Task A_slots_army_book_reads_back()
    {
        var sample = Tabletop.Content.Tests.ArmyTests.Sample();
        var unit = sample.Units[0];
        var army = sample with { Units = [unit with { Models = [unit.Models[0] with { ArmyBook = "z65fgu0l29i4lnlu" }, .. unit.Models.Skip(1)] }, .. sample.Units.Skip(1)] };
        var seeded = await pg.SeedArmyAsync(await pg.SeedAccountAsync("Owner"), army);
        var read = (await _rows.ReadArmyAsync(seeded.Id, CancellationToken.None))!.Army;
        Assert.Equal("z65fgu0l29i4lnlu", read.Units[0].Models[0].ArmyBook);
        Assert.Equal(Tabletop.Content.ContentJson.Serialize(seeded), Tabletop.Content.ContentJson.Serialize(read));
    }
}
