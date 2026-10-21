using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

public class DevArmyImportTests(ManagementFactory f) : IClassFixture<ManagementFactory>, IDisposable
{
    private readonly ManagementFactory _dev = new() { HostEnvironment = "Development", Settings = { ["DEV_MODE"] = "1" } };
    public void Dispose() => _dev.Dispose();

    const string Path = "/v1/dev/armies/import";
    static JsonElement Fixture() =>
        JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json"))).RootElement;

    [Fact]
    public async Task The_route_is_absent_outside_dev_mode()
    {
        var c = await f.AuthenticatedClient(Guid.CreateVersion7());   // the shared host is "Testing"
        var r = await c.PostAsJsonAsync(Path, new { source = "lifecycle", list = Fixture() }, ContentJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task A_list_body_imports_as_the_fetched_list_does()
    {
        var c = await _dev.AuthenticatedClient(Guid.CreateVersion7());
        var posted = await c.PostJson<Army>(Path, new { source = "lifecycle", list = Fixture() });
        var fetched = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        Assert.Equal("lifecycle", posted.Source);
        Assert.Equal(Shape(fetched), Shape(posted));
        Assert.Equal(ContentJson.Serialize(posted), ContentJson.Serialize(await c.GetJson<Army>($"/v1/armies/{posted.Id}")));
    }

    [Fact]
    public async Task A_list_body_gets_the_imports_problems()
    {
        var c = await _dev.AuthenticatedClient(Guid.CreateVersion7());
        var other = JsonDocument.Parse("""{"id":"t","name":"T","gameSystem":"ftl","units":[]}""").RootElement;
        var r = await c.PostAsJsonAsync(Path, new { source = "lifecycle", list = other }, ContentJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("Only Grimdark Future and Age of Fantasy lists import", await r.Content.ReadAsStringAsync());
        var noSource = await c.PostAsJsonAsync(Path, new { source = "", list = Fixture() }, ContentJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noSource.StatusCode);
    }

    [Fact]
    public async Task The_route_needs_a_session()
    {
        var r = await _dev.CreateClient().PostAsJsonAsync(Path, new { source = "lifecycle", list = Fixture() }, ContentJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    /// <summary>The army as JSON with its source cleared and every id (minted per import) blanked.</summary>
    static string Shape(Army a) =>
        System.Text.RegularExpressions.Regex.Replace(ContentJson.Serialize(a with { Source = "" }), "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "#");
}
