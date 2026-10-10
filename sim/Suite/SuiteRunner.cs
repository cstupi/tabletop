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
/// Plays the selected games one after another on one driver, judges each, and computes coverage. Every game is judged by Ended,
/// Invariants and FiredAll whether or not it lists them, then by its own checks. A socket run holds its run lock, sweeps what an
/// interrupted run left, then seeds, plays and deletes each game's rows, pass or fail, unless Keep. Keep writes each game's recording.
/// </summary>
/// <param name="games">The games to select from; the catalog by default.</param>
/// <param name="socket">The socket environment; from the process environment by default. One handed in is not disposed here.</param>
/// <param name="properties">The pinned set coverage is measured against; the shipped set by default.</param>
public sealed class SuiteRunner(IReadOnlyList<TestGame>? games = null, SuiteSocket? socket = null, PropertySet? properties = null)
{
    /// <summary>The wrappers scripts/ArmyBooks generated (the generated part's entries with effects), which coverage counts by pattern.</summary>
    static readonly IReadOnlySet<string> Generated =
        PropertiesSeeder.EmbeddedPart(PropertiesSeeder.GeneratedPart)?.Entries.Where(e => e.Effects.Count > 0).Select(e => e.Name).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>();

    public IReadOnlyList<TestGame> Games { get; } = games ?? SuiteCatalog.All;

    public async Task<SuiteReport> RunAsync(SuiteOptions options, IProgress<SuiteGameResult>? progress, CancellationToken ct)
    {
        var runId = Guid.CreateVersion7();
        var selected = SuiteCatalog.Select(Games, options.Only);
        var sock = options.Driver == DriverKind.Socket ? socket ?? SuiteSocket.FromEnvironment() : null;
        var results = new List<SuiteGameResult>();
        var runs = new List<SuiteGameRun>();
        IAsyncDisposable? runLock = null;
        try
        {
            if (sock is not null)
            {
                var rows = new SuiteRows(sock.Db);
                runLock = await rows.HoldAsync(runId, ct);
                await rows.SweepAsync(runId, ct);
            }
            foreach (var game in selected)
            {
                var (result, run) = await PlayAsync(game, options, runId, sock, ct);
                results.Add(result);
                runs.Add(run);
                progress?.Report(result);
            }
        }
        finally
        {
            if (runLock is not null) await runLock.DisposeAsync();
            if (sock is not null && sock != socket) await sock.DisposeAsync();
        }

        var coverage = Coverage.Of(properties ?? ScenarioCompiler.CoreProperties, runs, fullRun: options.Only.Count == 0, Generated,
            results.Where(r => r.Passed).Select(r => r.Name).ToHashSet(StringComparer.Ordinal));
        return new SuiteReport(runId, results, coverage,
            results.All(r => r.Passed) && Coverage.Complete(coverage) && coverage.Where(c => c.Counts).All(c => c.Uncovered || c.Fired));
    }

    /// <summary>A fresh session for a kept game's spectator account (SuiteGameResult.Spectator).</summary>
    public async Task<MintedSession> SpectatorSessionAsync(string spectator, CancellationToken ct)
    {
        var sock = socket ?? SuiteSocket.FromEnvironment();
        try { return await sock.SessionAsync(spectator, ct); }
        finally { if (sock != socket) await sock.DisposeAsync(); }
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
        string? recordingPath = null, spectator = null;
        SocketCredentials? credentials = null;
        var minted = new List<Guid>();
        try
        {
            IGameDriver inner;
            if (sock is not null)
            {
                credentials = await sock.CredentialsAsync(compiled, minted, ct);
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
            var (judged, exact) = Judge(game, run);
            run = run with { Exact = exact };

            failures.AddRange(report.Failures);     // the scenario's expectations; a stuck or failed run's detail
            failures.AddRange(driver.Failures);
            failures.AddRange(judged);

            // After the play, so the extra participant cannot touch the game's setup or its log.
            if (sock is not null && options.Keep)
            {
                spectator = SuiteRows.SpectatorName(runId, game.Name);
                var session = await sock.SessionAsync(spectator, ct);
                minted.Add(session.AccountId);
                await new SuiteRows(sock.Db).SeedSpectatorAsync(compiled.Info.Id, session.AccountId, spectator, ct);
            }

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
            // A failed cleanup is this game's failure; the run and its report go on.
            if (sock is not null && !options.Keep)
                try { await new SuiteRows(sock.Db).DeleteGameAsync(compiled.Info.Id, minted, CancellationToken.None); }
                catch (Exception ex) { failures.Add($"cleanup: {ex.Message}"); }
        }

        run ??= new SuiteGameRun(game, new RunOutcome.Failed(failures.LastOrDefault() ?? "setup"), [],
            new EventApplier(compiled.Board, compiled.Info).Projection, []);
        var pins = game.KnownOn(options.Driver);
        var (lines, known, stale) = SuiteGameResult.Attribute(failures, pins);
        var result = new SuiteGameResult(game.Name, game.Group, failures.Count == 0, lines, scenario.Seed,
            ReplayOf(game, options.Driver), recordingPath, sock is not null && options.Keep ? compiled.Info.Id : null, spectator, known,
            pins.Count == 0 ? null : pins.Select(k => k.Id).Distinct().ToList(), stale);
        return (result, run);
    }

    /// <summary>The checks every game makes (rules suite spec → Checks 1 and 2); a game's own check of the same name is not run twice.</summary>
    private static readonly IReadOnlyList<GameCheck> Mandatory = [Checks.Ended(), Checks.Invariants(), Checks.FiredAll()];

    /// <summary>The failures, and the result of every check that names the entries it is about.</summary>
    private static (IReadOnlyList<string> Failures, IReadOnlyDictionary<string, IReadOnlyList<ExactResult>> Exact) Judge(TestGame game, SuiteGameRun run)
    {
        var failures = new List<string>();
        var exact = new Dictionary<string, List<ExactResult>>();
        foreach (var check in Mandatory.Concat(game.Checks.Where(c => Mandatory.All(m => m.Name != c.Name))))
        {
            IReadOnlyList<string> reasons;
            try { reasons = check.FailuresOf(run); }
            catch (Exception ex) { reasons = [$"threw {ex.GetType().Name}: {ex.Message}"]; }
            failures.AddRange(reasons.Select(r => $"{check.Name}: {r}"));     // one line per reason, so a pin covers only the reason it names
            foreach (var entry in (check.Entries ?? []).Distinct())
                (exact.TryGetValue(entry, out var list) ? list : exact[entry] = []).Add(new ExactResult(game.Name, check.Name, reasons.Count == 0));
        }
        return (failures, exact.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ExactResult>)kv.Value));
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

    /// <summary>Single quotes, in which a POSIX shell expands nothing; a quote inside the name is closed, escaped and reopened.</summary>
    private static string Quote(string name) =>
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.') ? name : $"'{name.Replace("'", "'\\''")}'";

    private static string FileNameOf(string name) =>
        new([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')]);
}
