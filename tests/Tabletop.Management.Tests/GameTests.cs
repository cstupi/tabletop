using System.Net;
using System.Net.Http.Json;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class GameTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public GameTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-properties";

    Task Seed()
    {
        lock (_f.Rows.Gate) if (!_f.Rows.Properties.ContainsKey(Hash)) _f.Rows.SeedProperties(Hash, "Grimdark Future core", "grimdark-future");
        return Task.CompletedTask;
    }
    static CreateGameRequest Friday() => new("Friday", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false);

    [Fact]
    public async Task Properties_flags_only_the_default_rule_set()
    {
        _f.Rows.SeedProperties("sha256:test-351", "Default flag 3.5.1", "grimdark-future");
        _f.Rows.SeedProperties("sha256:test-360", "Default flag 3.6.0", "grimdark-future-3.6");
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        var listed = (await c.GetJson<List<PropertiesSummary>>("/v1/properties")).Where(p => p.Name.StartsWith("Default flag")).ToList();
        Assert.Equal([("Default flag 3.5.1", false), ("Default flag 3.6.0", true)], listed.Select(p => (p.Name, p.Default)).OrderBy(p => p.Name));
    }

    [Fact]
    public async Task Properties_lists_one_set_per_name_the_most_recently_seeded()
    {
        _f.Rows.SeedProperties("sha256:test-old", "GF listing", "grimdark-future", DateTimeOffset.UtcNow.AddDays(-1));
        _f.Rows.SeedProperties("sha256:test-new", "GF listing", "grimdark-future", DateTimeOffset.UtcNow);
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        var listed = (await c.GetJson<List<PropertiesSummary>>("/v1/properties")).Where(p => p.Name == "GF listing").ToList();
        Assert.Equal([new PropertiesSummary("sha256:test-new", "GF listing", "grimdark-future", false)], listed);
        var game = await c.PostJson<GameInfo>("/v1/games", Friday() with { PropertiesHash = "sha256:test-old" });    // an unlisted set still backs a game
        Assert.Equal("sha256:test-old", game.PropertiesHash);
    }

    [Fact]
    public async Task Properties_lists_the_pinned_sets_and_create_copies_game_rules_from_the_chosen_one()
    {
        await Seed();
        var creator = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(creator, "Clay");
        Assert.Contains(await c.GetJson<List<PropertiesSummary>>("/v1/properties"), p => p == new PropertiesSummary(Hash, "Grimdark Future core", "grimdark-future", false));
        var game = await c.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Equal(("Friday", GameStatus.Lobby, creator, "grimdark-future", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false),
            (game.Name, game.Status, game.CreatedBy, game.GameRules, game.PropertiesHash, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps));
        Assert.Empty(game.Teams);
        Assert.Equal([(creator, "Clay", (Guid?)null, (Guid?)null)], game.Participants.Select(p => (p.AccountId, p.Name, p.TeamId, p.ArmyId)));
        var stored = _f.Rows.Games[game.Id].RngSeed;
        Assert.NotEqual(0L, stored);                                                                            // generated at creation, not defaulted: a 0 seed would mean shared dice
        Assert.DoesNotContain("rngSeed", await c.GetStringAsync($"/v1/games/{game.Id}"), StringComparison.OrdinalIgnoreCase);   // dice are a function of the seed: never sent
        Assert.DoesNotContain("rngSeed", await (await c.PostAsync($"/v1/games/{game.Id}/join", null)).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var second = await c.PostAsJsonAsync("/v1/games", Friday(), Tabletop.Content.ContentJson.Options);
        Assert.DoesNotContain("rngSeed", await second.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Friday() with { PropertiesHash = "sha256:nope" }, Tabletop.Content.ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Browser_is_public_join_is_open_at_any_status_and_the_last_leave_ends_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Contains((await b.GetJson<Page<GameSummary>>("/v1/games?limit=200")).Items, g => g.Id == game.Id && g.Participants == 1 && g.ParticipantNames.SequenceEqual(["A"]) && !g.Joined);
        Assert.DoesNotContain((await b.GetJson<Page<GameSummary>>("/v1/games?joined=true")).Items, g => g.Id == game.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);
        var joined = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Contains((await b.GetJson<Page<GameSummary>>("/v1/games?joined=true")).Items, g => g.Id == game.Id && g.Joined && g.Status == GameStatus.Ended);
        Assert.Equal(GameStatus.Ended, joined.Status);
        Assert.Equal(2, joined.Participants.Count);
        Assert.Equal(2, (await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { })).Participants.Count);   // idempotent
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // a participant, not the creator
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);  // same rule on both routes
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
        _f.Rows.SeedGameEvent(game.Id, 1);
        var again = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // the creator, who left, still may
        lock (_f.Rows.Gate) Assert.DoesNotContain(_f.Rows.GameEvents, e => e.GameId == game.Id);
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
