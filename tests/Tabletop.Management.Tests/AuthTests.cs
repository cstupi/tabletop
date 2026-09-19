using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Management.Identity;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Tests;

public class AuthTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AuthTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Otp_login_creates_the_account_and_the_first_jwt_carries_the_claim()
    {
        var c = _f.CreateClient();
        var sent = await (await c.PostAsJsonAsync("/v1/auth/otp", new { email = "clay@example.com" })).Content.ReadFromJsonAsync<OtpSentResponse>();
        var before = _f.Stytch.Authenticates;
        var r = await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = sent!.MethodId, code = FakeStytch.Code });
        var session = await r.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - before);        // exactly one re-mint, because external_id was written on this login
        Assert.Equal("clay", session!.Account.Name);
        Assert.Equal(session.Account.Id.ToString(), _f.Stytch.Users["clay@example.com"].ExternalId);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.SessionJwt);
        Assert.Equal(session.Account.Id.ToString(), jwt.Claims.Single(x => x.Type == StytchAuthentication.AccountClaim).Value);
        c.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionJwt);
        Assert.Equal(session.Account, await c.GetFromJsonAsync<AccountRow>("/v1/account"));
    }

    [Fact]
    public async Task A_second_login_reuses_the_account()
    {
        var c = _f.CreateClient();
        var first = await (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "a@example.com", password = "correct horse battery" })).Content.ReadFromJsonAsync<SessionResponse>();
        var again = await (await c.PostAsJsonAsync("/v1/auth/password", new { email = "a@example.com", password = "correct horse battery" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(first!.Account.Id, again!.Account.Id);
    }

    [Fact]
    public async Task A_linked_login_and_a_refresh_cost_one_stytch_call_each()
    {
        var c = _f.CreateClient();
        await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "once@example.com", password = "long enough password" });

        var beforeLogin = _f.Stytch.Authenticates;
        var again = await (await c.PostAsJsonAsync("/v1/auth/password", new { email = "once@example.com", password = "long enough password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(0, _f.Stytch.Authenticates - beforeLogin);    // no re-mint: the user was already linked, so the template stamped the claim
        Assert.Equal(again!.Account.Id.ToString(), Claim(again.SessionJwt));

        var beforeRefresh = _f.Stytch.Authenticates;
        var refreshed = await (await c.PostAsJsonAsync("/v1/auth/session/refresh", new { sessionJwt = again.SessionJwt })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - beforeRefresh);   // the refresh itself and nothing after it
        Assert.Equal(again.Account.Id.ToString(), Claim(refreshed!.SessionJwt));
    }

    private static string Claim(string jwt) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Single(x => x.Type == StytchAuthentication.AccountClaim).Value;

    [Fact]
    public async Task Failures_map_to_401_409_422()
    {
        var c = _f.CreateClient();
        await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "dup@example.com", password = "long enough password" });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "dup@example.com", password = "long enough password" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "w@example.com", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/password", new { email = "dup@example.com", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = "email-nobody", code = "000000" })).StatusCode);
    }

    [Fact]
    public async Task Reset_refresh_and_logout()
    {
        var c = _f.CreateClient();
        var s = await (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "r@example.com", password = "long enough password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/v1/auth/password/reset", new { email = "r@example.com" })).StatusCode);
        Assert.Contains("r@example.com", _f.Stytch.ResetEmails);
        var token = "reset-" + _f.Stytch.Users["r@example.com"].Id;
        var after = await (await c.PostAsJsonAsync("/v1/auth/password/reset/complete", new { token, password = "another long password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(s!.Account.Id, after!.Account.Id);
        var refreshed = await (await c.PostAsJsonAsync("/v1/auth/session/refresh", new { sessionJwt = after.SessionJwt })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(s.Account.Id, refreshed!.Account.Id);
        c.DefaultRequestHeaders.Authorization = new("Bearer", refreshed.SessionJwt);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/v1/auth/session")).StatusCode);
        Assert.Contains(refreshed.SessionJwt, _f.Stytch.Revoked);
    }

    [Fact]
    public async Task A_non_json_stytch_response_maps_to_502()
    {
        var c = _f.CreateClient();
        _f.Stytch.Break = ("/v1/otps/email/login_or_create", 503, "<html>Bad Gateway</html>");
        var r = await c.PostAsJsonAsync("/v1/auth/otp", new { email = "gateway@example.com" });
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
    }

    [Fact]
    public async Task A_failed_link_leaves_no_orphaned_account_and_self_heals_next_login()
    {
        var c = _f.CreateClient();
        var db = _f.Services.GetRequiredService<Database>();
        async Task<long> AccountCount()
        {
            await using var conn = await db.Open();
            return await conn.ExecuteScalarAsync<long>("select count(*) from account");
        }

        var before = await AccountCount();
        var sent = await (await c.PostAsJsonAsync("/v1/auth/otp", new { email = "orphan@example.com" })).Content.ReadFromJsonAsync<OtpSentResponse>();
        var userId = _f.Stytch.Users["orphan@example.com"].Id;
        _f.Stytch.Break = ($"/v1/users/{userId}", 500, "<html>boom</html>");

        var failed = await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = sent!.MethodId, code = FakeStytch.Code });
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(before, await AccountCount());   // the failed link left no row behind

        var ok = await (await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = sent.MethodId, code = FakeStytch.Code })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(ok);
        Assert.Equal(before + 1, await AccountCount());   // the next login self-heals: exactly one row created
    }
}
