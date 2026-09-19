using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Session;

/// <summary>The fixed-rate presence relay, and the SIGTERM drain. Nothing else in the process runs on a timer.</summary>
public sealed class SessionHost(SessionRegistry registry, GameServerOptions options, IHostApplicationLifetime lifetime, ILogger<SessionHost> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.ApplicationStopping.Register(() => DrainAsync().GetAwaiter().GetResult());
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / options.PresenceRelayHz));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (var session in registry.Live)
                    try { await session.TickPresenceAsync(); }
                    catch (InvalidOperationException) { log.LogDebug("Game {GameId}: drained between Live and Tick", session.GameId); }
                    catch (Exception ex) { log.LogError(ex, "presence tick failed for {GameId}", session.GameId); }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DrainAsync()
    {
        log.LogInformation("Draining {Count} live games", registry.Live.Count);
        await registry.DrainAllAsync("server shutting down");
    }
}
