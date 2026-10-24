using System.Diagnostics;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Lifecycle;

/// <summary>Management's base address and the game server's socket; WatchUri is the replay socket (`/ws?watch=1`).</summary>
public sealed record LifecycleEndpoints(Uri Management, Uri GameServer)
{
    public Uri WatchUri => new UriBuilder(GameServer) { Query = "watch=1" }.Uri;

    /// <summary>TABLETOP_SIM_MANAGEMENT and TABLETOP_SIM_WS, from the process or the working directory's .env, with the socket driver's defaults.</summary>
    public static LifecycleEndpoints FromEnvironment()
    {
        Tabletop.GameServer.Configuration.DotEnv.Load(Directory.GetCurrentDirectory());
        return new(new Uri(Environment.GetEnvironmentVariable(SocketCredentials.ManagementUrlVariable) ?? SimOptions.DefaultManagement),
            new Uri(Environment.GetEnvironmentVariable("TABLETOP_SIM_WS") ?? SimOptions.DefaultWebSocket));
    }
}

/// <summary>One shape's verdict: every failure line (the step and what did not hold), and the game it created, if it got that far.</summary>
public sealed record LifecycleResult(string Shape, IReadOnlyList<string> Failures, double Seconds, Guid? GameId)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// Plays the selected shapes one after another against the stack (lifecycle suite spec). Each shape signs its accounts in afresh, so no
/// session outlives a shape, and deletes what it made in its own finally. The run first sweeps what an interrupted run left.
/// </summary>
public sealed class LifecycleRunner(LifecycleEndpoints endpoints)
{
    /// <summary>The fixed accounts: lifecycle-player-0..7 take seats, lifecycle-spectator records. Never deleted.</summary>
    public static IReadOnlyList<string> Players { get; } = Enumerable.Range(0, 8).Select(i => $"lifecycle-player-{i}").ToList();
    public const string Spectator = "lifecycle-spectator";
    public const string GamePrefix = "lifecycle ";

    /// <summary>How old a lifecycle game or army must be before a run's sweep deletes it: older than any run.</summary>
    public static readonly TimeSpan SweepAge = TimeSpan.FromHours(1);

    public static string GameName(Guid runId, string shape) => $"{GamePrefix}{runId:N} {shape}";

    public async Task<IReadOnlyList<LifecycleResult>> RunAsync(IReadOnlyList<LifecycleShape> shapes, IProgress<LifecycleResult>? progress, CancellationToken ct)
    {
        var runId = Guid.CreateVersion7();
        await SweepAsync(ct);
        var results = new List<LifecycleResult>();
        foreach (var shape in shapes)
        {
            ct.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            var game = new LifecycleGame(shape, GameName(runId, shape.Name), endpoints);
            var failures = await game.PlayAsync(ct);
            var result = new LifecycleResult(shape.Name, failures, clock.Elapsed.TotalSeconds, game.GameId);
            results.Add(result);
            progress?.Report(result);
        }
        return results;
    }

    /// <summary>Lifecycle games each account created and armies it holds, older than SweepAge: what a run that died before its finally left.</summary>
    async Task SweepAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var name in Players)
        {
            using var api = await ManagementApi.SignInAsync(endpoints.Management, name, ct);
            foreach (var game in await api.JoinedAsync(ct))
                if (game.CreatedBy == api.AccountId && game.Name.StartsWith(GamePrefix, StringComparison.Ordinal) && Stale(game.Id, now))
                    await api.DeleteGameAsync(game.Id, ct);
            foreach (var army in await api.ArmiesAsync(ct))
                if (army.Source == LifecycleArmy.Source && Stale(army.Id, now))
                    await api.DeleteArmyAsync(army.Id, ct);
        }
    }

    /// <summary>A v7 id older than SweepAge (its first 48 bits are its Unix time in milliseconds); any other id is never stale.</summary>
    public static bool Stale(Guid id, DateTimeOffset now) => CreatedAt(id) is { } at && now - at > SweepAge;

    public static DateTimeOffset? CreatedAt(Guid id)
    {
        if (id.Version != 7) return null;
        var bytes = id.ToByteArray(bigEndian: true);
        var ms = 0L;
        for (var i = 0; i < 6; i++) ms = (ms << 8) | bytes[i];
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }
}
