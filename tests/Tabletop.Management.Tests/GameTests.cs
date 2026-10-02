using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class GameTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public GameTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-properties";

    async Task Seed()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values (@Hash, 'Grimdark Future core', 'grimdark-future') on conflict do nothing", new { Hash });
    }
    static CreateGameRequest Friday() => new("Friday", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false);

    [Fact]
    public async Task Properties_lists_one_set_per_name_the_most_recently_seeded()
    {
        await using var db = new NpgsqlConnection(_f.Database.ConnectionString);
        await db.ExecuteAsync("""
            insert into management.properties (content_hash, name, game_rules, seeded_at) values
                ('sha256:test-old', 'GF listing', 'grimdark-future', now() - interval '1 day'),
                ('sha256:test-new', 'GF listing', 'grimdark-future', now())
            on conflict do nothing
            """);
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        var listed = (await c.GetJson<List<PropertiesSummary>>("/v1/properties")).Where(p => p.Name == "GF listing").ToList();
        Assert.Equal([new PropertiesSummary("sha256:test-new", "GF listing", "grimdark-future")], listed);
        var game = await c.PostJson<GameInfo>("/v1/games", Friday() with { PropertiesHash = "sha256:test-old" });    // an unlisted set still backs a game
        Assert.Equal("sha256:test-old", game.PropertiesHash);
    }

    [Fact]
    public async Task Properties_lists_the_pinned_sets_and_create_copies_game_rules_from_the_chosen_one()
    {
        await Seed();
        var creator = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(creator, "Clay");
        Assert.Contains(await c.GetJson<List<PropertiesSummary>>("/v1/properties"), p => p == new PropertiesSummary(Hash, "Grimdark Future core", "grimdark-future"));
        var game = await c.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Equal(("Friday", GameStatus.Lobby, creator, "grimdark-future", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false),
            (game.Name, game.Status, game.CreatedBy, game.GameRules, game.PropertiesHash, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps));
        Assert.Empty(game.Teams);
        Assert.Equal([(creator, "Clay", (Guid?)null, (Guid?)null)], game.Participants.Select(p => (p.AccountId, p.Name, p.TeamId, p.ArmyId)));
        Assert.NotEqual(0L, game.RngSeed);                                                                      // generated at creation, not defaulted: a 0 seed would mean shared dice
        Assert.Equal(game.RngSeed, (await c.GetJson<GameInfo>($"/v1/games/{game.Id}")).RngSeed);                 // stored, and read back unchanged
        Assert.NotEqual(game.RngSeed, (await c.PostJson<GameInfo>("/v1/games", Friday())).RngSeed);              // per game, not per properties set
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Friday() with { PropertiesHash = "sha256:nope" }, Tabletop.Content.ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Browser_is_public_join_is_open_at_any_status_and_the_last_leave_ends_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Contains((await b.GetJson<Page<GameSummary>>("/v1/games?limit=200")).Items, g => g.Id == game.Id && g.Participants == 1);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);
        var joined = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(GameStatus.Ended, joined.Status);
        Assert.Equal(2, joined.Participants.Count);
        Assert.Equal(2, (await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { })).Participants.Count);   // idempotent
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // a participant, not the creator
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);  // same rule on both routes
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into game.game_event (game_id, seq, kind, payload) values (@id, 1, 'turn_started', '{}')", new { id = game.Id });
        var again = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // the creator, who left, still may
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @id", new { id = game.Id }));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/games/{game.Id}")).StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_last_leaves_still_end_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        await Task.WhenAll(a.PostAsync($"/v1/games/{game.Id}/leave", null), b.PostAsync($"/v1/games/{game.Id}/leave", null));
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
    }
}
