using Npgsql;
using Tabletop.GameServer.Configuration;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Lifecycle;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Serve;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == ServeArguments.Verb)
            {
                await ServeApp.Build(ServeArguments.Parse(args[1..]), new SuiteRunner()).RunAsync();
                return 0;
            }
            if (args.Length > 0 && args[0] == SuiteArguments.Verb)
            {
                // The first Ctrl+C cancels the run, so its report is written as aborted; a second ends the process.
                using var cancel = new CancellationTokenSource();
                ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = !cancel.IsCancellationRequested; cancel.Cancel(); };
                Console.CancelKeyPress += onCancel;
                try { return await RunSuiteAsync(SuiteArguments.Parse(args[1..]), Console.Out, new SuiteRunner(), cancel.Token); }
                finally { Console.CancelKeyPress -= onCancel; }
            }
            if (args.Length > 0 && args[0] == LifecycleArguments.Verb)
            {
                using var cancel = new CancellationTokenSource();
                ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = !cancel.IsCancellationRequested; cancel.Cancel(); };
                Console.CancelKeyPress += onCancel;
                try { return await RunLifecycleAsync(LifecycleArguments.Parse(args[1..]), Console.Out, LifecycleEndpoints.FromEnvironment(), cancel.Token); }
                finally { Console.CancelKeyPress -= onCancel; }
            }
            return await RunAsync(SimOptions.Parse(args), Console.Out, CancellationToken.None);
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
    /// The rules suite: --list prints the selected games, otherwise the runner plays them. The report (SuiteReportFiles) is written to
    /// --out after every game and at the end, or when the run aborts. The console gets each failing game with its lines and replay as it
    /// finishes, a counter, the coverage entries the run fails on, the verdict line and the report's paths; --verbose adds a line per
    /// game and the whole coverage table. Exit 0 only when the report passed.
    /// </summary>
    public static async Task<int> RunSuiteAsync(SuiteArguments arguments, TextWriter output, SuiteRunner runner, CancellationToken ct)
    {
        if (arguments.List)
        {
            foreach (var game in SuiteCatalog.Select(runner.Games, arguments.Only))
                output.WriteLine($"{game.Name}  [{game.Group}]  {string.Join(", ", game.Covers)}");
            return 0;
        }

        var planned = SuiteCatalog.Select(runner.Games, arguments.Only).Count;      // an unknown name is a usage error before any file
        var outDir = arguments.Out ?? SuiteArguments.DefaultOut;
        var runId = Guid.CreateVersion7();
        var files = new SuiteReportFiles(outDir, runId, arguments.Driver, arguments.Only, planned);
        output.WriteLine($"suite run {runId:N}: {planned} games, {(arguments.Driver == DriverKind.Socket ? SimOptions.Socket : SimOptions.InProcess)}; report {files.MarkdownPath}");

        var (passed, failed) = (0, 0);
        void Progress(SuiteGameResult r)
        {
            files.Game(r);
            if (r.Passed) passed++; else failed++;
            var stale = r.StaleLines.ToList();
            if (arguments.Verbose || !r.Passed || stale.Count > 0)
                output.WriteLine($"{r.Verdict}  {r.Name}  [{r.Group}]  seed {r.Seed}  {r.DurationSeconds:0.0}s");
            foreach (var line in stale) output.WriteLine($"          {line}");
            foreach (var line in r.Failures.SelectMany(f => f.Split('\n'))) output.WriteLine($"          {line}");
            if (!r.Passed) output.WriteLine($"          replay: {r.Replay}");
            if (!arguments.Verbose && (passed + failed) % ProgressEvery == 0 && passed + failed < planned)
                output.WriteLine($"[{passed + failed}/{planned}] {passed} passed, {failed} failed");
        }

        SuiteReport report;
        try
        {
            report = await runner.RunAsync(new SuiteOptions(arguments.Only, arguments.Driver, arguments.Keep, outDir, runId, arguments.Seed),
                new Inline<SuiteGameResult>(Progress), ct);
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            files.Abort(ex is OperationCanceledException ? "cancelled" : ex.Message);
            output.WriteLine($"ABORTED: {(ex is OperationCanceledException ? "cancelled" : ex.ToString())}");
            output.WriteLine($"{passed + failed} of {planned} games played, {passed} passed, {failed} failed; run {runId:N}");
            WriteReportPaths(output, files);
            return 1;
        }

        files.Finish(report);
        var coverage = report.CoverageLines(problemsOnly: !arguments.Verbose).ToList();
        if (arguments.Verbose || coverage.Count > 1) foreach (var line in coverage) output.WriteLine(line);
        output.WriteLine(report.Verdict);
        WriteReportPaths(output, files);
        return report.ExitCode;
    }

    /// <summary>
    /// The lifecycle suite: --list prints the shapes, otherwise each is played against the stack and gets a line as it finishes, a failing
    /// one its failure lines after it, then the verdict. Exit 0 only when every shape passed.
    /// </summary>
    public static async Task<int> RunLifecycleAsync(LifecycleArguments arguments, TextWriter output, LifecycleEndpoints endpoints, CancellationToken ct)
    {
        var shapes = LifecycleShapes.Select(arguments.Only);
        if (arguments.List)
        {
            foreach (var shape in shapes) output.WriteLine($"{shape.Name}  {shape.Summary}");
            return 0;
        }
        output.WriteLine($"lifecycle: {shapes.Count} shapes against {endpoints.Management} and {endpoints.GameServer}");
        var results = await new LifecycleRunner(endpoints).RunAsync(shapes, new Inline<LifecycleResult>(r =>
        {
            output.WriteLine($"{(r.Passed ? "pass" : "FAIL")}  {r.Shape}  {r.Seconds:0.0}s");
            foreach (var line in r.Failures.SelectMany(f => f.Split('\n'))) output.WriteLine($"          {line}");
        }), ct);
        var failed = results.Count(r => !r.Passed);
        output.WriteLine($"{results.Count - failed} of {results.Count} shapes passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>A passing game prints nothing of its own; a counter line comes every this many games.</summary>
    private const int ProgressEvery = 25;

    private static void WriteReportPaths(TextWriter output, SuiteReportFiles files) =>
        output.WriteLine($"report: {files.MarkdownPath}\n        {files.JsonPath}\n        (copied to {SuiteReportFiles.Latest}.md and .json)");

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
        await new SocketRowSeeder(db).SeedAsync(compiled, credentials, ct);
        return await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(new Uri(address)), credentials, ct);
    }
}
