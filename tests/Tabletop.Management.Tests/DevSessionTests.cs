using System.Net;
using System.Net.Http.Json;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

public class DevSessionTests(ManagementFactory f) : IClassFixture<ManagementFactory>, IDisposable
{
    private readonly ManagementFactory _dev = new() { HostEnvironment = "Development" };
    public void Dispose() => _dev.Dispose();

    [Fact]
    public async Task The_mint_is_absent_outside_a_development_environment()
    {
        var r = await f.CreateClient().PostAsJsonAsync("/v1/dev/sessions", new { name = "team-1" });   // the shared host is "Testing"
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Empty(f.Stytch.Calls);
    }

    [Fact]
    public async Task The_mint_returns_a_usable_session_and_repeats_for_one_name()
    {
        var c = _dev.CreateClient();
        var first = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "team-1" });
        var second = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "team-1" });
        Assert.Equal(DevSessions.AccountIdFor("team-1"), first.Account.Id);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.Single(_dev.Stytch.Users);                                                              // found by external_id, not created twice
        Assert.Equal(1, _dev.Stytch.Calls.Count(p => p == "/v1/users"));
        Assert.Equal(2, _dev.Stytch.Authenticates);                                                    // one stamp per mint: each is a new session
        c.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionJwt);
        Assert.Equal(first.Account.Id, (await c.GetFromJsonAsync<AccountRow>("/v1/account"))!.Id);
    }

    [Fact]
    public async Task The_mint_stamps_the_roles_it_is_given_in_place_of_the_users()
    {
        var c = _dev.CreateClient();
        var admin = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "watcher-1", roles = new[] { "admin" } });
        var plain = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "watcher-1" });
        Assert.Equal(["admin"], Roles(admin.SessionJwt));
        Assert.Equal(["stytch_user"], Roles(plain.SessionJwt));                                       // no roles given: the Stytch user's
        Assert.Equal(["stytch_user"], _dev.Stytch.Users.Single().Value.Roles);                         // nothing written to Stytch
    }

    private static List<string> Roles(string jwt) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Where(x => x.Type == StytchAuthentication.RolesClaim).Select(x => x.Value).ToList();
}
