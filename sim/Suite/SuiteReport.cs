using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Suite;

/// <summary>One game's verdict. Replay is the command line that reruns that game alone with --keep. GameId is set for a kept socket game, the one a live feed can open; Spectator names the account (a participant with no team) that feed connects as.</summary>
public sealed record SuiteGameResult(string Name, string Group, bool Passed, IReadOnlyList<string> Failures, ulong Seed,
    string Replay, string? RecordingPath, Guid? GameId, string? Spectator = null);

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
        var lines = new List<string> { $"games ({Games.Count(g => g.Passed)}/{Games.Count} passed)" };
        foreach (var g in Games)
        {
            lines.Add($"  {(g.Passed ? "pass" : "FAIL")}  {g.Name}  [{g.Group}]  seed {g.Seed}");
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

        lines.Add($"{(Passed ? "PASSED" : "FAILED")}: {Games.Count(g => g.Passed)} of {Games.Count} games passed; "
            + $"{Coverage.Count - uncovered} of {Coverage.Count} entries covered; run {RunId:N}");
        return string.Join("\n", lines);
    }
}
