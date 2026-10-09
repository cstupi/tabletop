using System.Text.Json;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The runner in process, over the sample game: the whole path from TestGame to report.</summary>
public class SuiteRunnerTests
{
    static SuiteOptions Options(bool keep = false, string? outDir = null, params string[] only) =>
        new(only, DriverKind.InProcess, keep, outDir ?? TempDir());

    static async Task<SuiteReport> RunAsync(TestGame game, SuiteOptions? options = null) =>
        await new SuiteRunner([game]).RunAsync(options ?? Options(only: game.Name), null, Ct);

    static string Why(SuiteReport report) => string.Join("\n", report.Games.SelectMany(g => g.Failures));

    [Fact]
    public async Task The_sample_game_passes_and_reports_what_it_covers()
    {
        var report = await RunAsync(Game());
        Assert.True(report.Passed, Why(report));
        var result = Assert.Single(report.Games);
        Assert.Equal((Name, Group, 3UL, (string?)null, (Guid?)null), (result.Name, result.Group, result.Seed, result.RecordingPath, result.GameId));
        Assert.Equal(SuiteRunner.ReplayOf(Game(), DriverKind.InProcess), result.Replay);
        var ap = Assert.Single(report.Coverage);
        Assert.Equal(("AP", "rule", true), (ap.Name, ap.Kind, ap.Fired));
        Assert.Equal([Name], ap.Games);
    }

    [Theory]
    [InlineData(6, 1)]      // p.8: "a 6 is always a success"; the hit leads to one defense die
    [InlineData(1, 0)]      // p.8: "a 1 is always a failure"; no hit, so no defense roll
    public async Task A_dice_fix_sets_the_roll_and_the_outcome_follows(int face, int defenseDice)
    {
        string? edited = null;
        var check = Checks.Event("the fixed hit decides the block", log =>
        {
            var defense = DefenseAfterFirstEdit(log);
            edited = $"{defense?.Sum(r => r.Dice.Count)}";
            return defense is null ? "no dice_edited in the log" : defense.Sum(r => r.Dice.Count) == defenseDice ? null : $"{defense.Sum(r => r.Dice.Count)} defense dice";
        });
        var report = await RunAsync(Game(dice: [new DiceFix("hit", 0, [face])], checks: [Checks.Ended(), Checks.Invariants(), check]));
        Assert.True(report.Passed, Why(report) + $" (defense dice {edited})");
    }

    [Fact]
    public async Task An_unapplied_dice_fix_fails_the_game()
    {
        var report = await RunAsync(Game(dice: [new DiceFix("hit", 9_999, [6]), new DiceFix("hit", 0, [6, 6])]));
        Assert.False(report.Passed);
        var failures = Assert.Single(report.Games).Failures;
        Assert.Contains(failures, f => f.Contains("hit#9999") && f.Contains("never made that roll"));
        Assert.Contains(failures, f => f.Contains("hit#0") && f.Contains("2 faces for a roll of 1 dice"));
    }

    [Fact]
    public async Task A_failing_check_fails_the_game_with_its_name_and_reason_and_a_throwing_one_too()
    {
        var report = await RunAsync(Game(checks: [Checks.Event("p.8 quoted", _ => "expected 3 hits, saw 2"), new GameCheck("boom", _ => throw new InvalidOperationException("bad"))]));
        var failures = Assert.Single(report.Games).Failures;
        Assert.Contains("p.8 quoted: expected 3 hits, saw 2", failures);
        Assert.Contains(failures, f => f.StartsWith("boom: threw InvalidOperationException"));
        Assert.False(report.Passed);
    }

    [Fact]
    public async Task A_full_run_fails_on_coverage_and_an_only_run_does_not()
    {
        var full = await new SuiteRunner([Game()]).RunAsync(Options(), null, Ct);
        Assert.True(full.Games.Single().Passed, Why(full));
        Assert.False(full.Passed);                                                   // every other catalog entry is uncovered
        Assert.Contains(full.Coverage, c => c.Uncovered);
        Assert.Equal(1, full.ExitCode);

        var only = await new SuiteRunner([Game()]).RunAsync(Options(only: Group), null, Ct);
        Assert.True(only.Passed, Why(only));
        Assert.Equal(0, only.ExitCode);
    }

    [Fact]
    public async Task Nothing_is_written_without_keep_and_keep_writes_the_recording()
    {
        var directory = TempDir();
        try
        {
            var plain = await RunAsync(Game(), Options(outDir: directory, only: Name));
            Assert.Null(plain.Games.Single().RecordingPath);
            Assert.False(Directory.Exists(directory));

            var kept = await RunAsync(Game(), Options(keep: true, outDir: directory, only: Name));
            var path = kept.Games.Single().RecordingPath;
            Assert.Equal(Path.Combine(directory, $"{Name}-3.json"), path);
            var recording = await RecordingJson.ReadAsync(path!, Ct);
            Assert.Equal((Name, 3UL), (recording.Meta.Scenario, recording.Meta.Seed));
            Assert.NotEmpty(recording.Frames);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Progress_reports_each_game_as_it_finishes()
    {
        var seen = new List<string>();
        var games = new[] { Game(name: "one"), Game(name: "two") };
        await new SuiteRunner(games).RunAsync(Options(only: Group), new Inline(r => seen.Add(r.Name)), Ct);
        Assert.Equal(["one", "two"], seen);
    }

    sealed class Inline(Action<SuiteGameResult> report) : IProgress<SuiteGameResult>
    {
        public void Report(SuiteGameResult value) => report(value);
    }

    [Fact]
    public async Task The_suite_command_prints_the_report_writes_suite_json_and_exits_on_the_verdict()
    {
        var directory = TempDir();
        try
        {
            var output = new StringWriter();
            var code = await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Name, "--out", directory]), output, new SuiteRunner([Game()]), Ct);
            Assert.Equal(0, code);
            Assert.Contains($"pass  {Name}", output.ToString());
            Assert.Contains("PASSED", output.ToString());
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, SuiteReport.FileName)));
            Assert.True(json.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(Name, json.RootElement.GetProperty("games")[0].GetProperty("name").GetString());
            Assert.Equal("AP", json.RootElement.GetProperty("coverage")[0].GetProperty("name").GetString());

            var failing = new StringWriter();
            Assert.Equal(1, await Program.RunSuiteAsync(SuiteArguments.Parse([]), failing, new SuiteRunner([Game()]), Ct));   // full run: coverage
            Assert.Contains("UNCOVERED", failing.ToString());

            var listed = new StringWriter();
            Assert.Equal(0, await Program.RunSuiteAsync(SuiteArguments.Parse(["--list"]), listed, new SuiteRunner([Game()]), Ct));
            Assert.Equal($"{Name}  [{Group}]  AP", listed.ToString().Trim());

            await Assert.ThrowsAsync<UsageException>(() => Program.RunSuiteAsync(SuiteArguments.Parse(["--only", "nope"]), new StringWriter(), new SuiteRunner([Game()]), Ct));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Main_routes_the_suite_verb_and_a_usage_error_exits_two()
    {
        Assert.Equal(2, await Program.Main(["suite", "--only", "no-such-game-or-group"]));
        Assert.Equal(2, await Program.Main(["suite", "--bogus"]));
    }
}
