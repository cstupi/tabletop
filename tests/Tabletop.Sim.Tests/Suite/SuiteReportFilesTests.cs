using System.Text.Json;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The suite command's report files and console: every run writes suite-&lt;run id&gt;.json and .md, and the latest copies.</summary>
public class SuiteReportFilesTests
{
    static async Task<T> InDirAsync<T>(Func<string, Task<T>> body)
    {
        var directory = TempDir();
        try { return await body(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    static string Only(string directory) => Assert.Single(Directory.GetFiles(directory, "suite-*.json"), p => !p.EndsWith(SuiteReportFiles.Latest + ".json"));

    [Fact]
    public Task A_passing_run_writes_the_json_and_markdown_reports_and_the_latest_copies() => InDirAsync(async directory =>
    {
        var output = new StringWriter();
        var code = await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Name, "--out", directory]), output, new SuiteRunner([Game()]), Ct);
        Assert.Equal(0, code);

        var jsonPath = Only(directory);
        var runId = Path.GetFileNameWithoutExtension(jsonPath)["suite-".Length..];
        Assert.True(Guid.TryParseExact(runId, "N", out _), runId);
        var mdPath = Path.ChangeExtension(jsonPath, ".md");
        Assert.True(File.Exists(mdPath));
        Assert.Equal(await File.ReadAllTextAsync(jsonPath), await File.ReadAllTextAsync(Path.Combine(directory, "suite-latest.json")));
        Assert.Equal(await File.ReadAllTextAsync(mdPath), await File.ReadAllTextAsync(Path.Combine(directory, "suite-latest.md")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = json.RootElement;
        Assert.Equal(runId, root.GetProperty("runId").GetGuid().ToString("N"));
        Assert.Equal("in-process", root.GetProperty("driver").GetString());
        Assert.Equal("passed", root.GetProperty("verdict").GetString());
        Assert.StartsWith("PASSED: 1 of 1 games passed", root.GetProperty("summary").GetString());
        Assert.Equal([Name], root.GetProperty("only").EnumerateArray().Select(e => e.GetString()));
        Assert.True(root.GetProperty("endedAt").GetDateTimeOffset() >= root.GetProperty("startedAt").GetDateTimeOffset());
        var game = Assert.Single(root.GetProperty("games").EnumerateArray());
        Assert.Equal(Name, game.GetProperty("name").GetString());
        Assert.True(game.GetProperty("passed").GetBoolean());
        Assert.Equal(3UL, game.GetProperty("seed").GetUInt64());
        Assert.Equal(SuiteRunner.ReplayOf(Game(), DriverKind.InProcess), game.GetProperty("replay").GetString());
        Assert.True(game.GetProperty("durationSeconds").GetDouble() > 0);
        Assert.Equal("grimdark-future", game.GetProperty("edition").GetString());
        Assert.Equal("AP", root.GetProperty("coverage")[0].GetProperty("name").GetString());
        Assert.Equal("grimdark-future", root.GetProperty("coverage")[0].GetProperty("edition").GetString());

        var md = await File.ReadAllTextAsync(mdPath);
        Assert.Contains("PASSED: 1 of 1 games passed", md.Split('\n').Take(4).Aggregate((a, b) => a + "\n" + b));
        Assert.Contains("## Failures (0)", md);
        Assert.Contains("## Coverage", md);
        Assert.Contains("AP  fired", md);
        Assert.Contains("| Verdict | Game | Edition | Group | Seed | Seconds |", md);
        Assert.Contains($"| pass | {Name} | 3.5 | {Group} | 3 |", md);
        Assert.Contains("coverage 3.5 (1/1 covered", md);
        Assert.Contains("  3.5  rule   AP  fired", md);

        // The console: no coverage table, the verdict and the report's paths.
        var console = output.ToString();
        Assert.DoesNotContain("AP  fired", console);
        Assert.Contains("PASSED: 1 of 1 games passed", console);
        Assert.Contains(mdPath, console);
        Assert.Contains(jsonPath, console);
        return 0;
    });

    [Fact]
    public Task Verbose_prints_the_coverage_table_as_well() => InDirAsync(async directory =>
    {
        var output = new StringWriter();
        await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Name, "--out", directory, "--verbose"]), output, new SuiteRunner([Game()]), Ct);
        Assert.Contains("AP  fired", output.ToString());
        Assert.Contains($"pass  {Name}", output.ToString());
        return 0;
    });

    [Fact]
    public Task A_failing_game_prints_its_lines_and_replay_as_it_happens_and_the_report_lists_it_first() => InDirAsync(async directory =>
    {
        var failing = Game(name: "bad-game", checks: [Checks.Event("p.8 quoted", _ => "expected 3 hits, saw 2")]);
        var output = new StringWriter();
        var code = await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Group, "--out", directory]), output,
            new SuiteRunner([failing, Game()]), Ct);
        Assert.Equal(1, code);

        var console = output.ToString();
        var failLine = console.IndexOf("FAIL  bad-game", StringComparison.Ordinal);
        Assert.True(failLine >= 0, console);
        Assert.True(console.IndexOf("p.8 quoted: expected 3 hits, saw 2", StringComparison.Ordinal) > failLine, console);
        Assert.Contains($"replay: {SuiteRunner.ReplayOf(failing, DriverKind.InProcess)}", console);
        Assert.DoesNotContain($"pass  {Name}", console);         // a passing game is a counter, not a line of its own
        Assert.Contains("FAILED: 1 of 2 games passed", console);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Only(directory)));
        Assert.Equal("failed", json.RootElement.GetProperty("verdict").GetString());
        var bad = json.RootElement.GetProperty("games")[0];
        Assert.Equal(["p.8 quoted: expected 3 hits, saw 2"], bad.GetProperty("failures").EnumerateArray().Select(e => e.GetString()));

        var md = await File.ReadAllTextAsync(Path.Combine(directory, "suite-latest.md"));
        var failures = md.IndexOf("## Failures (1)", StringComparison.Ordinal);
        Assert.True(failures >= 0, md);
        Assert.True(md.IndexOf("p.8 quoted: expected 3 hits, saw 2", StringComparison.Ordinal) > failures);
        Assert.Contains(SuiteRunner.ReplayOf(failing, DriverKind.InProcess), md);
        Assert.True(md.IndexOf("## Coverage", StringComparison.Ordinal) > md.IndexOf(SuiteRunner.ReplayOf(failing, DriverKind.InProcess), StringComparison.Ordinal));
        return 0;
    });

    [Fact]
    public Task A_full_run_prints_the_coverage_entries_it_fails_on() => InDirAsync(async directory =>
    {
        var output = new StringWriter();
        Assert.Equal(1, await Program.RunSuiteAsync(SuiteArguments.Parse(["--out", directory]), output, new SuiteRunner([Game()]), Ct));
        Assert.Contains("UNCOVERED", output.ToString());
        Assert.DoesNotContain("AP  fired", output.ToString());
        return 0;
    });

    [Fact]
    public Task A_cancelled_run_still_writes_its_report_with_the_games_played() => InDirAsync(async directory =>
    {
        using var cts = new CancellationTokenSource();
        var first = Game(name: "first", checks: [Checks.Event("cancels the run", _ => { cts.Cancel(); return null; })]);
        var output = new StringWriter();
        var code = await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Group, "--out", directory]), output,
            new SuiteRunner([first, Game(name: "second")]), cts.Token);
        Assert.Equal(1, code);
        Assert.Contains("ABORTED", output.ToString());

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Only(directory)));
        var root = json.RootElement;
        Assert.Equal("aborted", root.GetProperty("verdict").GetString());
        Assert.Equal(["first"], root.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("coverage").ValueKind);
        Assert.Contains("ABORTED", await File.ReadAllTextAsync(Path.Combine(directory, "suite-latest.md")));
        return 0;
    });

    [Fact]
    public Task The_report_is_on_disk_after_each_game_before_the_run_ends() => InDirAsync(async directory =>
    {
        string? midRun = null;
        var second = Game(name: "second", checks: [Checks.Event("reads the report", _ =>
        {
            midRun = File.ReadAllText(Path.Combine(directory, "suite-latest.json"));
            return null;
        })]);
        await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Group, "--out", directory]), new StringWriter(),
            new SuiteRunner([Game(name: "first"), second]), Ct);
        using var json = JsonDocument.Parse(midRun!);
        Assert.Equal("running", json.RootElement.GetProperty("verdict").GetString());
        Assert.Equal(["first"], json.RootElement.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        return 0;
    });
}
