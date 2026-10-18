using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ManagementClientTests
{
    static (ManagementClient Client, FakeManagement Fake) Make() { var f = new FakeManagement(); return (new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100")), f); }

    [Fact]
    public async Task Every_request_carries_the_version_header_and_the_bearer_when_logged_in()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.OK, new AccountRow(Guid.CreateVersion7(), "Clay"));
        c.Jwt = "jwt-1";
        await c.GetAccount();
        var r = f.Requests.Single();
        Assert.Equal(ProtocolVersion.Current.ToString(), r.Headers.GetValues(ProtocolVersion.Header).Single());
        Assert.Equal("Bearer jwt-1", r.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5100/v1/account", r.RequestUri!.ToString());
    }

    [Fact]
    public async Task Login_paths_and_shapes()
    {
        var (c, f) = Make();
        var account = new AccountRow(Guid.CreateVersion7(), "clay");
        f.Answer(HttpStatusCode.OK, new OtpSentResponse("email-1")).Answer(HttpStatusCode.OK, new SessionResponse("jwt-2", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-3", account)).Answer(HttpStatusCode.Created, new SessionResponse("jwt-4", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-5", account)).Answer(HttpStatusCode.NoContent);
        Assert.Equal("email-1", (await c.SendOtp("clay@example.com")).MethodId);
        Assert.Equal("jwt-2", (await c.VerifyOtp("email-1", "123456")).SessionJwt);
        Assert.Equal("jwt-3", (await c.LoginWithPassword("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-4", (await c.SignUp("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-5", (await c.Refresh("jwt-old")).SessionJwt);
        c.Jwt = "jwt-5"; await c.Logout();
        Assert.Equal(["/v1/auth/otp", "/v1/auth/otp/verify", "/v1/auth/password", "/v1/auth/password/signup", "/v1/auth/session/refresh", "/v1/auth/session"], f.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal(HttpMethod.Delete, f.Requests[5].Method);
        Assert.Contains("\"methodId\":\"email-1\"", f.Bodies[1]);
        Assert.Null(f.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task A_426_is_the_update_required_exception_and_problems_carry_their_title()
    {
        var (c, f) = Make();
        f.Answer((HttpStatusCode)426, "{\"title\":\"Update required\",\"detail\":\"This server accepts Tabletop-Client-Version >= 2.\",\"status\":426}", "application/problem+json");
        var up = await Assert.ThrowsAsync<UpdateRequiredException>(() => c.ListProperties());
        Assert.Equal(426, up.Status);
        f.Answer(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\",\"detail\":\"unauthorized_credentials\",\"status\":401}", "application/problem+json");
        var ex = await Assert.ThrowsAsync<ManagementException>(() => c.LoginWithPassword("a", "b"));
        Assert.Equal((401, "Unauthorized", "unauthorized_credentials"), (ex.Status, ex.Title, ex.Detail));
    }

    [Fact]
    public async Task Games_layouts_and_the_manifest_round_trip_contract_types()
    {
        var (c, f) = Make();
        var game = Samples.Game;
        var layout = Tabletop.Content.Tests.TableStateTests.Sample();
        f.Answer(HttpStatusCode.OK, new Page<GameSummary>([new GameSummary(game.Id, "Friday", GameStatus.Lobby, game.CreatedBy, DateTimeOffset.UnixEpoch, 1)], game.Id))
         .Answer(HttpStatusCode.Created, game).Answer(HttpStatusCode.OK, game).Answer(HttpStatusCode.NoContent)
         .Answer(HttpStatusCode.Created, layout).Answer(HttpStatusCode.OK, new Manifest([new ManifestEntry(Guid.CreateVersion7(), AssetKind.Miniature, new string('a', 64), new Uri("http://localhost:9000/b/k"), 2.5, new Tabletop.Geometry.Orientation { Pitch = 90, Yaw = 0, Roll = 0 })], DateTimeOffset.UnixEpoch));
        var page = await c.ListGames(null, 25);
        Assert.Equal("http://localhost:5100/v1/games?limit=25", f.Requests[0].RequestUri!.ToString());
        Assert.Equal(game.Id, page.Next);
        Assert.Equal(ContentJson.Serialize(game), ContentJson.Serialize(await c.CreateGame(new CreateGameRequest("Friday", "sha256:abc", SchedulerShape.AlternatingActivation, Enforcement.Warn, false))));
        Assert.Contains("\"schedulerShape\":\"alternating-activation\"", f.Bodies[1]);
        await c.JoinGame(game.Id); await c.LeaveGame(game.Id);
        Assert.Equal($"/v1/games/{game.Id}/leave", f.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal(ContentJson.Serialize(layout), ContentJson.Serialize(await c.SaveLayout(layout)));
        var entry = (await c.GetManifest(game.Id)).Assets.Single();
        Assert.Equal((AssetKind.Miniature, 2.5, new Tabletop.Geometry.Orientation { Pitch = 90, Yaw = 0, Roll = 0 }), (entry.Kind, entry.Scale, entry.Orientation));
        Assert.Equal($"/v1/games/{game.Id}/assets", f.Requests[5].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Rule_texts_are_read_from_the_pinned_set()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.OK, "[{\"name\":\"Tough\",\"kind\":\"unit\",\"takesParameter\":true,\"ruleText\":\"Must take X wounds to be killed.\"}]");
        var rows = await c.GetRuleTexts("sha256:abc");
        Assert.Equal("http://localhost:5100/v1/properties/sha256:abc/rules", f.Requests.Single().RequestUri!.ToString());
        Assert.Equal(new RuleTextDto("Tough", "unit", true, "Must take X wounds to be killed."), rows.Single());
    }

    [Fact]
    public async Task An_unknown_set_is_a_404()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.NotFound, "{\"title\":\"Not found\",\"status\":404}", "application/problem+json");
        var ex = await Assert.ThrowsAsync<ManagementException>(() => c.GetRuleTexts("sha256:nope"));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task A_responses_builtin_version_is_raised_and_GetBuiltin_is_null_when_nothing_is_published()
    {
        var (c, f) = Make();
        var seen = new List<string>();
        c.BuiltinVersionSeen += seen.Add;
        f.Answer(HttpStatusCode.OK, new AccountRow(Guid.CreateVersion7(), "Clay"));
        await c.GetAccount();
        Assert.Empty(seen);
        var withHeader = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"title\":\"Not found\"}") };
        withHeader.Headers.Add(ProtocolVersion.BuiltinHeader, "1.3.0");
        f.Respond(withHeader);
        Assert.Null(await c.GetBuiltin());
        Assert.Equal(["1.3.0"], seen);
        Assert.Equal("http://localhost:5100/v1/builtin", f.Requests[^1].RequestUri!.ToString());
    }
}
