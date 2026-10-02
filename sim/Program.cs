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

        // Scenario-local spells are folded into the pinned PropertySet and have no authored document, so
        // the game server could not load them from Postgres.
        if (options.Driver == SimOptions.Socket && template.Spells.Count > 0)
        {
            output.WriteLine($"scenario '{template.Name}' declares its own spells, which have no authored properties document the game server can load; --driver socket cannot run it (use --driver in-process)");
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
            await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "summary.json"), summary.ToJson(), ct);
        }
        return summary.Failed == 0 ? 0 : 1;
    }

    private static async Task<RunReport> PlayAsync(Scenario scenario, SimOptions options, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        var answers = new ComposedAnswers(scenario.Script, scenario.ScriptOnly ? null : new AnswerPolicy(new Chooser(scenario.Seed)));

        NpgsqlDataSource? db = null;
        try
        {
            IGameDriver driver;
            if (options.Driver == SimOptions.Socket)
            {
                db = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
                    ?? throw new UsageException("TABLETOP_DB_CONNECTION is not set; --driver socket needs the same database the servers use"));
                driver = await OpenSocketAsync(compiled, db, ct);
            }
            else driver = await InProcessDriver.StartAsync(compiled, ct);

            await using (driver)
            {
                var loop = new RunLoop(driver, compiled, answers, new Chooser(scenario.Seed ^ 0x5EED));
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
