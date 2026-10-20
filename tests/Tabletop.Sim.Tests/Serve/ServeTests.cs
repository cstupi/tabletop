using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Serve;
using Tabletop.Sim.Suite;
using Tabletop.Sim.Tests.Suite;

namespace Tabletop.Sim.Tests.Serve;

/// <summary>The runner service on a real Kestrel port, driven over HTTP with the sample game and the in-process driver.</summary>
public sealed class ServeTests : IAsyncLifetime
{
    private readonly string outDir = SuiteFixtures.TempDir();
    private WebApplication app = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync() => await StartAsync(new SuiteRunner([SuiteFixtures.Game()]));

    private async Task StartAsync(SuiteRunner runner)
    {
        app = ServeApp.Build(new ServeArguments(0, "127.0.0.1", outDir), runner);
        await app.StartAsync();
        http = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await app.DisposeAsync();
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<Guid> PostRunAsync(object body)
    {
        var response = await http.PostAsJsonAsync("/runs", body, Web);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("runId").GetGuid();
    }

    private async Task<List<(string Event, JsonElement Data)>> EventsAsync(Guid id)
    {
        var events = new List<(string, JsonElement)>();
        using var response = await http.GetAsync($"/runs/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        string? name = null;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("event: ")) name = line[7..];
            else if (line.StartsWith("data: ")) events.Add((name!, JsonDocument.Parse(line[6..]).RootElement.Clone()));
        }
        return events;
    }

    [Fact]
    public async Task Suite_lists_every_game_with_group_and_covers()
    {
        var body = await http.GetFromJsonAsync<JsonElement>("/suite", Web);
        var game = Assert.Single(body.GetProperty("games").EnumerateArray());
        Assert.Equal(SuiteFixtures.Name, game.GetProperty("name").GetString());
        Assert.Equal(SuiteFixtures.Group, game.GetProperty("group").GetString());
        Assert.Equal("AP", Assert.Single(game.GetProperty("covers").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_run_streams_one_game_event_per_result_then_done_with_the_report()
    {
        var id = await PostRunAsync(new { only = new[] { SuiteFixtures.Name }, driver = "in-process", keep = false });
        var events = await EventsAsync(id);

        Assert.Equal(["game", "done"], events.Select(e => e.Event));
        Assert.Equal(SuiteFixtures.Name, events[0].Data.GetProperty("name").GetString());
        var report = events[1].Data;
        Assert.Equal(1, report.GetProperty("games").GetArrayLength());
        Assert.True(report.TryGetProperty("coverage", out _));
        Assert.True(report.TryGetProperty("passed", out _));
    }

    [Fact]
    public async Task A_second_run_while_one_is_active_is_refused_with_409()
    {
        // The first run waits on a database that is not there (a one-second connect timeout), so it is still active when the second arrives.
        await StartWithUnreachableDatabaseAsync();
        var id = await PostRunAsync(new { driver = "socket" });
        var refused = await http.PostAsJsonAsync("/runs", new { }, Web);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(id, (await refused.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("runId").GetGuid());
        await EventsAsync(id);
        Assert.NotEqual(Guid.Empty, await PostRunAsync(new { }));
    }


    private async Task StartWithUnreachableDatabaseAsync()
    {
        await app.DisposeAsync();
        await StartAsync(new SuiteRunner([SuiteFixtures.Game()], new SuiteSocket(new UnreachableRows(), (_, _) => throw new InvalidOperationException(), () => throw new InvalidOperationException())));
    }

    /// <summary>A database that is not there: every call fails after a second, as a one-second connect timeout does.</summary>
    private sealed class UnreachableRows : ISocketRows
    {
        static async Task<T> Fail<T>(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            throw new InvalidOperationException("the database is unreachable");
        }
        public Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct) => Fail<bool>(ct);
        public Task DeleteAsync(Guid gameId, CancellationToken ct) => Fail<bool>(ct);
        public Task SeedSpectatorAsync(Guid gameId, Guid accountId, string name, CancellationToken ct) => Fail<bool>(ct);
        public Task<IAsyncDisposable> HoldAsync(Guid runId, CancellationToken ct) => Fail<IAsyncDisposable>(ct);
        public Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct) => Fail<IReadOnlyList<Guid>>(ct);
    }

    [Fact]
    public async Task A_run_is_accepted_again_once_the_last_one_is_done()
    {
        await EventsAsync(await PostRunAsync(new { }));
        Assert.NotEqual(Guid.Empty, await PostRunAsync(new { }));
    }

    [Fact]
    public async Task An_unknown_game_or_driver_is_a_400_and_starts_no_run()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/runs", new { only = new[] { "nope" } }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/runs", new { driver = "carrier-pigeon" }, Web)).StatusCode);
        Assert.NotEqual(Guid.Empty, await PostRunAsync(new { }));
    }

    [Fact]
    public async Task An_unknown_run_has_no_events_and_no_recordings()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/runs/{Guid.NewGuid()}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/runs/{Guid.NewGuid()}/recordings/x")).StatusCode);
    }

    [Fact]
    public async Task A_kept_game_serves_its_recording_and_an_unkept_run_serves_none()
    {
        var kept = await PostRunAsync(new { keep = true });
        await EventsAsync(kept);
        var recording = await http.GetAsync($"/runs/{kept}/recordings/{SuiteFixtures.Name}");
        Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
        Assert.Equal("application/json", recording.Content.Headers.ContentType?.MediaType);
        JsonDocument.Parse(await recording.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/runs/{kept}/recordings/not-a-game")).StatusCode);

        var plain = await PostRunAsync(new { keep = false });
        await EventsAsync(plain);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/runs/{plain}/recordings/{SuiteFixtures.Name}")).StatusCode);
        Assert.True(File.Exists(Path.Combine(outDir, kept.ToString("N"), $"suite-{kept:N}.json")));     // the run's report, under the run's id
        Assert.True(File.Exists(Path.Combine(outDir, kept.ToString("N"), $"suite-{kept:N}.md")));
        Assert.False(Directory.Exists(Path.Combine(outDir, plain.ToString("N"))));     // an unkept run writes nothing, not even its directory
    }

    [Fact]
    public async Task Spectate_is_a_404_for_an_unknown_run_an_unknown_game_and_a_game_not_kept_on_the_socket()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/runs/{Guid.NewGuid()}/games/x/spectate", null)).StatusCode);
        var run = await PostRunAsync(new { keep = true });      // in-process: kept, but no game row to watch
        await EventsAsync(run);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/runs/{run}/games/{SuiteFixtures.Name}/spectate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/runs/{run}/games/nope/spectate", null)).StatusCode);
    }

    [Fact]
    public async Task A_recording_is_served_as_soon_as_its_game_is_reported()
    {
        var run = await PostRunAsync(new { keep = true });
        await EventsAsync(run);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/runs/{run}/recordings/{SuiteFixtures.Name}")).StatusCode);
    }

    [Fact]
    public async Task A_late_reader_replays_a_finished_runs_events()
    {
        var id = await PostRunAsync(new { });
        var first = await EventsAsync(id);
        var second = await EventsAsync(id);
        Assert.Equal(first.Select(e => e.Event), second.Select(e => e.Event));
    }

    [Fact]
    public async Task A_run_that_fails_after_the_202_ends_with_an_error_event()
    {
        // The socket run's database is unreachable, so the run lock fails: the stream ends with `error`, not `done`.
        await StartWithUnreachableDatabaseAsync();
        var events = await EventsAsync(await PostRunAsync(new { driver = "socket" }));
        Assert.Equal("error", Assert.Single(events).Event);
    }


    [Fact]
    public void Serve_arguments_default_to_loopback_5180_and_reject_bad_ports()
    {
        var defaults = ServeArguments.Parse([]);
        Assert.Equal((5180, "127.0.0.1"), (defaults.Port, defaults.Bind));
        Assert.Equal(("0.0.0.0", 6000), (ServeArguments.Parse(["--bind", "0.0.0.0", "--port", "6000"]).Bind, ServeArguments.Parse(["--port", "6000"]).Port));
        Assert.Throws<UsageException>(() => ServeArguments.Parse(["--port", "x"]));
        Assert.Throws<UsageException>(() => ServeArguments.Parse(["--nope"]));
    }
}
