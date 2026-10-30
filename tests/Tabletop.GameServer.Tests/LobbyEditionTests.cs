using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>The lobby's SetConfiguration naming another edition's set (rule-set versions spec → How a game picks its edition).</summary>
public class LobbyEditionTests
{
    readonly InMemorySeeder db = new();
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static SetConfiguration Configure(string hash, Dictionary<string, string>? options = null) =>
        new("Friday", hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false, options ?? new Dictionary<string, string>());
    static string Json(RulesOptionCatalog catalog) => WireJson.Serialize(catalog);

    /// <summary>Every frame the client receives until it has been quiet for 300 ms.</summary>
    static async Task<List<SocketMessage>> DrainAsync(TestClient client)
    {
        var frames = new List<SocketMessage>();
        try { while (true) frames.Add(await client.ReceiveAsync(TimeSpan.FromMilliseconds(300))); }
        catch (OperationCanceledException) { return frames; }
    }

    /// <summary>A lobby on a 3.5.1 set, the creator and the second player connected.</summary>
    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Other)> LobbyAsync(Action<SeededGame>? before = null)
    {
        var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        before?.Invoke(seeded);
        var creator = await TestClient.OpenAsync(factory); var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        return (factory, seeded, creator, other);
    }

    [Fact]
    public async Task Changing_the_set_to_another_edition_swaps_the_rules_and_resends_the_option_catalog()
    {
        var v360 = await db.SeedPropertiesAsync(RuleSets.Gf360, "Grimdark Future v3.6.0");
        var (f, g, creator, other) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; using var ___ = f;
        await using var watcher = await TestClient.OpenAsync(f, watch: true);
        await watcher.ConnectAsync(g.Participants[1].AccountId, g.GameId);

        await creator.SendAsync(Edit(Configure(v360)));
        var info = (await other.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal((RuleSets.Gf360, v360), (info.GameRules, info.PropertiesHash));
        Assert.Equal(Json(GrimdarkFutureRules.V360.OptionCatalog), Json((await other.ExpectAsync<RulesOptionsMessage>()).Options));
        Assert.Equal(Json(GrimdarkFutureRules.V360.OptionCatalog), Json((await creator.ExpectAsync<RulesOptionsMessage>()).Options));
        Assert.Equal(Json(GrimdarkFutureRules.V360.OptionCatalog), Json((await watcher.ExpectAsync<RulesOptionsMessage>()).Options));

        // The session's rules swapped: a later connect is welcomed with the 3.6 catalog.
        await using var late = await TestClient.OpenAsync(f);
        var welcome = await late.ConnectAsync(g.Participants[1].AccountId, g.GameId);
        Assert.Equal(Json(GrimdarkFutureRules.V360.OptionCatalog), Json(welcome.Options));
        Assert.NotEqual(Json(GrimdarkFutureRules.V351.OptionCatalog), Json(welcome.Options));
    }

    [Fact]
    public async Task Changing_the_edition_drops_armies_of_the_other_edition()
    {
        var v360 = await db.SeedPropertiesAsync(RuleSets.Gf360, "Grimdark Future v3.6.0");
        Army? old = null, current = null;
        var (f, g, creator, other) = await LobbyAsync(seeded =>
        {
            old = db.SeedArmyAsync(seeded.CreatorAccountId).GetAwaiter().GetResult();
            current = db.SeedArmyAsync(seeded.Participants[1].AccountId, Tabletop.Content.Tests.ArmyTests.Sample() with { Edition = ArmyEditions.V36 }).GetAwaiter().GetResult();
            db.GameRows.SetParticipantArmyAsync(seeded.Participants[0].ParticipantId, old.Id, CancellationToken.None).GetAwaiter().GetResult();
            db.GameRows.SetParticipantArmyAsync(seeded.Participants[1].ParticipantId, current.Id, CancellationToken.None).GetAwaiter().GetResult();
        });
        await using var _ = creator; await using var __ = other; using var ___ = f;

        await creator.SendAsync(Edit(Configure(v360)));
        var info = (await other.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Null(info.Participants.Single(p => p.Id == g.Participants[0].ParticipantId).ArmyId);
        Assert.Equal(current!.Id, info.Participants.Single(p => p.Id == g.Participants[1].ParticipantId).ArmyId);
        Assert.Null(db.GameRows.Participant(g.Participants[0].ParticipantId)!.ArmyId);
        await other.ExpectAsync<RulesOptionsMessage>();
    }

    [Fact]
    public async Task A_set_change_within_the_same_edition_sends_no_option_catalog()
    {
        var other351 = await db.SeedPropertiesAsync(RuleSets.Gf351, "Grimdark Future v3.5.1 (b)");
        var (f, g, creator, other) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; using var ___ = f;

        await creator.SendAsync(Edit(Configure(other351)));
        var info = (await other.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal((RuleSets.Gf351, other351), (info.GameRules, info.PropertiesHash));
        Assert.DoesNotContain(await DrainAsync(other), m => m is RulesOptionsMessage);
    }

    [Fact]
    public async Task Options_are_checked_against_the_new_editions_catalog()
    {
        var v360 = await db.SeedPropertiesAsync(RuleSets.Gf360, "Grimdark Future v3.6.0");
        var (f, g, creator, other) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; using var ___ = f;

        await creator.SendAsync(Edit(Configure(v360, new Dictionary<string, string> { ["no_such_option"] = "on" })));
        var refused = await creator.ExpectAsync<Refused>();
        Assert.Equal((RefusalReason.Unknown, "unknown option no_such_option"), (refused.Reason, refused.Detail));
        var row = db.GameRows.Game(g.GameId)!;
        Assert.Equal((RuleSets.Gf351, g.PropertiesHash), (row.GameRules, row.PropertiesHash));        // nothing written
        Assert.DoesNotContain(await DrainAsync(other), m => m is GameInfoMessage or RulesOptionsMessage);       // no GameInfo, no catalog
        await using var late = await TestClient.OpenAsync(f);
        Assert.Equal(Json(GrimdarkFutureRules.V351.OptionCatalog), Json((await late.ConnectAsync(g.Participants[1].AccountId, g.GameId)).Options));
    }
}
