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
        // The catalog comes first: both clients rebuild their option controls when the GameInfo lands.
        var frames = await DrainAsync(other);
        var options = frames.FindIndex(m => m is RulesOptionsMessage);
        var game = frames.FindIndex(m => m is GameInfoMessage);
        Assert.True(options >= 0 && game > options, string.Join(", ", frames.Select(m => m.GetType().Name)));
        var info = ((GameInfoMessage)frames[game]).Game;
        Assert.Equal((RuleSets.Gf360, v360), (info.GameRules, info.PropertiesHash));
        Assert.Equal(Json(GrimdarkFutureRules.V360.OptionCatalog), Json(((RulesOptionsMessage)frames[options]).Options));
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
    public async Task A_set_whose_game_rules_this_build_does_not_ship_is_refused()
    {
        var unknown = await db.SeedPropertiesAsync("warhammer-40k", "Unknown rules");
        var (f, g, creator, other) = await LobbyAsync();
        await using var _ = creator; await using var __ = other; using var ___ = f;

        await creator.SendAsync(Edit(Configure(unknown)));
        Assert.Equal(RefusalReason.Unknown, (await creator.ExpectAsync<Refused>()).Reason);
        Assert.Equal((RuleSets.Gf351, g.PropertiesHash), (db.GameRows.Game(g.GameId)!.GameRules, db.GameRows.Game(g.GameId)!.PropertiesHash));
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

    [Fact]
    public async Task A_3_5_army_is_refused_in_a_3_6_game_and_the_reverse()
    {
        Army? v35 = null, v36 = null;
        var (f, g, creator, other) = await LobbyAsync(seeded =>
        {
            v35 = db.SeedArmyAsync(seeded.CreatorAccountId).GetAwaiter().GetResult();
            v36 = db.SeedArmyAsync(seeded.CreatorAccountId, Tabletop.Content.Tests.ArmyTests.Sample() with { Edition = ArmyEditions.V36 }).GetAwaiter().GetResult();
        });
        await using var _ = creator; await using var __ = other; using var ___ = f;

        // The seeded game plays 3.5.1: the 3.6 army is refused, nothing written.
        await creator.SendAsync(Edit(new PickArmy(v36!.Id)));
        var refused = await creator.ExpectAsync<Refused>();
        Assert.Equal((RefusalReason.NotPermitted, "this army is a 3.6 list; the game plays Grimdark Future 3.5"), (refused.Reason, refused.Detail));
        Assert.Null(db.GameRows.Participant(g.Participants[0].ParticipantId)!.ArmyId);
        await creator.SendAsync(Edit(new PickArmy(v35!.Id)));
        Assert.Equal(v35.Id, (await other.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.Id == g.Participants[0].ParticipantId).ArmyId);

        var v360 = await db.SeedPropertiesAsync(RuleSets.Gf360, "Grimdark Future v3.6.0");
        await creator.SendAsync(Edit(Configure(v360)));
        await other.ExpectAsync<GameInfoMessage>();
        await creator.SendAsync(Edit(new PickArmy(v35.Id)));
        refused = await creator.ExpectAsync<Refused>();
        Assert.Equal((RefusalReason.NotPermitted, "this army is a 3.5 list; the game plays Grimdark Future 3.6"), (refused.Reason, refused.Detail));
        await creator.SendAsync(Edit(new PickArmy(v36.Id)));
        Assert.Equal(v36.Id, (await other.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.Id == g.Participants[0].ParticipantId).ArmyId);
    }

    [Fact]
    public async Task EndSetup_refuses_a_mismatched_army()
    {
        // A row that already holds another edition's army (picked before the check, or written past it): EndSetup checks it as it snapshots it.
        var (f, g, creator, other) = await LobbyAsync(seeded =>
        {
            var v36 = db.SeedArmyAsync(seeded.Participants[1].AccountId, Tabletop.Content.Tests.ArmyTests.Sample() with { Edition = ArmyEditions.V36 }).GetAwaiter().GetResult();
            db.GameRows.SetParticipantArmyAsync(seeded.Participants[1].ParticipantId, v36.Id, CancellationToken.None).GetAwaiter().GetResult();
        });
        await using var _ = creator; await using var __ = other; using var ___ = f;

        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>();
        await creator.SendAsync(Edit(new EndSetup()));
        var refused = await creator.ExpectAsync<Refused>();
        var name = (await db.GameRows.ReadGameAsync(g.GameId, CancellationToken.None))!.Participants.Single(p => p.Id == g.Participants[1].ParticipantId).Name;
        Assert.Equal(RefusalReason.NotPermitted, refused.Reason);
        Assert.Equal($"{name}'s army cannot be played: this army is a 3.6 list; the game plays Grimdark Future 3.5", refused.Detail);
        Assert.Equal(GameStatus.Setup, db.GameRows.Game(g.GameId)!.Status);
    }
}

