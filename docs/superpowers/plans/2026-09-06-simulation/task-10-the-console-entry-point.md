# Task 10: The console entry point

**Files:**
- Create: `sim/Program.cs`, `sim/SimOptions.cs`, `sim/Summary.cs`
- Test: `tests/Tabletop.Sim.Tests/ProgramTests.cs`

**Interfaces:**
- Consumes: everything above; `NpgsqlDataSource` (Npgsql).
- Produces: `sealed record SimOptions(string? Scenario, ulong FromSeed, ulong ToSeed, string Driver, string OutputDirectory, int? RoundCap, bool Recording, bool List)` with `static SimOptions Parse(string[] args)`, `static string Usage`, `IEnumerable<ulong> Seeds`, and `sealed class UsageException(string) : Exception`; `sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<RunReport> Reports)` with `string Table()`; `static class Program` with `Task<int> Main(string[])` and `Task<int> RunAsync(SimOptions, TextWriter, CancellationToken)`.

Spec: module prompt item 7 — "Run a scenario by name, or N games from a seed range, writing recordings and a summary. Decide the argument surface and keep it small."

**The argument surface, and nothing more.**

```
dotnet run --project sim -- <scenario> [options]

  --seed N              one seed (default: the scenario's own)
  --seeds A..B          a closed range of seeds, one game each
  --driver in-process   the default; or socket
  --out DIR             where recordings and summary.json go (default: ./recordings)
  --rounds N            override the scenario's round cap
  --no-recording        run and report, write no files
  --list                list the scenario names and exit
```

Environment for `--driver socket`: `TABLETOP_DB_CONNECTION` (the same variable the servers read), `TABLETOP_SIM_WS` (default `ws://localhost:5200/ws`), `TABLETOP_MANAGEMENT_URL` (default `http://localhost:5100`, where the session mint lives). There is no token variable: the tokens are minted per run. Exit code `0` when every run passed, `1` otherwise, `2` on a usage error.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Sim;

namespace Tabletop.Sim.Tests;

