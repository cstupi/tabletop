using Tabletop.Content;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Suite;

/// <summary>
/// One game's verdict. Replay is the command line that reruns that game alone with --keep. GameId is set for a kept socket game, the one a
/// live feed can open; Spectator names the account (a participant with no team) that feed connects as. Pinned: the defect ids the game is
/// pinned to (TestGame.Known). Known: set when the game failed and every failure is a pinned defect's, prefixed "known defect &lt;id&gt;: ";
/// null when it passed or a failure is unexpected. Stale: each pin no failure matched, as "&lt;id&gt;: &lt;check&gt;": its defect no longer shows there.
/// DurationSeconds: the game's wall time, setup and cleanup included.
/// </summary>
public sealed record SuiteGameResult(string Name, string Group, bool Passed, IReadOnlyList<string> Failures, ulong Seed,
    string Replay, string? RecordingPath, Guid? GameId, string? Spectator = null, IReadOnlyList<string>? Known = null,
    IReadOnlyList<string>? Pinned = null, IReadOnlyList<string>? Stale = null, double? DurationSeconds = null)
{
    /// <summary>The game-rules id the game ran under (TestGame.Edition).</summary>
    public string Edition { get; init; } = RuleSets.Gf351;

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

    /// <summary>pass, FAIL, or KNOWN with the defect ids.</summary>
    [JsonIgnore]
    public string Verdict => Passed ? "pass" : Known is { } ids ? $"KNOWN {string.Join(",", ids)}" : "FAIL";

    /// <summary>Each pin no failure matched, as "(pinned &lt;id&gt; did not fail: &lt;check&gt;)".</summary>
    [JsonIgnore]
    public IEnumerable<string> StaleLines => (Stale ?? []).Select(pin =>
    {
        var cut = pin.IndexOf(": ", StringComparison.Ordinal);
        return $"(pinned {pin[..cut]} did not fail: {pin[(cut + 2)..]})";
    });
}

/// <summary>A run's games and coverage. Passed: every game passed and, on a full run, no entry is uncovered.</summary>
public sealed record SuiteReport(Guid RunId, IReadOnlyList<SuiteGameResult> Games, IReadOnlyList<CoverageEntry> Coverage, bool Passed)
{
    public int ExitCode => Passed ? 0 : 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The whole report as text: the games, every coverage entry, the verdict.</summary>
    public string Table() => string.Join("\n", GameLines().Concat(CoverageLines(problemsOnly: false)).Append(Verdict));

    int KnownCount => Games.Count(g => g.Known is not null);
    int UnexpectedCount => Games.Count(g => !g.Passed && g.Known is null);

    /// <summary>A line per game, its stale pins, the first line of each failure and a failing game's replay.</summary>
    public IEnumerable<string> GameLines()
    {
        yield return $"games ({Games.Count(g => g.Passed)}/{Games.Count} passed, {KnownCount} failing on known defects, {UnexpectedCount} unexpected)";
        foreach (var g in Games)
        {
            yield return $"  {g.Verdict}  {g.Name}  [{g.Group}]  {SuiteCatalog.EditionLabel(g.Edition)}  seed {g.Seed}";
            foreach (var pin in g.StaleLines) yield return $"          {pin}";
            foreach (var f in g.Failures) yield return $"          {f.Split('\n')[0]}";
            if (!g.Passed) yield return $"          replay: {g.Replay}";
        }
    }

    /// <summary>
    /// Per shipped set the run lists entries of: its coverage totals, then every counted entry and the text-only ones, each line naming
    /// the set's edition; with problemsOnly, only the entries that fail the run (uncovered, not fired, a pattern not passed) or an exact check.
    /// </summary>
    public IEnumerable<string> CoverageLines(bool problemsOnly)
    {
        foreach (var set in Coverage.GroupBy(c => c.Edition))
        {
            var edition = SuiteCatalog.EditionLabel(set.Key);
            var counted = set.Where(c => c.Counts).ToList();
            var uncovered = counted.Count(c => c.Uncovered);
            var silent = counted.Count(c => !c.Uncovered && !c.Fired);
            var byPattern = counted.Count(c => c.State == CoverageState.Pattern && !c.Uncovered);
            var textOnly = set.Where(c => !c.Counts).Select(c => c.Name).ToList();
            yield return $"coverage {edition} ({counted.Count - uncovered}/{counted.Count} covered, {byPattern} by pattern, {silent} covered but not fired; {textOnly.Count} not automated)";
            foreach (var c in counted.Where(c => !problemsOnly || c.Uncovered || !c.Fired || c.Exact.Any(x => !x.Passed)))
            {
                var state = c.Uncovered ? "UNCOVERED"
                    : c.State == CoverageState.Pattern ? c.Fired ? "covered by pattern" : "PATTERN NOT PASSED"
                    : c.Fired ? "fired" : "NOT FIRED";
                var exact = c.Exact.Count == 0 ? "" : $"  exact {c.Exact.Count(x => x.Passed)}/{c.Exact.Count}"
                    + string.Concat(c.Exact.Where(x => !x.Passed).Select(x => $"  FAIL {x.Game}: {x.Check}"));
                yield return $"  {edition}  {c.Kind,-5}  {c.Name}  {state}{(c.Uncovered ? "" : $"  ({string.Join(", ", c.Games)})")}{exact}";
            }
            if (textOnly.Count > 0 && !problemsOnly) yield return $"not automated ({textOnly.Count}): {string.Join(", ", textOnly)}";
        }
    }

    /// <summary>PASSED or FAILED, the game counts, the coverage counts and the run id, on one line.</summary>
    [JsonIgnore]
    public string Verdict
    {
        get
        {
            var counted = Coverage.Where(c => c.Counts).ToList();
            var uncovered = counted.Count(c => c.Uncovered);
            return $"{(Passed ? "PASSED" : "FAILED")}: {Games.Count(g => g.Passed)} of {Games.Count} games passed, {KnownCount} failing on known defects, "
                + $"{UnexpectedCount} unexpected; {counted.Count - uncovered} of {counted.Count} entries covered, {Coverage.Count(c => !c.Counts)} not automated; run {RunId:N}";
        }
    }
}
