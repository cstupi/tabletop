# Task 5: Rule text endpoint

**Wave:** 2 · **Depends on:** — · **Model:** sonnet

**Files:**
- Modify: `server/management/Games/GameRepository.cs` (a `CatalogRuleText` row record and `RuleTexts(hash)`)
- Modify: `server/management/Games/PropertiesEndpoint.cs` (map `GET /v1/properties/{hash}/rules`)
- Test: `tests/Tabletop.Management.Tests/RuleTextTests.cs` (new)

**Interfaces:**
- Consumes: `management.properties`, `management.catalog_entry` (`scripts/postgres/init/001_schema.sql`, `docs/schema.md` →
  `catalog_entry`).
- Produces: `GET /v1/properties/{hash}/rules` → `200 [{ "name", "kind", "takesParameter", "ruleText" }]` ordered by name,
  `404` for a hash with no `properties` row, `401` without a bearer. The JSON matches Task 1's client
  `RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText)` under `ContentJson` (camelCase).

Spec: *Rule text*. A pinned set never changes, so the response carries no paging and no cursor. A known set with no
entries is `200 []`, not 404: the client then tags every rule *not automated*.

`catalog_entry.kind` is `unit | model | weapon | spell`; the endpoint returns it as stored. The row record lives in
`GameRepository.cs` because `GameDtos.cs` is outside this task's files.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.Management.Tests/RuleTextTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

public class RuleTextTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public RuleTextTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-rule-texts", Bare = "sha256:test-rule-texts-bare";

    async Task Seed()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("""
            insert into management.properties (content_hash, name, game_rules) values
                (@Hash, 'Rule texts', 'grimdark-future'), (@Bare, 'Rule texts bare', 'grimdark-future')
            on conflict do nothing;
            insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, rule_text) values
                (@Hash, 'Tough', 'model', true, 'all', 'Must take X wounds before being killed.'),
                (@Hash, 'AP', 'weapon', true, null, 'Targets get -X to Defense rolls when blocking hits.'),
                (@Hash, 'Fearless', 'unit', false, null, 'Gets +1 to morale tests.')
            on conflict do nothing;
            """, new { Hash, Bare });
    }

    [Fact]
    public async Task Rules_returns_the_catalog_entries_of_a_pinned_set_by_name()
    {
        await Seed();
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        var response = await c.GetAsync($"/v1/properties/{Uri.EscapeDataString(Hash)}/rules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = json.RootElement.EnumerateArray().Select(e => (
            e.GetProperty("name").GetString(), e.GetProperty("kind").GetString(),
            e.GetProperty("takesParameter").GetBoolean(), e.GetProperty("ruleText").GetString())).ToList();
        Assert.Equal([
            ("AP", "weapon", true, "Targets get -X to Defense rolls when blocking hits."),
            ("Fearless", "unit", false, "Gets +1 to morale tests."),
            ("Tough", "model", true, "Must take X wounds before being killed."),
        ], rows);
    }

    [Fact]
    public async Task Rules_of_a_set_with_no_entries_is_empty()
    {
        await Seed();
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        var response = await c.GetAsync($"/v1/properties/{Uri.EscapeDataString(Bare)}/rules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rules_is_404_for_an_unknown_hash_and_401_without_a_token()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/v1/properties/sha256%3Anope/rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync($"/v1/properties/{Uri.EscapeDataString(Hash)}/rules")).StatusCode);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `docker compose up -d && dotnet test tests/Tabletop.Management.Tests --filter RuleTextTests`
Expected: `Rules_returns_…` and `Rules_of_a_set_…` fail with `404` where `200` was expected (no route);
`Rules_is_404_…` fails on the 401 assertion (the unmatched route answers 404).

- [ ] **Step 3: Implement the query**

In `server/management/Games/GameRepository.cs`, above `public sealed class GameRepository`:

```csharp
/// <summary>One catalog entry's player-facing text (spec: in-game UX → Rule text). Dapper maps by constructor parameter name.</summary>
public sealed record CatalogRuleText(string Name, string Kind, bool TakesParameter, string RuleText);
```

Inside the class, after `ListProperties`:

```csharp
    /// <summary>Every catalog entry of a pinned set, by name; null when no set has this hash.</summary>
    public async Task<IReadOnlyList<CatalogRuleText>?> RuleTexts(string hash)
    {
        await using var c = await db.Open();
        if (await c.ExecuteScalarAsync<int>("select count(*) from properties where content_hash = @hash", new { hash }) == 0) return null;
        return (await c.QueryAsync<CatalogRuleText>("""
            select name as Name, kind as Kind, takes_parameter as TakesParameter, rule_text as RuleText
            from catalog_entry where properties_hash = @hash order by name
            """, new { hash })).ToList();
    }
```

- [ ] **Step 4: Map the route**

Replace the body of `server/management/Games/PropertiesEndpoint.cs` with:

```csharp
namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/properties", async (GameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
        // A pinned set never changes: the client fetches this once per hash and caches it.
        app.MapGet("/v1/properties/{hash}/rules", async (string hash, GameRepository games) =>
            await games.RuleTexts(hash) is { } rows ? Results.Ok(rows) : Results.NotFound()).RequireAuthorization();
    }
}
```

`Program.cs` already calls `app.MapProperties()`.

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Management.Tests --filter "RuleTextTests|GameTests"`
Expected: PASS, the existing `Properties_lists_…` tests included.

- [ ] **Step 6: Commit**

```bash
git add server/management/Games/GameRepository.cs server/management/Games/PropertiesEndpoint.cs tests/Tabletop.Management.Tests/RuleTextTests.cs
git commit -m "Serve a pinned properties set's rule texts at GET /v1/properties/{hash}/rules" -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