public class ProgramTests
{
    [Fact]
    public void The_argument_surface_parses_a_name_a_seed_and_a_range()
    {
        var one = SimOptions.Parse(["skirmish", "--seed", "9"]);
        Assert.Equal("skirmish", one.Scenario);
        Assert.Equal([9ul], one.Seeds);
        Assert.Equal("in-process", one.Driver);

        var many = SimOptions.Parse(["skirmish", "--seeds", "3..6", "--driver", "socket", "--out", "/tmp/x", "--rounds", "2", "--no-recording"]);
        Assert.Equal([3ul, 4ul, 5ul, 6ul], many.Seeds);
        Assert.Equal("socket", many.Driver);
        Assert.Equal("/tmp/x", many.OutputDirectory);
        Assert.Equal(2, many.RoundCap);
        Assert.False(many.Recording);

        Assert.True(SimOptions.Parse(["--list"]).List);
        Assert.Throws<UsageException>(() => SimOptions.Parse([]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--driver", "carrier-pigeon"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--seeds", "6..3"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--nonsense"]));
    }

    [Fact]
    public async Task Listing_the_scenarios_exits_zero_and_names_them()
    {
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(SimOptions.Parse(["--list"]), output, TestContext.Current.CancellationToken));
        Assert.Contains("skirmish", output.ToString());
        Assert.Contains("stalemate", output.ToString());
    }

    [Fact]
    public async Task Running_a_seed_range_writes_a_recording_each_and_one_summary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        try
        {
            var code = await Program.RunAsync(
                SimOptions.Parse(["stalemate", "--seeds", "1..3", "--out", directory]), output, TestContext.Current.CancellationToken);
            Assert.Equal(0, code);
            Assert.Equal(3, Directory.GetFiles(directory, "stalemate-*.json").Length);
            Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
            Assert.Contains("3 runs", output.ToString());
            Assert.Contains("stalemate", output.ToString());

            var recording = await Recording.RecordingJson.ReadAsync(Path.Combine(directory, "stalemate-1.json"), TestContext.Current.CancellationToken);
            Assert.Equal(1ul, recording.Meta.Seed);
            Assert.Equal("in-process", recording.Meta.Driver);
            Assert.NotEmpty(recording.Frames);

            // RunLoop always leaves RunReport.RecordingPath null; this is the only code that fills it in,
            // and the path reaches summary.json by no other route.
            Assert.Contains("stalemate-1.json",
                await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"), TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task An_unknown_scenario_is_a_usage_error()
    {
        var output = new StringWriter();
        Assert.Equal(2, await Program.RunAsync(SimOptions.Parse(["no-such-scenario"]), output, TestContext.Current.CancellationToken));
        Assert.Contains("no scenario", output.ToString());
    }

    [Fact]
    public async Task No_recording_writes_nothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(
            SimOptions.Parse(["stalemate", "--seed", "1", "--out", directory, "--no-recording"]), output, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(directory));
    }
}
```

- [ ] **Step 2: Run; expect failure, then write `SimOptions.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter ProgramTests`
Expected: `CS0246: ... 'SimOptions' could not be found`.

```csharp
namespace Tabletop.Sim;

public sealed class UsageException(string message) : Exception(message);

public sealed record SimOptions(
    string? Scenario, ulong FromSeed, ulong ToSeed, string Driver,
    string OutputDirectory, int? RoundCap, bool Recording, bool List)
{
    public const string InProcess = "in-process";
    public const string Socket = "socket";

    public static string Usage => """
        dotnet run --project sim -- <scenario> [options]

          --seed N              one seed (default: the scenario's own)
          --seeds A..B          a closed range of seeds, one game each
          --driver in-process   the default; or socket
          --out DIR             where recordings and summary.json go (default: ./recordings)
          --rounds N            override the scenario's round cap
          --no-recording        run and report, write no files
          --list                list the scenario names and exit

        For --driver socket: TABLETOP_DB_CONNECTION, TABLETOP_SIM_WS (default ws://localhost:5200/ws)
        and TABLETOP_MANAGEMENT_URL (default http://localhost:5100). Session tokens are minted from
        POST /v1/dev/sessions, which needs the management API run as a development environment.
        """;

    public IEnumerable<ulong> Seeds
    {
        get { for (var seed = FromSeed; seed <= ToSeed; seed++) yield return seed; }
    }

    public static SimOptions Parse(string[] args)
    {
        string? scenario = null, driver = InProcess, output = "recordings";
        ulong? from = null, to = null;
        int? rounds = null;
        var recording = true;
        var list = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list": list = true; break;
                case "--no-recording": recording = false; break;
                case "--seed": from = to = ULong(Next(args, ref i, "--seed")); break;
                case "--seeds":
                    var parts = Next(args, ref i, "--seeds").Split("..", StringSplitOptions.TrimEntries);
                    if (parts.Length != 2) throw new UsageException("--seeds wants A..B");
                    from = ULong(parts[0]); to = ULong(parts[1]);
                    if (from > to) throw new UsageException($"--seeds {from}..{to} runs backwards");
                    break;
                case "--driver":
                    driver = Next(args, ref i, "--driver");
                    if (driver is not (InProcess or Socket)) throw new UsageException($"--driver is {InProcess} or {Socket}, not '{driver}'");
                    break;
                case "--out": output = Next(args, ref i, "--out"); break;
                case "--rounds": rounds = int.Parse(Next(args, ref i, "--rounds")); break;
                default:
                    if (args[i].StartsWith('-')) throw new UsageException($"unknown option '{args[i]}'\n\n{Usage}");
                    if (scenario is not null) throw new UsageException($"one scenario at a time; got '{scenario}' and '{args[i]}'");
                    scenario = args[i];
                    break;
            }
        }

        if (!list && scenario is null) throw new UsageException(Usage);
        var seed = from ?? (scenario is not null && Scenarios.ScenarioCatalog.TryGet(scenario, out var s) ? s.Seed : 1);
        return new SimOptions(scenario, seed, to ?? seed, driver, output, rounds, recording, list);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{option} wants a value");

    private static ulong ULong(string text) =>
        ulong.TryParse(text, out var value) ? value : throw new UsageException($"'{text}' is not a seed");
}
```
- [ ] **Step 3: Write `Summary.cs`**

```csharp
using Tabletop.Sim.Run;

namespace Tabletop.Sim;

public sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<RunReport> Reports)
{
    public static Summary Of(string driver, IReadOnlyList<RunReport> reports) => new(
        driver, reports.Count, reports.Count(r => r.Passed), reports.Count(r => !r.Passed),
        reports.Sum(r => r.DurationMs), reports);

    public string Table()
    {
        var lines = new List<string>
        {
            $"{Runs} runs on {Driver}: {Passed} passed, {Failed} failed, {TotalMs:N0} ms",
            "  seed  outcome      rounds  seq   frames  asks   ms",
        };
        lines.AddRange(Reports.Select(r =>
            $"  {r.Seed,-5} {r.Outcome.Kind,-12} {r.Rounds,-7} {r.LastSeq,-5} {r.Frames,-7} {r.AsksAnswered,-6} {r.DurationMs,-6:N0}"));
        lines.AddRange(Reports.Where(r => !r.Passed).SelectMany(r =>
            r.Failures.Select(f => $"  seed {r.Seed}: {f}")));
        return string.Join("\n", lines);
    }
}
```

- [ ] **Step 4: Write `Program.cs`**

```csharp
using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(SimOptions.Parse(args), Console.Out, CancellationToken.None); }
        catch (UsageException usage) { Console.Error.WriteLine(usage.Message); return 2; }
    }

    public static async Task<int> RunAsync(SimOptions options, TextWriter output, CancellationToken ct)
    {
        if (options.List)
        {
            foreach (var name in ScenarioCatalog.Names) output.WriteLine(name);
            return 0;
        }

        if (!ScenarioCatalog.TryGet(options.Scenario!, out var template))
        {
            output.WriteLine($"no scenario '{options.Scenario}'; known: {string.Join(", ", ScenarioCatalog.Names)}");
            return 2;
        }

        var reports = new List<RunReport>();
        foreach (var seed in options.Seeds)
        {
            var scenario = template.WithSeed(seed);
            if (options.RoundCap is { } cap) scenario = scenario with { RoundCap = cap };
            reports.Add(await PlayAsync(scenario, options, ct));
        }

        var summary = Summary.Of(options.Driver, reports);
        output.WriteLine(summary.Table());
        if (options.Recording)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "summary.json"),
                System.Text.Json.JsonSerializer.Serialize(summary, RecordingJson.Options), ct);
        }
        return summary.Failed == 0 ? 0 : 1;
    }

    private static async Task<RunReport> PlayAsync(Scenario scenario, SimOptions options, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        var answers = new ComposedAnswers(scenario.Script, scenario.ScriptOnly ? null : new AnswerPolicy(new Chooser(scenario.Seed)));

        await using var driver = options.Driver == SimOptions.Socket
            ? await OpenSocketAsync(compiled, ct)
            : await InProcessDriver.StartAsync(compiled, ct);

        var loop = new RunLoop(driver, compiled, answers, new Chooser(scenario.Seed ^ 0x5EED));
        var report = await loop.RunAsync(ct);

        if (!options.Recording) return report;
        var path = Path.Combine(options.OutputDirectory, $"{scenario.Name}-{scenario.Seed}.json");
        await RecordingJson.WriteAsync(loop.BuildRecording(report), path, ct);
        return report with { RecordingPath = path };
    }

    private static async Task<IGameDriver> OpenSocketAsync(CompiledScenario compiled, CancellationToken ct)
    {
        var connection = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
            ?? throw new UsageException("TABLETOP_DB_CONNECTION is not set; --driver socket needs the same database the servers use");
        var address = Environment.GetEnvironmentVariable("TABLETOP_SIM_WS") ?? "ws://localhost:5200/ws";
        var management = Environment.GetEnvironmentVariable(SocketCredentials.ManagementUrlVariable) ?? "http://localhost:5100";
        using var http = new HttpClient { BaseAddress = new Uri(management) };
        var credentials = await SocketCredentials.MintAsync(compiled, http, ct);

        var db = NpgsqlDataSource.Create(connection);
        await new SocketRowSeeder(db, new PostgresGameRows(db)).SeedAsync(compiled, credentials, ct);
        return await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(new Uri(address)), credentials, db, ct);
    }
}
```

A socket run leaves its rows and its log in Postgres on purpose: the game is then in the database to look at, and `sim/` is a developer tool. `SocketRowSeeder.DeleteAsync` exists for the tests, which must not accumulate games. Repeating a `--driver socket` run with the same seed now makes a *second* game rather than colliding on the primary key, because the game id is random and only `rng_seed` repeats — so a seed range leaves one row per run and `--driver socket` accumulates games until someone deletes them. That is the price of the seed being stored rather than derived, and it is the right way round: the two runs are separate games that rolled the same dice.

- [ ] **Step 5: Run the tests and the console for real**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter ProgramTests
dotnet run --project sim -- --list
dotnet run --project sim -- skirmish --seeds 1..20 --out /tmp/sim
```

Expected: twenty runs, every one `decided` or `round_limit`, no failures, twenty recordings and a `summary.json` in `/tmp/sim`, and the whole thing in a few seconds. A `stuck` run here is the simulator doing its job: read the dumped frames, and the bug is in the scheduler or the interpreter.

- [ ] **Step 6: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests
git commit -m "Run a scenario or a seed range from the console, writing recordings and a summary"
```
