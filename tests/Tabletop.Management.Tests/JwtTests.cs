using System.Net.Http.Json;

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
}
