using Tabletop.Protocol;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Suite;

/// <summary>
/// A driver that applies a game's DiceFixes as their rolls arrive: each event the run loop takes passes through here, and a roll a
/// fix names is set by SetDice (taken only by a server with GameServerOptions.AcceptsSetDice on) while its frame is still on the stack,
/// before the loop answers the step gate that holds it there. The edit's events join the batch the loop receives. Nothing here
/// reads a rule: a fix names a roll kind and an ordinal, and the faces are the game's literals.
/// </summary>
public sealed class DiceFixingDriver(IGameDriver inner, IReadOnlyList<DiceFix> fixes) : IGameDriver
{
    private readonly Dictionary<string, int> _seen = [];
    private readonly HashSet<DiceFix> _met = [];
    private readonly List<string> _failures = [];

    public IGameDriver Inner => inner;

    /// <summary>Every fix that failed: its roll never came, its frame had closed, its faces outnumber the dice, or the server refused the edit.</summary>
    public IReadOnlyList<string> Failures =>
        [.. _failures, .. fixes.Where(f => !_met.Contains(f)).Select(f => $"{Label(f)}: the game never made that roll")];

    public string Name => inner.Name;
    public Guid GameId => inner.GameId;
    public GameInfo Info => inner.Info;
    public Board Board => inner.Board;
    public IReadOnlyList<Guid> TeamIds => inner.TeamIds;
    public Guid ParticipantFor(Guid teamId) => inner.ParticipantFor(teamId);
    public DriverMode Mode { get => inner.Mode; set => inner.Mode = value; }

    public Task StartAsync(CancellationToken ct) => inner.StartAsync(ct);
    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) => inner.OpenAsksAsync(ct);
    public Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct) => inner.SubmitAsync(participantId, command, ct);
    public Task<Projection> ProjectionAsync(CancellationToken ct) => inner.ProjectionAsync(ct);

    public async Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct) => await FixAsync(await inner.DrainEventsAsync(ct), ct);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        var step = await inner.StepAsync(ct);
        var events = await FixAsync(step.Events, ct);
        return events.Count == step.Events.Count ? step : new StepResult(events, await inner.OpenAsksAsync(ct), Quiescent: false);
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private async Task<IReadOnlyList<GameEvent>> FixAsync(IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        if (fixes.Count == 0) return events;
        var all = events.ToList();
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Payload is not StepCompleted step) continue;
            foreach (var roll in step.Rolls)
            {
                var ordinal = _seen.GetValueOrDefault(roll.Roll);
                _seen[roll.Roll] = ordinal + 1;
                var fix = fixes.FirstOrDefault(f => f.Roll == roll.Roll && f.Ordinal == ordinal && !_met.Contains(f));
                if (fix is null) continue;
                _met.Add(fix);
                if (await RefusalAsync(fix, step, roll, ct) is { } failure) { _failures.Add($"{Label(fix)}: {failure}"); continue; }
                all.AddRange(await inner.DrainEventsAsync(ct));
            }
        }
        return all;
    }

    private async Task<string?> RefusalAsync(DiceFix fix, StepCompleted step, RollRecord roll, CancellationToken ct)
    {
        if (fix.Faces.Count > roll.Dice.Count) return $"{fix.Faces.Count} faces for a roll of {roll.Dice.Count} dice (step {step.Step})";
        if ((await inner.ProjectionAsync(ct)).Stack.All(f => f.Id != step.FrameId))
            return $"step {step.Step} finished its frame, so SetDice cannot reach the roll";
        var result = await inner.SubmitAsync(inner.ParticipantFor(inner.TeamIds[0]),
            new SetDice(step.FrameId, roll.Id, fix.Faces.Select((face, i) => new DieValue(i, face)).ToList()), ct);
        return result is SubmitResult.Rejected r ? $"SetDice refused: {r.Reason} {r.Detail}" : null;
    }

    private static string Label(DiceFix fix) => $"dice fix {fix.Roll}#{fix.Ordinal} [{string.Join(",", fix.Faces)}]";
}
