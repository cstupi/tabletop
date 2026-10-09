using Npgsql;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Persistence;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.Length > 0 && args[0] == SuiteArguments.Verb
                ? await RunSuiteAsync(SuiteArguments.Parse(args[1..]), Console.Out, new SuiteRunner(), CancellationToken.None)
                : await RunAsync(SimOptions.Parse(args), Console.Out, CancellationToken.None);
        }
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

        return await RunScenarioAsync(template, options, output, ct);
    }

    /// <summary>
    /// The rules suite: --list prints the selected games, otherwise the runner plays them and the report is printed, a line per game
    /// as it finishes and the tables at the end. Exit 0 only when the report passed. suite.json goes to --out with --keep or --out.
    /// </summary>
    public static async Task<int> RunSuiteAsync(SuiteArguments arguments, TextWriter output, SuiteRunner runner, CancellationToken ct)
    {
        if (arguments.List)
        {
            foreach (var game in SuiteCatalog.Select(runner.Games, arguments.Only))
                output.WriteLine($"{game.Name}  [{game.Group}]  {string.Join(", ", game.Covers)}");
            return 0;
        }

        var outDir = arguments.Out ?? SuiteArguments.DefaultOut;
        var report = await runner.RunAsync(new SuiteOptions(arguments.Only, arguments.Driver, arguments.Keep, outDir),
            new Inline<SuiteGameResult>(r => output.WriteLine($"{(r.Passed ? "pass" : "FAIL")}  {r.Name}")), ct);
        output.WriteLine(report.Table());
        if (arguments.Keep || arguments.Out is not null)
        {
            Directory.CreateDirectory(outDir);
            await File.WriteAllTextAsync(Path.Combine(outDir, SuiteReport.FileName), report.ToJson(), ct);
        }
        return report.ExitCode;
    }

    /// <summary>Reports on the caller's thread, so the per-game lines come out in order before the tables (Progress&lt;T&gt; posts to the pool).</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>Every seed of the options' range against one scenario; the seam a test uses for a scenario the catalog does not hold.</summary>
    public static async Task<int> RunScenarioAsync(Scenario template, SimOptions options, TextWriter output, CancellationToken ct)
    {
        var reports = new List<RunReport>();
        foreach (var seed in options.Seeds)
        {
            var scenario = template.WithSeed(seed);
            if (options.RoundCap is { } cap) scenario = scenario with { RoundCap = cap };
            try { reports.Add(await PlayAsync(scenario, options, ct)); }
            // A setup fault (a refused seed, a dead server) fails this seed; the range and the summary go on.
            catch (Exception ex) when (ex is not (UsageException or OperationCanceledException))
            {
                reports.Add(new RunReport(scenario.Name, scenario.Seed, options.Driver, new RunOutcome.Failed(ex.ToString()),
                    0, 0, 0, 0, 0, [$"failed: {ex}"], RecordingPath: null));
            }
        }

        var summary = Summary.Of(options.Driver, reports);
        output.WriteLine(summary.Table());
        if (options.Recording)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "summary.json"), summary.ToJson(), ct);
        }
        return summary.Failed == 0 ? 0 : 1;
    }

    private static async Task<RunReport> PlayAsync(Scenario scenario, SimOptions options, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        var answers = new ComposedAnswers(scenario.Script, scenario.Policy());

        NpgsqlDataSource? db = null;
        try
        {
            IGameDriver driver;
            if (options.Driver == SimOptions.Socket)
            {
                DotEnv.Load(Directory.GetCurrentDirectory());
                db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
                    ?? throw new UsageException("TABLETOP_DB_CONNECTION is not set; --driver socket needs the same database the servers use"));
                driver = await OpenSocketAsync(compiled, db, ct);
            }
            else driver = await InProcessDriver.StartAsync(compiled, ct);

            await using (driver)
            {
                var loop = new RunLoop(driver, compiled, answers, HandPlacement.ChooserFor(scenario.Seed));
                var report = await loop.RunAsync(ct);

                if (!options.Recording) return report;
                var path = Path.Combine(options.OutputDirectory, $"{scenario.Name}-{scenario.Seed}.json");
                await RecordingJson.WriteAsync(loop.BuildRecording(report), path, ct);
                return report with { RecordingPath = path };
            }
        }
        finally { if (db is not null) await db.DisposeAsync(); }
    }

    private static async Task<IGameDriver> OpenSocketAsync(CompiledScenario compiled, NpgsqlDataSource db, CancellationToken ct)
    {
        var address = Environment.GetEnvironmentVariable("TABLETOP_SIM_WS") ?? SimOptions.DefaultWebSocket;
        var management = Environment.GetEnvironmentVariable(SocketCredentials.ManagementUrlVariable) ?? SimOptions.DefaultManagement;
        using var http = new HttpClient { BaseAddress = new Uri(management) };
        var credentials = await SocketCredentials.MintAsync(compiled, http, ct);

        // The rows and the log stay in Postgres on purpose: the game is there to look at.
        await new SocketRowSeeder(db, new PostgresGameRows(db)).SeedAsync(compiled, credentials, ct);
        return await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(new Uri(address)), credentials, ct);
    }
}
