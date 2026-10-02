using System.Text.Json;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;

namespace Tabletop.Sim;

public sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<RunReport> Reports)
{
    public static Summary Of(string driver, IReadOnlyList<RunReport> reports) => new(
        driver, reports.Count, reports.Count(r => r.Passed), reports.Count(r => !r.Passed),
        reports.Sum(r => r.DurationMs), reports);

    public string Table()
    {
        var lines = new List<string>
        {
            $"{Runs} runs of {string.Join(", ", Reports.Select(r => r.Scenario).Distinct())} on {Driver}: {Passed} passed, {Failed} failed, {TotalMs:N0} ms",
            "  seed  outcome      rounds  seq   frames  asks   ms",
        };
        lines.AddRange(Reports.Select(r =>
            $"  {r.Seed,-5} {r.Outcome.Kind,-12} {r.Rounds,-7} {r.LastSeq,-5} {r.Frames,-7} {r.AsksAnswered,-6} {r.DurationMs,-6:N0}"));
        lines.AddRange(Reports.Where(r => !r.Passed).SelectMany(r =>
            r.Failures.Select(f => $"  seed {r.Seed}: {f}")));
        return string.Join("\n", lines);
    }

    /// <summary>The whole outcome, detail included: serialised through the abstract RunOutcome only its kind would be written.</summary>
    private sealed record OutcomeJson(
        string Kind, Guid? WinningTeamId = null, int? Rounds = null, string? Reason = null,
        string? Detail = null, Guid? AskId = null, string? Question = null)
    {
        public static OutcomeJson Of(RunOutcome outcome) => outcome switch
        {
            RunOutcome.Decided d => new(outcome.Kind, WinningTeamId: d.WinningTeamId),
            RunOutcome.RoundLimit r => new(outcome.Kind, Rounds: r.Rounds),
            RunOutcome.Stuck s => new(outcome.Kind, Reason: s.Reason.ToString(), Detail: s.Detail),
            RunOutcome.Failed f => new(outcome.Kind, Detail: f.Detail),
            RunOutcome.Paused p => new(outcome.Kind, AskId: p.AskId, Question: p.Question),
            _ => new(outcome.Kind),
        };
    }

    private sealed record ReportJson(
        string Scenario, ulong Seed, string Driver, bool Passed, OutcomeJson Outcome, int Rounds, long LastSeq,
        int Frames, int AsksAnswered, double DurationMs, IReadOnlyList<string> Failures, string? RecordingPath);

    private sealed record SummaryJson(
        string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<ReportJson> Reports);

    public string ToJson() => JsonSerializer.Serialize(
        new SummaryJson(Driver, Runs, Passed, Failed, TotalMs, Reports.Select(r => new ReportJson(
            r.Scenario, r.Seed, r.Driver, r.Passed, OutcomeJson.Of(r.Outcome), r.Rounds, r.LastSeq, r.Frames,
            r.AsksAnswered, r.DurationMs, r.Failures, r.RecordingPath)).ToList()),
        RecordingJson.Options);
}
