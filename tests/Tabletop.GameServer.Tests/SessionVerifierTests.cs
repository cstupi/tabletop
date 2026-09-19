using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class SessionVerifierTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = GameServerFactory.ProjectId, StytchJwksUrl = "unused" };
    static StytchJwtVerifier Verifier() => new(TestKeys.Source, Options);

    [Fact]
    public async Task A_valid_token_yields_the_account_id()
    {
        var account = Guid.CreateVersion7();
        var session = await Verifier().VerifyAsync(TestKeys.Mint(account), CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(account, session.AccountId);
        Assert.StartsWith("user-test-", session.StytchUserId);
    }

    [Theory]
    [InlineData("issuer")] [InlineData("audience")] [InlineData("expired")] [InlineData("no-claim")] [InlineData("garbage")]
    public async Task Bad_tokens_yield_null(string flaw)
    {
        var account = Guid.CreateVersion7();
        var jwt = flaw switch
        {
            "issuer" => TestKeys.Mint(account, issuer: "stytch.com/project-test-other"),
            "audience" => TestKeys.Mint(account, audience: "project-test-other"),
            "expired" => TestKeys.Mint(account, lifetime: TimeSpan.FromMinutes(-10)),
            "no-claim" => TestKeys.Mint(account, includeAccountClaim: false),
            _ => "not.a.jwt",
        };
        Assert.Null(await Verifier().VerifyAsync(jwt, CancellationToken.None));
    }
}
