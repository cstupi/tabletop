using System.Diagnostics;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Drive a game to one of the three ends spec → What a full game means lists, and fail loudly on the two
/// further things that can happen: no progress at all, and an assertion that does not hold. Paused is the
/// fifth and is not an end. Every answer comes from the answer source; nothing here reads a rule.
/// </summary>
public sealed partial class RunLoop(
    IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)
{
    private static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    private readonly RunSettings _settings = settings ?? new RunSettings();
    private readonly List<GameEvent> _log = [];
    private AskFingerprint? _lastAnswered;
    private int _sinceLastAnswer;       // non-Ask events absorbed since the last accepted answer, wherever they were drained
    private int _absorbedThisCycle;     // every event absorbed since the last step's check

    public FrameProjector Projector { get; } = new(scenario);
    public IReadOnlyList<GameEvent> Log => _log;
    public IAnswerSource Answers => answers;

    public async Task<RunReport> RunAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var failures = new List<string>();
        RunOutcome outcome;
        try
        {
            outcome = await DriveAsync(ct);
        }
        catch (UnscriptedAskException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        // A hung server or a dead socket, not a stuck game: the engine may have been making progress.
        catch (RestTimeoutException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        catch (NoLegalAnswerException ex) { outcome = new RunOutcome.Stuck(StuckReason.NoLegalAnswer, ex.Message); }

        var projection = await driver.ProjectionAsync(ct);
        // A paused run is mid-game: the expectations describe the end and would fail at every pause.
        if (outcome is not RunOutcome.Paused)
            failures.AddRange(Expectations.Check(scenario.Scenario.Expectations, projection, _log, outcome, scenario));
        // The driver's projection is the recorder's: a team-less viewer's, with sealed answers withheld. The log it
        // is replayed from is the recorder's too, so the cold replay sees the same view (ProjectionComparer compares
        // open asks by id only).
        if (_settings.CheckInvariants && outcome is not RunOutcome.Paused)   // R16: a paused run is mid-game
        {
            failures.AddRange(Invariants.Invariants.CheckAll(projection, _log, scenario)
                .Where(r => !r.Held).Select(r => $"{r.Name}: {r.Detail}"));

            if (driver is InProcessDriver inProcess)
            {
                var server = await inProcess.ServerProjectionAsync(ct);
                failures.AddRange(ProjectionComparer.Differences(server, projection)
                    .Select(d => $"the driver's projection differs from the server's — {d}"));
            }
        }
        if (outcome is RunOutcome.Stuck or RunOutcome.Failed)
            failures.Add($"{outcome.Kind}: {outcome}\nlast frames:\n{string.Join("\n", LastFrames(_settings.FrameDumpOnFailure))}");

        // RecordingPath stays null: this loop writes no file. Program.PlayAsync fills it in after
        // RecordingJson.WriteAsync, and it is the only thing that does — see RunReport's own comment.
        return new RunReport(scenario.Scenario.Name, scenario.Scenario.Seed, driver.Name, outcome,
            projection.Scheduler.Round, projection.LastSeq, Projector.Frames.Count,
            answers.History.Count, clock.Elapsed.TotalMilliseconds, failures, RecordingPath: null);
    }

    private async Task<RunOutcome> DriveAsync(CancellationToken ct)
    {
        Absorb(await driver.DrainEventsAsync(ct));
        for (var step = 0; step < _settings.MaxSteps; step++)
        {
            var result = await driver.StepAsync(ct);
            Absorb(result.Events);
            var produced = _absorbedThisCycle;
            _absorbedThisCycle = 0;

            var projection = await driver.ProjectionAsync(ct);
            if (Decided(projection) is { } winner) return new RunOutcome.Decided(winner);

            var open = result.OpenAsks.Where(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t))).ToList();
            if (open.Count == 0)
            {
                // The events an answer produced were drained when it was submitted, so the step itself may
                // return none; "nothing emitted" counts every event absorbed since the previous check.
                if (produced == 0)
                    return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"no ask is open and nothing was emitted at seq {projection.LastSeq}");
                continue;
            }

            // n whole rounds, ending on round n's boundary Ask rather than part-way through round n + 1.
            // The boundary Ask always comes: the scheduler cannot advance a round without one (invariant 5).
            if (projection.Scheduler.Round >= scenario.Scenario.RoundCap &&
                open.All(a => a.Ask.Question == AskQuestions.EndRound))
                return new RunOutcome.RoundLimit(projection.Scheduler.Round);

            if (driver.Mode == DriverMode.StepAndWait) return new RunOutcome.Paused(open[0].Ask.Id, open[0].Ask.Question);

            if (Reopened(open[0].Ask) is { } stuck) return stuck;

            if (await AnswerAsync(open[0], projection, ct) is { } refusal) return refusal;
        }
        return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"the loop ran {_settings.MaxSteps} steps without an end");
    }

    /// <summary>An unsealed Ask is answered once, by the first listed team that has not answered; a sealed Ask by every addressed team.</summary>
    private async Task<RunOutcome?> AnswerAsync(OpenAsk open, Projection projection, CancellationToken ct)
    {
        // The whole state: an answer may read anything the engine exposes. AnswerPolicy, the
        // default, reads only the geometry; a named state-reading IAnswerSource would read the rest.
        var view = new PolicyView(projection, driver.Board, volumes: null, driver.Info, scenario.Properties);
        foreach (var teamId in open.Ask.Teams.Where(t => !open.Answers.ContainsKey(t)))
        {
            var answer = answers.Answer(open.Ask, view);
            var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct);
            if (result is SubmitResult.Rejected rejected)
                return new RunOutcome.Stuck(StuckReason.SubmitRefused,
                    $"'{open.Ask.Question}' answered with {answer.GetType().Name}: {rejected.Reason} {rejected.Detail}");
            _lastAnswered = AskFingerprint.Of(open.Ask);
            _sinceLastAnswer = 0;                   // reset before the drain: Absorb counts what the answer produced
            Absorb(await driver.DrainEventsAsync(ct));

            if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
                foreach (var unitId in selected.PieceIds)
                    if (await PlaceAsync(teamId, unitId, ct) is { } refusal) return refusal;

            if (open.Ask.Sealed) continue;
            break;
        }
        return null;
    }

    private async Task<RunOutcome?> PlaceAsync(Guid teamId, Guid unitId, CancellationToken ct)
    {
        var projection = await driver.ProjectionAsync(ct);
        if (!projection.Units.ContainsKey(unitId)) return null;
        var moves = HandPlacement.IntoZone(projection, scenario, teamId, unitId, chooser);
        if (moves.Count == 0) return null;
        var result = await driver.SubmitAsync(driver.ParticipantFor(teamId), new MoveModels(moves), ct);
        Absorb(await driver.DrainEventsAsync(ct));
        return result is SubmitResult.Rejected rejected
            ? new RunOutcome.Stuck(StuckReason.SubmitRefused, $"hand placement of unit {unitId}: {rejected.Reason} {rejected.Detail}")
            : null;
    }

    /// <summary>
    /// An Ask answered and reopening unchanged, with nothing but the three Ask events between. One non-Ask
    /// event since the last answer clears the suspicion, so this catches a tight re-Ask loop and nothing
    /// slower: an engine emitting one cosmetic event per pass re-Asks forever and is caught by
    /// RunSettings.MaxSteps instead, which is the backstop and not a formality.
    /// </summary>
    private RunOutcome? Reopened(Ask ask)
    {
        if (_lastAnswered is null || _sinceLastAnswer > 0) return null;
        return AskFingerprint.Of(ask) == _lastAnswered
            ? new RunOutcome.Stuck(StuckReason.AskReopenedUnchanged, $"'{ask.Question}' reopened unchanged after being answered")
            : null;
    }

    /// <summary>One team with models in play and at least one other with none (spec → What a full game means).</summary>
    private Guid? Decided(Projection projection)
    {
        if (projection.Scheduler.Round < 1) return null;                       // deployment: the shelf is not a wipe
        var standing = scenario.TeamIds.Where(t => Expectations.ModelsOnTable(projection, scenario, t) > 0).ToList();
        return standing.Count == 1 && scenario.TeamIds.Count > 1 ? standing[0] : null;
    }

    private void Absorb(IReadOnlyList<GameEvent> events)
    {
        foreach (var e in events)
        {
            _log.Add(e);
            if (_settings.Record) Projector.Add(e);
            _absorbedThisCycle++;
            if (e.Payload is not (AskOpened or AskAnswered or AskClosed)) _sinceLastAnswer++;
        }
    }
}
