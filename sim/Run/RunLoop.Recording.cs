using Tabletop.Protocol;
using Tabletop.Sim.Recording;

namespace Tabletop.Sim.Run;

public sealed partial class RunLoop
{
    public IReadOnlyList<string> LastFrames(int n) =>
        Projector.Frames.TakeLast(n).Select(f => $"  [{f.Index}] seq {f.Seq} {f.Kind}: {f.Line}").ToList();

    public Recording.Recording BuildRecording(RunReport report) => Projector.Build(new RecordingMeta(
        report.Scenario, report.Seed, report.Driver, scenario.Info.GameRules, scenario.Info.PropertiesHash,
        Wire(scenario.Info.SchedulerShape), Wire(scenario.Info.Enforcement),
        scenario.Scenario.RoundCap,
        scenario.Info.Teams.Select(t => new RecordedTeam(t.Id, t.Ordinal,
            scenario.Scenario.Armies.ElementAtOrDefault(t.Ordinal - 1)?.Name ?? $"Team {t.Ordinal}",
            scenario.Info.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Name).ToList())).ToList(),
        report.Outcome.Kind,
        report.Outcome switch { RunOutcome.Stuck s => s.Detail, RunOutcome.Failed f => f.Detail, _ => null },
        report.Outcome is RunOutcome.Decided d ? d.WinningTeamId : null,
        report.Rounds, report.LastSeq, DateTimeOffset.UtcNow, report.DurationMs));

    /// <summary>The Protocol's own spelling of an enum, so the meta block carries one convention and not two.</summary>
    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');
}
