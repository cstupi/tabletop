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
}
