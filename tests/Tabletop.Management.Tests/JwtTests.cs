using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tabletop.Management.Tests;

public class JwtTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public JwtTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task A_valid_jwt_reads_the_account()
    {
        var id = Guid.CreateVersion7();
        var client = await _f.AuthenticatedClient(id, "Clay");
        var account = await client.GetFromJsonAsync<Identity.AccountRow>("/v1/account");
        Assert.Equal(new Identity.AccountRow(id, "Clay"), account);
    }

    [Fact]
    public async Task No_token_wrong_issuer_and_expired_are_401()
    {
        var anon = _f.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anon.GetAsync("/v1/account")).StatusCode);
        foreach (var jwt in new[] { TestKeys.Mint(Guid.CreateVersion7(), issuer: "stytch.com/other"), TestKeys.Mint(Guid.CreateVersion7(), expires: DateTimeOffset.UtcNow.AddMinutes(-10)) })
        {
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await c.GetAsync("/v1/account")).StatusCode);
        }
    }

    [Fact]
    public async Task An_unknown_kid_refetches_the_jwks_once()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        Assert.True((await c.GetAsync("/v1/account")).IsSuccessStatusCode);
        var before = TestKeys.Fetches;
        TestKeys.Rotate();
        var rotated = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        Assert.True((await rotated.GetAsync("/v1/account")).IsSuccessStatusCode);
        Assert.Equal(before + 1, TestKeys.Fetches);
    }

    [Fact]
    public async Task Concurrent_unknown_kid_requests_collapse_into_one_refetch()
    {
        // A fresh StytchJwks, isolated from other tests' floor state, sharing the fake JWKS
        // endpoint and its Fetches counter.
        var options = _f.Services.GetRequiredService<Tabletop.Management.Configuration.ManagementOptions>();
        var jwks = new Identity.StytchJwks(new HttpClient(TestKeys.JwksHandler), options);
        await jwks.Resolve(null); // the lazy first load; does not count toward the floor
        var before = TestKeys.Fetches;

        var tasks = Enumerable.Range(0, 8).Select(_ => jwks.Resolve("kid-does-not-exist"));
        await Task.WhenAll(tasks);

        Assert.Equal(before + 1, TestKeys.Fetches);
    }
}
