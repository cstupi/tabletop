using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review H2: an unknown kid cannot make every connect a JWKS fetch.</summary>
public class JwksSourceTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = GameServerFactory.ProjectId, StytchJwksUrl = "https://jwks.test/keys" };

    sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        public TimeSpan Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var key = TestKeys.Jwks.Keys[0];
            var json = $$"""{"keys":[{"kty":"{{key.Kty}}","kid":"{{key.Kid}}","n":"{{key.N}}","e":"{{key.E}}"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    static (HttpJwksSource Source, CountingHandler Handler, ManualTime Time) Source()
    {
        var handler = new CountingHandler();
        var time = new ManualTime();
        return (new HttpJwksSource(new HttpClient(handler), Options, time, NullLogger<HttpJwksSource>.Instance), handler, time);
    }

    [Fact]
    public async Task Tokens_with_unknown_kids_refetch_at_most_once_per_30_seconds()
    {
        var (source, handler, time) = Source();
        var verifier = new StytchJwtVerifier(source, Options);
        Assert.NotNull(await verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        for (var i = 0; i < 20; i++) Assert.Null(await verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid(), signingKey: TestKeys.OtherKey), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        time.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 20; i++) Assert.Null(await verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid(), signingKey: TestKeys.OtherKey), CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_fetch()
    {
        var (source, handler, _) = Source();
        handler.Delay = TimeSpan.FromMilliseconds(200);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => source.RefreshAsync(CancellationToken.None)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_failed_fetch_keeps_the_keys_already_held()
    {
        var (source, handler, time) = Source();
        var verifier = new StytchJwtVerifier(source, Options);
        await source.GetAsync(CancellationToken.None);
        handler.Fail = true;
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.NotNull(await verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Held_keys_older_than_an_hour_fail_closed_while_fetches_fail()
    {
        var (source, handler, time) = Source();
        var verifier = new StytchJwtVerifier(source, Options);
        await source.GetAsync(CancellationToken.None);
        handler.Fail = true;
        time.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(await verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid()), CancellationToken.None));
        time.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAnyAsync<Exception>(() => verifier.VerifyAsync(TestKeys.Mint(Guid.NewGuid()), CancellationToken.None));
        time.Advance(TimeSpan.FromSeconds(5));   // inside the 30 s throttle: still closed, not the old keys
        await Assert.ThrowsAnyAsync<Exception>(() => source.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_source_that_never_fetched_throws_when_the_fetch_fails()
    {
        var (source, handler, _) = Source();
        handler.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None));
    }

    [Fact]
    public void A_fetch_has_five_seconds() => Assert.Equal(TimeSpan.FromSeconds(5), HttpJwksSource.FetchTimeout);
}
