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
