using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Suite;

/// <summary>
/// One game's verdict. Replay is the command line that reruns that game alone with --keep. GameId is set for a kept socket game, the one a
/// live feed can open; Spectator names the account (a participant with no team) that feed connects as. Pinned: the defect ids the game is
/// pinned to (TestGame.Known). Known: set when the game failed and every failure is a pinned defect's, prefixed "known defect &lt;id&gt;: ";
/// null when it passed or a failure is unexpected. Stale: each pin no failure matched, as "&lt;id&gt;: &lt;check&gt;": its defect no longer shows there.
/// </summary>
public sealed record SuiteGameResult(string Name, string Group, bool Passed, IReadOnlyList<string> Failures, ulong Seed,
    string Replay, string? RecordingPath, Guid? GameId, string? Spectator = null, IReadOnlyList<string>? Known = null,
    IReadOnlyList<string>? Pinned = null, IReadOnlyList<string>? Stale = null)
{
    /// <summary>The failures with each pinned defect's lines marked, the defect ids when every failure is pinned, and the pins nothing matched.</summary>
    public static (IReadOnlyList<string> Failures, IReadOnlyList<string>? Known, IReadOnlyList<string>? Stale) Attribute(
        IReadOnlyList<string> failures, IReadOnlyList<KnownDefect> pins)
    {
        var marked = failures.Select(f => (Line: f, Pin: pins.FirstOrDefault(p => p.Matches(f)))).ToList();
        var lines = marked.Select(m => m.Pin is null ? m.Line : $"known defect {m.Pin.Id}: {m.Line}").ToList();
        var known = marked.Count > 0 && marked.All(m => m.Pin is not null) ? marked.Select(m => m.Pin!.Id).Distinct().ToList() : null;
        var stale = pins.Where(p => !failures.Any(p.Matches)).Select(p => $"{p.Id}: {p.Check}").Distinct().ToList();
        return (lines, known, stale.Count == 0 ? null : stale);
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
            lines.Add($"  {verdict}  {g.Name}  [{g.Group}]  seed {g.Seed}");
            foreach (var pin in g.Stale ?? [])
            {
                var cut = pin.IndexOf(": ", StringComparison.Ordinal);
                lines.Add($"          (pinned {pin[..cut]} did not fail: {pin[(cut + 2)..]})");
            }
            lines.AddRange(g.Failures.Select(f => $"          {f.Split('\n')[0]}"));
            if (!g.Passed) lines.Add($"          replay: {g.Replay}");
        }

        var counted = Coverage.Where(c => c.Counts).ToList();
        var uncovered = counted.Count(c => c.Uncovered);
        var silent = counted.Count(c => !c.Uncovered && !c.Fired);
        var byPattern = counted.Count(c => c.State == CoverageState.Pattern && !c.Uncovered);
        var textOnly = Coverage.Where(c => !c.Counts).Select(c => c.Name).ToList();
        lines.Add($"coverage ({counted.Count - uncovered}/{counted.Count} covered, {byPattern} by pattern, {silent} covered but not fired; {textOnly.Count} not automated)");
        foreach (var c in counted)
        {
            var state = c.Uncovered ? "UNCOVERED"
                : c.State == CoverageState.Pattern ? c.Fired ? "covered by pattern" : "PATTERN NOT PASSED"
                : c.Fired ? "fired" : "NOT FIRED";
            var exact = c.Exact.Count == 0 ? "" : $"  exact {c.Exact.Count(x => x.Passed)}/{c.Exact.Count}"
                + string.Concat(c.Exact.Where(x => !x.Passed).Select(x => $"  FAIL {x.Game}: {x.Check}"));
            lines.Add($"  {c.Kind,-5}  {c.Name}  {state}{(c.Uncovered ? "" : $"  ({string.Join(", ", c.Games)})")}{exact}");
        }
        if (textOnly.Count > 0) lines.Add($"not automated ({textOnly.Count}): {string.Join(", ", textOnly)}");

        lines.Add($"{(Passed ? "PASSED" : "FAILED")}: {Games.Count(g => g.Passed)} of {Games.Count} games passed, {known} failing on known defects, {unexpected} unexpected; "
            + $"{counted.Count - uncovered} of {counted.Count} entries covered, {textOnly.Count} not automated; run {RunId:N}");
        return string.Join("\n", lines);
    }
}
