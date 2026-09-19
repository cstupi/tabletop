using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PresenceAndShutdownTests(PostgresFixture pg)
{
    [Fact]
    public async Task Presence_reaches_the_other_participant_within_a_few_ticks_and_ends_when_they_leave()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var a = await TestClient.OpenAsync(factory);
        await using var b = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await b.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await a.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        var relayed = await b.ExpectAsync<PresenceMessage>();
        Assert.Equal(seeded.Participants[0].ParticipantId, relayed.Presence.ParticipantId);
        await a.SendAsync(new PingMessage(new Ping(Guid.Empty, new(9, 9, 0), null)));
        Assert.Equal(9, (await b.ExpectAsync<PingMessage>()).Ping.Point.X);
        await a.DisposeAsync();
        Assert.Equal(seeded.Participants[0].ParticipantId, (await b.ExpectAsync<PresenceEnded>()).ParticipantId);
    }

    [Fact]
    public async Task Stopping_the_host_closes_every_socket_with_going_away_and_empties_the_registry()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var a = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        var registry = factory.Services.GetRequiredService<SessionRegistry>();
        Assert.Single(registry.Live);

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopped = new TaskCompletionSource();
        lifetime.ApplicationStopped.Register(() => stopped.TrySetResult());
        lifetime.StopApplication();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await a.WaitClosedAsync());
        Assert.Empty(registry.Live);
        Assert.True(registry.Draining);
    }
}
