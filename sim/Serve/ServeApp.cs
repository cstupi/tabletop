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
            _ = Task.Run(() => PlayAsync(runner, run, new SuiteOptions(only, driver.Value, request?.Keep ?? false, run.OutDir),
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
            var path = runs.TryGetValue(id, out var run)
                ? run.Report?.Games.FirstOrDefault(g => g.Name == game)?.RecordingPath : null;
            return path is not null && File.Exists(path) ? Results.File(Path.GetFullPath(path), "application/json") : Results.NotFound();
        });

        return app;
    }

    private static async Task PlayAsync(SuiteRunner runner, SuiteRun run, SuiteOptions options, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(options.OutDir);
            var report = await runner.RunAsync(options, new Inline<SuiteGameResult>(run.Game), ct);
            if (options.Keep) await File.WriteAllTextAsync(Path.Combine(options.OutDir, SuiteReport.FileName), report.ToJson(), ct);
            run.Done(report);
        }
        catch (Exception ex) { run.Error(ex.Message); }
    }

    /// <summary>Reports on the runner's thread, in order (Progress&lt;T&gt; posts to the pool).</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
