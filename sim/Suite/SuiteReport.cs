using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Suite;

/// <summary>
/// One game's verdict. Replay is the command line that reruns that game alone with --keep. GameId is set for a kept socket game, the one a
/// live feed can open; Spectator names the account (a participant with no team) that feed connects as. Pinned: the defect ids the game is
/// pinned to (TestGame.Known). Known: set when the game failed and every failure is a pinned defect's, prefixed "known defect &lt;id&gt;: ";
/// null when it passed or a failure is unexpected.
/// </summary>
public sealed record SuiteGameResult(string Name, string Group, bool Passed, IReadOnlyList<string> Failures, ulong Seed,
    string Replay, string? RecordingPath, Guid? GameId, string? Spectator = null, IReadOnlyList<string>? Known = null,
    IReadOnlyList<string>? Pinned = null)
{
    /// <summary>The failures with each pinned defect's lines marked, and the defect ids when every failure is pinned.</summary>
    public static (IReadOnlyList<string> Failures, IReadOnlyList<string>? Known) Attribute(IReadOnlyList<string> failures, IReadOnlyList<KnownDefect> pins)
    {
        var marked = failures.Select(f => (Line: f, Pin: pins.FirstOrDefault(p => f.StartsWith(p.Check, StringComparison.Ordinal)))).ToList();
        var lines = marked.Select(m => m.Pin is null ? m.Line : $"known defect {m.Pin.Id}: {m.Line}").ToList();
        var known = marked.Count > 0 && marked.All(m => m.Pin is not null) ? marked.Select(m => m.Pin!.Id).Distinct().ToList() : null;
        return (lines, known);
    }
}

/// <summary>A run's games and coverage. Passed: every game passed and, on a full run, no entry is uncovered.</summary>
public sealed record SuiteReport(Guid RunId, IReadOnlyList<SuiteGameResult> Games, IReadOnlyList<CoverageEntry> Coverage, bool Passed)
{
    public const string FileName = "suite.json";

    public int ExitCode => Passed ? 0 : 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public string Table()
    {
        var known = Games.Count(g => g.Known is not null);
        var unexpected = Games.Count(g => !g.Passed && g.Known is null);
        var lines = new List<string> { $"games ({Games.Count(g => g.Passed)}/{Games.Count} passed, {known} failing on known defects, {unexpected} unexpected)" };
        foreach (var g in Games)
        {
            var verdict = g.Passed ? "pass" : g.Known is { } ids ? $"KNOWN {string.Join(",", ids)}" : "FAIL";
            var stale = g.Passed && g.Pinned is { Count: > 0 } pinned ? $"  (pinned {string.Join(",", pinned)} did not fail)" : "";
            lines.Add($"  {verdict}  {g.Name}  [{g.Group}]  seed {g.Seed}{stale}");
            lines.AddRange(g.Failures.Select(f => $"          {f.Split('\n')[0]}"));
            if (!g.Passed) lines.Add($"          replay: {g.Replay}");
        }

        var uncovered = Coverage.Count(c => c.Uncovered);
        var silent = Coverage.Count(c => !c.Uncovered && !c.Fired);
        lines.Add($"coverage ({Coverage.Count - uncovered}/{Coverage.Count} covered, {silent} covered but not fired)");
        foreach (var c in Coverage)
        {
            var state = c.Uncovered ? "UNCOVERED" : c.Fired ? "fired" : "NOT FIRED";
            var exact = c.Exact.Count == 0 ? "" : $"  exact {c.Exact.Count(x => x.Passed)}/{c.Exact.Count}"
                + string.Concat(c.Exact.Where(x => !x.Passed).Select(x => $"  FAIL {x.Game}: {x.Check}"));
            lines.Add($"  {c.Kind,-5}  {c.Name}  {state}{(c.Uncovered ? "" : $"  ({string.Join(", ", c.Games)})")}{exact}");
        }

        lines.Add($"{(Passed ? "PASSED" : "FAILED")}: {Games.Count(g => g.Passed)} of {Games.Count} games passed, {known} failing on known defects, {unexpected} unexpected; "
            + $"{Coverage.Count - uncovered} of {Coverage.Count} entries covered; run {RunId:N}");
        return string.Join("\n", lines);
    }
}
