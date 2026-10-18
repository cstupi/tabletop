using System.Diagnostics;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Drive a game to one of the three ends spec → What a full game means lists (ended, round-capped, stuck), and fail loudly on the two
/// further things that can happen: no progress at all, and an assertion that does not hold. Paused is the
/// fifth and is not an end. Every answer comes from the answer source; nothing here reads a rule.
/// </summary>
public sealed partial class RunLoop(
    IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)
{
    private static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement];

    private readonly RunSettings _settings = settings ?? new RunSettings();
    private readonly List<GameEvent> _log = [];
    private AskFingerprint? _lastAnswered;
    private int _sinceLastAnswer;       // non-Ask events absorbed since the last accepted answer, wherever they were drained
    private RunOutcome? _forced;     // an outcome decided outside DriveAsync, reported by the next RunAsync
    private int _absorbedThisCycle;     // every event absorbed since the last step's check
    private bool _setAsideSent;         // the scenario's set-aside units, sent once before the first answer

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
            outcome = _forced ?? await DriveAsync(ct);
            _forced = null;
        }
        catch (UnscriptedAskException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        // A hung server or a dead socket, not a stuck game: the engine may have been making progress.
        catch (RestTimeoutException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        catch (DriverFaultException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        catch (NoLegalAnswerException ex) { outcome = new RunOutcome.Stuck(StuckReason.NoLegalAnswer, ex.Message); }
        // Anything else (a policy or projection fault, a refused submit shape) is this seed's failure, with the frame dump below.
        catch (Exception ex) when (ex is not OperationCanceledException) { outcome = new RunOutcome.Failed(ex.ToString()); }

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

    /// <summary>Continue after a pause, under whatever Mode the driver is in now.</summary>
    public Task<RunReport> ResumeAsync(CancellationToken ct) => RunAsync(ct);

    /// <summary>
    /// Answer the Ask the run paused on, from outside the answer source, then continue under the current
    /// Mode, so in StepAndWait this advances exactly one decision. The seam the spec leaves for answering an
    /// Ask from a debugger; nothing here is a control channel.
    /// </summary>
    public async Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer answer, CancellationToken ct)
    {
        var open = (await driver.OpenAsksAsync(ct)).FirstOrDefault(a => a.Ask.Teams.Contains(teamId) && !a.Answers.ContainsKey(teamId))
            ?? throw new InvalidOperationException($"no ask is open for team {teamId:N}");
        var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct, Seat(open.Ask, teamId, answer, await driver.ProjectionAsync(ct)));
        if (result is SubmitResult.Rejected rejected)
            throw new InvalidOperationException($"'{open.Ask.Question}' refused: {rejected.Reason} {rejected.Detail}");
        answers.Record(open.Ask, answer);
        Absorb(await driver.DrainEventsAsync(ct));

        // A refused placement is the loop's own Stuck(SubmitRefused): the next RunAsync reports it instead of driving.
        if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
            foreach (var unitId in selected.PieceIds)
                if (await PlaceAsync(teamId, unitId, ct) is { } refusal) { _forced = refusal; break; }

        return await RunAsync(ct);
    }

    private async Task<RunOutcome> DriveAsync(CancellationToken ct)
    {
        _lastAnswered = null;       // a pause between an answer and its reopening is not a hang
        Absorb(await driver.DrainEventsAsync(ct));
        if (!_setAsideSent)
        {
            _setAsideSent = true;
            if (await SetAsideAsync(ct) is { } refused) return refused;
            if (await PassengersAsync(ct) is { } refusedInside) return refusedInside;
        }
        for (var step = 0; step < _settings.MaxSteps; step++)
        {
            var result = await driver.StepAsync(ct);
            Absorb(result.Events);
            var produced = _absorbedThisCycle;
            _absorbedThisCycle = 0;

            var projection = await driver.ProjectionAsync(ct);
            if (projection.Ended is { } end) return new RunOutcome.Ended(projection.Scheduler.Round, end.Winners);

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

    /// <summary>An unsealed Ask is answered once, by the first listed team that has not answered; a sealed Ask by every addressed team while it stays open.</summary>
    private async Task<RunOutcome?> AnswerAsync(OpenAsk open, Projection projection, CancellationToken ct)
    {
        // The whole state: an answer may read anything the engine exposes. AnswerPolicy, the
        // default, reads only the geometry; a named state-reading IAnswerSource would read the rest.
        var view = new PolicyView(projection, driver.Board, volumes: null, driver.Info, scenario.Properties);
        if (await SetAsideAtLastAskAsync(open.Ask, ct) is { } setAside) return setAside.Refused;
        foreach (var teamId in open.Ask.Teams.Where(t => !open.Answers.ContainsKey(t)))
        {
            var answer = answers.Answer(open.Ask, view);
            var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct, Seat(open.Ask, teamId, answer, projection));
            if (result is SubmitResult.Rejected rejected)
                return new RunOutcome.Stuck(StuckReason.SubmitRefused,
                    $"'{open.Ask.Question}' answered with {answer.GetType().Name}: {rejected.Reason} {rejected.Detail}");
            _lastAnswered = AskFingerprint.Of(open.Ask);
            _sinceLastAnswer = 0;                   // reset before the drain: Absorb counts what the answer produced
            Absorb(await driver.DrainEventsAsync(ct));

            if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
                foreach (var unitId in selected.PieceIds)
                    if (await PlaceAsync(teamId, unitId, ct) is { } refusal) return refusal;

            if (open.Ask.Sealed && (await driver.ProjectionAsync(ct)).OpenAsks.ContainsKey(open.Ask.Id)) continue;   // a sealed Ask the server closed early (game_over's End) takes no more answers
            break;
        }
        return null;
    }

    /// <summary>
    /// The scenario's set-aside units get the condition a player sets from the menu, before the first answer: the roll-off's confirm gate is
    /// what is open then, so the first deploy Ask has not opened and already leaves them out (a deploy Ask open before the change would be
    /// asked again). Sent by the owning participant.
    /// </summary>
    private async Task<RunOutcome?> SetAsideAsync(CancellationToken ct)
    {
        for (var i = 0; i < scenario.Scenario.Armies.Count; i++)
        {
            var army = driver.Board.Armies[i];
            foreach (var ordinal in scenario.Scenario.Armies[i].SetAside)
            {
                var unit = army.Snapshot.Units.Single(u => u.Ordinal == ordinal);
                var result = await driver.SubmitAsync(army.ParticipantId,
                    new SetCondition(new Subject(SubjectKind.Unit, unit.Id), scenario.SetAsideCondition, true), ct);
                Absorb(await driver.DrainEventsAsync(ct));
                if (result is SubmitResult.Rejected rejected)
                    return new RunOutcome.Stuck(StuckReason.SubmitRefused, $"setting aside {unit.Name}: {rejected.Reason} {rejected.Detail}");
            }
            foreach (var (ordinal, condition) in scenario.Scenario.Armies[i].Conditions)
            {
                var unit = army.Snapshot.Units.Single(u => u.Ordinal == ordinal);
                var result = await driver.SubmitAsync(army.ParticipantId, new SetCondition(new Subject(SubjectKind.Unit, unit.Id), condition, true), ct);
                Absorb(await driver.DrainEventsAsync(ct));
                if (result is SubmitResult.Rejected rejected)
                    return new RunOutcome.Stuck(StuckReason.SubmitRefused, $"setting {condition} on {unit.Name}: {rejected.Reason} {rejected.Detail}");
            }
        }
        return null;
    }

    /// <summary>
    /// A deploy Ask offering one unit the scenario sets aside there (ScenarioArmy.SetAsideAtLastAsk): the owning participant sets it aside from the
    /// menu instead of answering, and the server asks again or moves on. Null when the Ask is to be answered as usual.
    /// </summary>
    private async Task<(RunOutcome? Refused, bool Sent)?> SetAsideAtLastAskAsync(Ask ask, CancellationToken ct)
    {
        if (ask.Question != AskQuestions.Deploy || ask.Legal is not PieceSelection { Eligible: [var only] }) return null;
        for (var i = 0; i < scenario.Scenario.Armies.Count; i++)
        {
            var army = driver.Board.Armies[i];
            if (army.Snapshot.Units.FirstOrDefault(u => u.Id == only) is not { } unit || !scenario.Scenario.Armies[i].SetAsideAtLastAsk.Contains(unit.Ordinal)) continue;
            var result = await driver.SubmitAsync(army.ParticipantId, new SetCondition(new Subject(SubjectKind.Unit, unit.Id), scenario.SetAsideCondition, true), ct);
            Absorb(await driver.DrainEventsAsync(ct));
            return (result is SubmitResult.Rejected rejected
                ? new RunOutcome.Stuck(StuckReason.SubmitRefused, $"setting aside {unit.Name} at its deploy Ask: {rejected.Reason} {rejected.Detail}") : null, true);
        }
        return null;
    }

    /// <summary>
    /// The scenario's passengers go in their Transports before the first answer (passengers spec): one MoveModels per unit, every model from the
    /// shelf into the Transport's first model, sent by the owning participant as the roster's Put in transport does. No Ask is answered.
    /// </summary>
    private async Task<RunOutcome?> PassengersAsync(CancellationToken ct)
    {
        for (var i = 0; i < scenario.Scenario.Armies.Count; i++)
        {
            var army = driver.Board.Armies[i];
            foreach (var (ordinal, transport) in scenario.Scenario.Armies[i].Passengers)
            {
                var unit = army.Snapshot.Units.Single(u => u.Ordinal == ordinal);
                var hull = army.Snapshot.Units.Single(u => u.Ordinal == transport).Models[0].Id;
                var result = await driver.SubmitAsync(army.ParticipantId,
                    new MoveModels(unit.Models.Select(m => new ModelRelocation(m.Id, new OnShelf(), new Inside(hull))).ToList()), ct);
                Absorb(await driver.DrainEventsAsync(ct));
                if (result is SubmitResult.Rejected rejected)
                    return new RunOutcome.Stuck(StuckReason.SubmitRefused, $"putting {unit.Name} in its transport: {rejected.Reason} {rejected.Detail}");
            }
        }
        return null;
    }

    private async Task<RunOutcome?> PlaceAsync(Guid teamId, Guid unitId, CancellationToken ct)
    {
        var projection = await driver.ProjectionAsync(ct);
        if (!projection.Units.ContainsKey(unitId)) return null;
        var moves = HandPlacement.IntoZone(projection, scenario, teamId, unitId, chooser);
        if (moves.Count == 0) return null;
        var result = await driver.SubmitAsync(projection.Units[unitId].ParticipantId, new MoveModels(moves), ct);   // the unit's own seat places it (p.50)
        Absorb(await driver.DrainEventsAsync(ct));
        return result is SubmitResult.Rejected rejected
            ? new RunOutcome.Stuck(StuckReason.SubmitRefused, $"hand placement of unit {unitId}: {rejected.Reason} {rejected.Detail}")
            : null;
    }

    /// <summary>
    /// Who of a team answers (rulebook p.50 Team Games, game-flow spec → Participants): the Ask's Seat; for an own-units pick, the owner of the unit
    /// the answer picks; else the team's first seat. Nothing here judges the answer: the server refuses a wrong seat.
    /// </summary>
    private Guid? Seat(Ask ask, Guid teamId, AskAnswer answer, Projection projection)
    {
        if (ask.Seat is { } seat && scenario.TeamOfParticipant.GetValueOrDefault(seat) == teamId) return seat;
        if (!ask.OwnUnitsOnly) return null;
        IEnumerable<Guid> pieces = answer switch
        {
            Chosen c when ask.Legal is Options o && o.Choices.FirstOrDefault(x => x.Id == c.OptionId) is { } option => option.Pieces,
            SelectedPieces s => s.PieceIds,
            _ => [],
        };
        return pieces.Select(id => projection.Models.TryGetValue(id, out var m) ? m.UnitId : id)
            .Select(id => projection.Units.TryGetValue(id, out var u) ? u.ParticipantId : (Guid?)null)
            .FirstOrDefault(p => p is { } owner && scenario.TeamOfParticipant.GetValueOrDefault(owner) == teamId);
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
