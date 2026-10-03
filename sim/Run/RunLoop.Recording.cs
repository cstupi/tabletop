using Tabletop.Protocol;
using Tabletop.Sim.Recording;

namespace Tabletop.Sim.Run;

public sealed partial class RunLoop
{
    public IReadOnlyList<string> LastFrames(int n) =>
        Projector.Frames.TakeLast(n).Select(f => $"  [{f.Index}] seq {f.Seq} {f.Kind}: {f.Line}").ToList();

    /// <summary>
    /// A run that ended normally but failed an invariant or an expectation is recorded as <c>failed</c>, with the first failure
    /// as the detail (spec → What a full game means, the Failed row). Stuck and failed runs keep their own kind and detail.
    /// </summary>
    private (string Kind, string? Detail) OutcomeOf(RunReport report) => report switch
    {
        { Failures.Count: > 0, Outcome: RunOutcome.Ended or RunOutcome.RoundLimit } => ("failed", report.Failures[0]),
        { Outcome: RunOutcome.Ended { Winners.Count: > 1 } e } => (report.Outcome.Kind,
            $"tied between {string.Join(", ", e.Winners.Select(w => scenario.Info.Teams.FirstOrDefault(t => t.Id == w) is { } t ? $"Team {t.Ordinal}" : w.ToString("N")[..8]))}"),
        _ => (report.Outcome.Kind, report.Outcome switch { RunOutcome.Stuck s => s.Detail, RunOutcome.Failed f => f.Detail, _ => null }),
    };

    public Recording.Recording BuildRecording(RunReport report) => BuildRecording(report, OutcomeOf(report));

    private Recording.Recording BuildRecording(RunReport report, (string Kind, string? Detail) outcome) => Projector.Build(new RecordingMeta(
        report.Scenario, report.Seed, report.Driver, scenario.Info.GameRules, scenario.Info.PropertiesHash,
        Wire(scenario.Info.SchedulerShape), Wire(scenario.Info.Enforcement),
        scenario.Scenario.RoundCap,
        scenario.Info.Teams.Select(t => new RecordedTeam(t.Id, t.Ordinal,
            scenario.Scenario.Armies.ElementAtOrDefault(t.Ordinal - 1)?.Name ?? $"Team {t.Ordinal}",
            scenario.Info.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Name).ToList())).ToList(),
        outcome.Kind, outcome.Detail,
        report.Outcome is RunOutcome.Ended { Winners.Count: 1 } e ? e.Winners[0] : null,
        report.Rounds, report.LastSeq, DateTimeOffset.UtcNow, report.DurationMs));

    /// <summary>The Protocol's own spelling of an enum, so the meta block carries one convention and not two.</summary>
    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');
}
