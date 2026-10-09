using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public enum DriverKind { InProcess, Socket }

/// <summary>Only: game names and groups, none for every game (a full run). OutDir: where a kept game's recording goes.</summary>
public sealed record SuiteOptions(IReadOnlyList<string> Only, DriverKind Driver, bool Keep, string OutDir);

/// <summary>
/// Plays the selected games one after another on one driver, judges each, and computes coverage. A socket run sweeps what an
/// interrupted run left, then seeds, plays and deletes each game's rows, pass or fail, unless Keep. Keep writes each game's recording.
/// </summary>
/// <param name="games">The games to select from; the catalog by default.</param>
/// <param name="socket">The socket environment; from the process environment by default. One handed in is not disposed here.</param>
/// <param name="properties">The pinned set coverage is measured against; the shipped set by default.</param>
public sealed class SuiteRunner(IReadOnlyList<TestGame>? games = null, SuiteSocket? socket = null, PropertySet? properties = null)
{
    public IReadOnlyList<TestGame> Games { get; } = games ?? SuiteCatalog.All;

    public async Task<SuiteReport> RunAsync(SuiteOptions options, IProgress<SuiteGameResult>? progress, CancellationToken ct)
    {
        var runId = Guid.CreateVersion7();
        var selected = SuiteCatalog.Select(Games, options.Only);
        var sock = options.Driver == DriverKind.Socket ? socket ?? SuiteSocket.FromEnvironment() : null;
        var results = new List<SuiteGameResult>();
        var runs = new List<SuiteGameRun>();
        try
        {
            if (sock is not null) await new SuiteRows(sock.Db).SweepAsync(runId, ct);
            foreach (var game in selected)
            {
                var (result, run) = await PlayAsync(game, options, runId, sock, ct);
                results.Add(result);
                runs.Add(run);
                progress?.Report(result);
            }
        }
        finally { if (sock is not null && sock != socket) await sock.DisposeAsync(); }

        var coverage = Coverage.Of(properties ?? ScenarioCompiler.CoreProperties, runs, fullRun: options.Only.Count == 0);
        return new SuiteReport(runId, results, coverage, results.All(r => r.Passed) && Coverage.Complete(coverage));
    }

    /// <summary>The command line that reruns one game alone and keeps it.</summary>
    public static string ReplayOf(TestGame game, DriverKind driver) =>
        $"dotnet run --project sim -- suite --only {Quote(game.Name)} --keep{(driver == DriverKind.Socket ? " --driver socket" : "")}";

    private static async Task<(SuiteGameResult, SuiteGameRun)> PlayAsync(
        TestGame game, SuiteOptions options, Guid runId, SuiteSocket? sock, CancellationToken ct)
    {
        // The step gate keeps a completed step's rolls on the stack, which is where SetDice reaches them.
        var scenario = game.Dice.Count > 0 ? game.Scenario with { WaitsBetweenSteps = true } : game.Scenario;
        var compiled = ScenarioCompiler.Compile(scenario);
        if (sock is not null)
            compiled = compiled with { Info = compiled.Info with { Name = SuiteRows.GameName(runId, game.Name, options.Keep) } };

        var failures = new List<string>();
        SuiteGameRun? run = null;
        string? recordingPath = null;
        SocketCredentials? credentials = null;
        try
        {
            IGameDriver inner;
            if (sock is not null)
            {
                credentials = await sock.CredentialsAsync(compiled, ct);
                await new SocketRowSeeder(sock.Db, new PostgresGameRows(sock.Db)).SeedAsync(compiled, credentials, ct);
                inner = await SocketDriver.StartAsync(compiled, sock.Connector(), credentials, ct);
            }
            else inner = await InProcessDriver.StartAsync(compiled, ct);

            await using var driver = new DiceFixingDriver(inner, game.Dice);
            // The invariants are checked here rather than in the loop, so a game's Invariants check reads them apart from its expectations.
            var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, scenario.Policy()),
                HandPlacement.ChooserFor(scenario.Seed), new RunSettings(CheckInvariants: false));
            var report = await loop.RunAsync(ct);
            var final = await driver.ProjectionAsync(ct);
            run = new SuiteGameRun(game, report.Outcome, EventApplier.EffectiveLog(loop.Log), final,
                await InvariantFailuresAsync(inner, report.Outcome, final, loop.Log, compiled, ct));

            failures.AddRange(report.Failures);     // the scenario's expectations; a stuck or failed run's detail
            failures.AddRange(driver.Failures);
            failures.AddRange(Judge(game, run));

            if (options.Keep)
            {
                recordingPath = Path.Combine(options.OutDir, $"{FileNameOf(game.Name)}-{scenario.Seed}.json");
                await RecordingJson.WriteAsync(loop.BuildRecording(report with { Failures = failures }), recordingPath, ct);
            }
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or UsageException))
        {
            failures.Add($"setup: {ex.Message}");
        }
        finally
        {
            if (sock is not null && !options.Keep)
                await new SuiteRows(sock.Db).DeleteGameAsync(compiled.Info.Id,
                    credentials?.Sessions.Select(s => s.AccountId).ToList() ?? [], CancellationToken.None);
        }

        run ??= new SuiteGameRun(game, new RunOutcome.Failed(failures.LastOrDefault() ?? "setup"), [],
            new EventApplier(compiled.Board, compiled.Info).Projection, []);
        var result = new SuiteGameResult(game.Name, game.Group, failures.Count == 0, failures, scenario.Seed,
            ReplayOf(game, options.Driver), recordingPath, sock is not null && options.Keep ? compiled.Info.Id : null);
        return (result, run);
    }

    private static IEnumerable<string> Judge(TestGame game, SuiteGameRun run)
    {
        foreach (var check in game.Checks)
        {
            string? failure;
            try { failure = check.Failure(run); }
            catch (Exception ex) { failure = $"threw {ex.GetType().Name}: {ex.Message}"; }
            if (failure is not null) yield return $"{check.Name}: {failure}";
        }
    }

    private static async Task<IReadOnlyList<string>> InvariantFailuresAsync(IGameDriver inner, RunOutcome outcome, Projection final,
        IReadOnlyList<GameEvent> log, CompiledScenario compiled, CancellationToken ct)
    {
        if (outcome is RunOutcome.Paused) return [];
        var failures = Invariants.Invariants.CheckAll(final, log, compiled).Where(r => !r.Held).Select(r => $"{r.Name}: {r.Detail}").ToList();
        if (inner is InProcessDriver inProcess)
            failures.AddRange(ProjectionComparer.Differences(await inProcess.ServerProjectionAsync(ct), final)
                .Select(d => $"the driver's projection differs from the server's — {d}"));
        return failures;
    }

    private static string Quote(string name) => name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.') ? name : $"\"{name}\"";

    private static string FileNameOf(string name) =>
        new([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')]);
}
