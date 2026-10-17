using System.Net;
using System.Text.Json;

namespace Tabletop.Management.Tests;

public class RuleTextTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public RuleTextTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-rule-texts", Bare = "sha256:test-rule-texts-bare";

    Task Seed()
    {
        lock (_f.Rows.Gate)
        {
            if (_f.Rows.Properties.ContainsKey(Hash)) return Task.CompletedTask;
            _f.Rows.SeedProperties(Hash, "Rule texts", "grimdark-future");
            _f.Rows.SeedProperties(Bare, "Rule texts bare", "grimdark-future");
            _f.Rows.SeedCatalogEntry(Hash, "Tough", "model", true, "Must take X wounds before being killed.");
            _f.Rows.SeedCatalogEntry(Hash, "AP", "weapon", true, "Targets get -X to Defense rolls when blocking hits.");
            _f.Rows.SeedCatalogEntry(Hash, "Fearless", "unit", false, "Gets +1 to morale tests.");
            _f.Rows.SeedCatalogEntry(Hash, "Precise", "model", false, "Gets +1 to hit when attacking.", played: false);
        }
        return Task.CompletedTask;
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
            e.GetProperty("takesParameter").GetBoolean(), e.GetProperty("ruleText").GetString(), e.GetProperty("played").GetBoolean())).ToList();
        Assert.Equal([
            ("AP", "weapon", true, "Targets get -X to Defense rolls when blocking hits.", true),
            ("Fearless", "unit", false, "Gets +1 to morale tests.", true),
            ("Precise", "model", false, "Gets +1 to hit when attacking.", false),
            ("Tough", "model", true, "Must take X wounds before being killed.", true),
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
