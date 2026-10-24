using System.Net;
using System.Net.Http.Json;
using System.Text;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public class RandomTableTests : IClassFixture<ManagementFactory>
{
    readonly ManagementFactory _f;
    public RandomTableTests(ManagementFactory f) => _f = f;

    static readonly TableSize Opr = new(72, 48);
    static List<DeploymentZone> Frontline() =>
        ZoneStyles.ShapesOf("frontline").Select((s, i) => new DeploymentZone(Guid.CreateVersion7(), s, i + 1)).ToList();

    static StringContent Raw(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Random_lays_a_table_for_a_seed()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var t = await c.PostJson<RandomTable>("/v1/layouts/random", new RandomTableRequest(Opr, Planet.Hospitable, Frontline(), 1));
        Assert.Equal(1, t.Seed);
        Assert.InRange(t.Terrain.Count + t.Unplaced.Count, 13, 21);
        Assert.All(t.Terrain, p => { Assert.NotNull(p.Mesh.Builtin); Assert.NotEmpty(p.Volumes); Assert.Equal(0, p.Position.Z); });
    }

    [Fact]
    public async Task The_same_seed_answers_the_same_body()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var request = new RandomTableRequest(Opr, Planet.Ruins, Frontline(), 3);
        var a = await (await c.PostAsJsonAsync("/v1/layouts/random", request, ContentJson.Options)).Content.ReadAsStringAsync();
        var b = await (await c.PostAsJsonAsync("/v1/layouts/random", request, ContentJson.Options)).Content.ReadAsStringAsync();
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task No_seed_picks_one_and_answers_it()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var first = await c.PostJson<RandomTable>("/v1/layouts/random", new RandomTableRequest(Opr, Planet.Death, Frontline(), null));
        var again = await c.PostJson<RandomTable>("/v1/layouts/random", new RandomTableRequest(Opr, Planet.Death, Frontline(), first.Seed));
        Assert.Equal(ContentJson.Serialize(first.Terrain), ContentJson.Serialize(again.Terrain));
    }

    [Fact]
    public async Task It_matches_the_library()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var zones = Frontline();
        var t = await c.PostJson<RandomTable>("/v1/layouts/random", new RandomTableRequest(Opr, Planet.Hospitable, zones, 1));
        var laid = RandomTables.Lay(Planet.Hospitable, Opr, zones, 1, BuiltinLayouts.Embedded().Terrain);
        Assert.Equal(ContentJson.Serialize(laid.Pieces.Select(p => p.Placement).ToList()), ContentJson.Serialize(t.Terrain));
        Assert.Equal(laid.Unplaced, t.Unplaced);
        Assert.Equal(laid.Unlisted, t.Unlisted);
    }

    [Theory]
    [InlineData("{\"size\":{\"width\":0,\"length\":48},\"planet\":\"hospitable\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"size\":{\"width\":145,\"length\":48},\"planet\":\"hospitable\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"size\":{\"width\":72,\"length\":48},\"planet\":\"hospitable\",\"zones\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"shape\":\"frontline/9\",\"teamOrdinal\":1}]}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"size\":{\"width\":72,\"length\":48},\"planet\":\"lava\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"size\":{\"width\":72,\"length\":48},\"planet\":1}", HttpStatusCode.BadRequest)]
    public async Task Bad_requests_are_refused(string body, HttpStatusCode status)
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(status, (await c.PostAsync("/v1/layouts/random", Raw(body))).StatusCode);
    }

    [Fact]
    public async Task Seventeen_zones_are_refused()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var zones = Enumerable.Range(0, 17).Select(_ => new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1)).ToList();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/layouts/random", new RandomTableRequest(Opr, Planet.Hospitable, zones, 1), ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsync("/v1/layouts/random", Raw("{\"size\":{\"width\":72,\"length\":48},\"planet\":\"hospitable\"}"))).StatusCode);
}
