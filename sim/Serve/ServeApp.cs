using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Serve;

/// <summary>`POST /runs`: only = game names and groups (none: every game), driver = in-process | socket, keep.</summary>
public sealed record RunRequest(IReadOnlyList<string>? Only, string? Driver, bool Keep);

/// <summary>
/// The runner service the viewer's Tests page calls: the suite's games, one run at a time, and a run's results as server-sent
/// events. The viewer proxies /suite and /runs to it like /v1, so the browser stays same-origin and there is no CORS.
/// </summary>
public static class ServeApp
{
    public static WebApplication Build(ServeArguments arguments, SuiteRunner runner)
    {
        var builder = WebApplication.CreateSlimBuilder();
        // The game server's appsettings.json lands beside this assembly and names its own port; none of it is this service's.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["urls"] = $"http://{arguments.Bind}:{arguments.Port}" });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();
        var runs = new ConcurrentDictionary<Guid, SuiteRun>();
        var gate = new object();
        SuiteRun? active = null;

        app.MapGet("/suite", () => Results.Json(new
        {
            games = runner.Games.Select(g => new { name = g.Name, group = g.Group, covers = g.Covers }),
        }, SuiteReport.Json));

        app.MapPost("/runs", (RunRequest? request, IHostApplicationLifetime lifetime) =>
        {
            var only = request?.Only ?? [];
            var driver = (request?.Driver ?? SimOptions.InProcess) switch
            {
                SimOptions.InProcess => DriverKind.InProcess,
                SimOptions.Socket => DriverKind.Socket,
                var other => (DriverKind?)null,
            };
            if (driver is null) return Results.BadRequest(new { error = $"driver is {SimOptions.InProcess} or {SimOptions.Socket}" });
            try { SuiteCatalog.Select(runner.Games, only); }
            catch (UsageException usage) { return Results.BadRequest(new { error = usage.Message }); }

            SuiteRun run;
            lock (gate)
            {
                if (active is { Finished: false }) return Results.Conflict(new { error = "a run is active", runId = active.Id });
                var id = Guid.CreateVersion7();
                run = active = runs[id] = new SuiteRun(id, Path.Combine(arguments.Out, id.ToString("N")));
            }
            _ = Task.Run(() => PlayAsync(runner, run, new SuiteOptions(only, driver.Value, request?.Keep ?? false, run.OutDir, run.Id),
                lifetime.ApplicationStopping));
            return Results.Accepted($"/runs/{run.Id}/events", new { runId = run.Id });
        });

        app.MapGet("/runs/{id:guid}/events", async (Guid id, HttpContext http) =>
        {
            if (!runs.TryGetValue(id, out var run)) { http.Response.StatusCode = 404; return; }
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            await http.Response.Body.FlushAsync(http.RequestAborted);
            await foreach (var message in run.ReadAsync(http.RequestAborted))
            {
                await http.Response.WriteAsync($"event: {message.Event}\ndata: {message.Data}\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
        });

        app.MapGet("/runs/{id:guid}/recordings/{game}", (Guid id, string game) =>
        {
            var path = runs.TryGetValue(id, out var run) ? run.ResultOf(game)?.RecordingPath : null;
            return path is not null && File.Exists(path) ? Results.File(Path.GetFullPath(path), "application/json") : Results.NotFound();
        });

        // A fresh session for the spectator a kept socket game was given; the live feed connects with it.
        app.MapPost("/runs/{id:guid}/games/{game}/spectate", async (Guid id, string game, HttpContext http) =>
        {
            var result = runs.TryGetValue(id, out var run) ? run.ResultOf(game) : null;
            if (result is not { GameId: { } gameId, Spectator: { } spectator }) return Results.NotFound();
            try
            {
                var session = await runner.SpectatorSessionAsync(spectator, http.RequestAborted);
                return Results.Json(new { gameId, jwt = session.Jwt }, SuiteReport.Json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { error = ex.Message }, SuiteReport.Json, statusCode: 502);
            }
        });

        return app;
    }

    private static async Task PlayAsync(SuiteRunner runner, SuiteRun run, SuiteOptions options, CancellationToken ct)
    {
        // A kept run writes its report files beside its recordings; an unkept run writes nothing, not even its directory.
        var files = options.Keep
            ? new SuiteReportFiles(options.OutDir, run.Id, options.Driver, options.Only, SuiteCatalog.Select(runner.Games, options.Only).Count)
            : null;
        try
        {
            var report = await runner.RunAsync(options, new Inline<SuiteGameResult>(r => { files?.Game(r); run.Game(r); }), ct);
            files?.Finish(report);
            run.Done(report);
        }
        catch (Exception ex)
        {
            try { files?.Abort(ex is OperationCanceledException ? "cancelled" : ex.Message); }
            catch (IOException) { }         // the run's error event still goes out
            run.Error(ex.Message);
        }
    }

    /// <summary>Reports on the runner's thread, in order (Progress&lt;T&gt; posts to the pool).</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
