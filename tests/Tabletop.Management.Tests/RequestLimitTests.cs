using System.Net;
using System.Net.Http.Json;
using System.Text;
using Tabletop.Content;
using Tabletop.Management.Games;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class RequestLimitTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public RequestLimitTests(ManagementFactory f) => _f = f;

    static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_json_body_over_a_megabyte_is_refused_with_413()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var big = "{\"name\":\"" + new string('x', 1_100_000) + "\"}";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync("/v1/layouts", Json(big))).StatusCode);
        var chunked = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big)));   // no Content-Length: counted as it is read
        chunked.Headers.ContentType = new("application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync("/v1/layouts", chunked)).StatusCode);
    }

    [Fact]
    public async Task Enums_bind_by_their_names_only()
    {
        lock (_f.Rows.Gate) if (!_f.Rows.Properties.ContainsKey("sha256:limits")) _f.Rows.SeedProperties("sha256:limits", "GF", "grimdark-future");
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        string Game(string shape, string enforcement) => $"{{\"name\":\"G\",\"propertiesHash\":\"sha256:limits\",\"schedulerShape\":{shape},\"enforcement\":{enforcement},\"waitsBetweenSteps\":false}}";
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync("/v1/games", Json(Game("\"player-turn\"", "\"warn\"")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/v1/games", Json(Game("99", "\"warn\"")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/v1/games", Json(Game("\"player-turn\"", "1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/v1/games", Json(Game("\"player-turn\"", "\"bogus\"")))).StatusCode);
    }

    [Fact]
    public async Task Game_names_are_bounded_text()
    {
        lock (_f.Rows.Gate) if (!_f.Rows.Properties.ContainsKey("sha256:limits")) _f.Rows.SeedProperties("sha256:limits", "GF", "grimdark-future");
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        CreateGameRequest Named(string name) => new(name, "sha256:limits", SchedulerShape.PlayerTurn, Enforcement.Warn, false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Named(new string('g', 65)), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Named("bell\u0007"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/games", Named(new string('g', 64)), ContentJson.Options)).StatusCode);
    }
}
