using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review M5: hydration is serialised per game, not per process.</summary>
public class SessionRegistryTests
{
    /// <summary>IGameRows whose ReadGameAsync for one game waits on Gate.</summary>
    public class GatedRows : DispatchProxy
    {
        public IGameRows Inner = null!;
        public Guid Held;
        public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IGameRows.ReadGameAsync) && (Guid)args![0]! == Held)
                return Gate.Task.ContinueWith(_ => Inner.ReadGameAsync(Held, CancellationToken.None)).Unwrap();
            return method.Invoke(Inner, args);
        }
    }

    [Fact]
    public async Task A_slow_hydration_holds_up_only_its_own_game()
    {
        var db = new InMemorySeeder();
        var slow = await db.SeedGameAsync(GameStatus.Lobby);
        var quick = await db.SeedGameAsync(GameStatus.Lobby);
        var rows = DispatchProxy.Create<IGameRows, GatedRows>();
        var gated = (GatedRows)(object)rows;
        gated.Inner = db.Rows; gated.Held = slow.GameId;
        var options = new GameServerOptions { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };
        var registry = new SessionRegistry(new SessionServices(rows, db.Log, new InertEngineFactory(), options, NullLoggerFactory.Instance));

        var slowHydration = registry.GetOrHydrateAsync(slow.GameId, CancellationToken.None);
        var quickSession = await registry.GetOrHydrateAsync(quick.GameId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(quick.GameId, quickSession.GameId);
        Assert.False(slowHydration.IsCompleted);

        var second = registry.GetOrHydrateAsync(slow.GameId, CancellationToken.None);
        gated.Gate.SetResult();
        Assert.Same(await slowHydration, await second);   // two sockets for one game still hydrate it once
        await registry.DrainAllAsync("done");
    }

    static async Task<(SessionRegistry, GameSession)> LobbyAsync()
    {
        var db = new InMemorySeeder();
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var options = new GameServerOptions { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };
        var registry = new SessionRegistry(new SessionServices(db.Rows, db.Log, new InertEngineFactory(), options, NullLoggerFactory.Instance));
        return (registry, await registry.GetOrHydrateAsync(seeded.GameId, CancellationToken.None));
    }

    /// <summary>The last socket leaving while another's join is queued on the session (got by get-or-hydrate, not yet run): the join
    /// counts, so the game stays in memory and the newcomer stays open.</summary>
    [Fact]
    public async Task A_join_queued_before_the_last_socket_leaves_keeps_the_game()
    {
        var (registry, session) = await LobbyAsync();
        var busy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = session.RunAsync(async () => { await busy.Task; return 0; });
        var watcher = new RecordingClient(Guid.Empty);
        var join = session.WatchAsync(watcher);
        var release = registry.ReleaseIfEmptyAsync(session);                  // the leaving socket's handshake finally
        busy.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));
        await join.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(watcher.Closed);
        Assert.Contains(watcher, session.Watchers);
        Assert.Same(session, Assert.Single(registry.Live));
        await registry.DrainAllAsync("done");
    }

    /// <summary>A join queued after the release decided the session is empty is told the session is gone, which the handshake answers
    /// by hydrating again; it never lands on the session being drained.</summary>
    [Fact]
    public async Task A_join_queued_after_the_release_decided_is_told_to_hydrate_again()
    {
        var (registry, session) = await LobbyAsync();
        var busy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = session.RunAsync(async () => { await busy.Task; return 0; });
        var release = registry.ReleaseIfEmptyAsync(session);                  // its gate is free: it reaches the session's mailbox before returning
        var watcher = new RecordingClient(Guid.Empty);
        var join = session.WatchAsync(watcher);
        busy.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<SessionDrainedException>(() => join.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(watcher.Received);
        Assert.Empty(registry.Live);
    }
}
