using Tabletop.Protocol;
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

    [Theory]
    [InlineData("Red unit 1")]
    [InlineData("Blue unit 1")]
    public async Task A_keyed_dice_fix_sets_the_named_units_roll(string actor)
    {
        var check = new GameCheck("the edited roll is the keyed unit's", run =>
        {
            var payloads = run.EffectiveLog.Select(e => e.Payload).ToList();
            if (payloads.OfType<DiceEdited>().FirstOrDefault() is not { } edit) return "no dice_edited in the log";
            var step = payloads.OfType<StepCompleted>().First(s => s.Rolls.Any(r => r.Id == edit.RollId));
            var frame = payloads.OfType<SequenceStarted>().First(s => s.FrameId == step.FrameId);
            var name = run.Final.Units[frame.Context.ActorUnitId!.Value].Definition.Name;
            return name == actor ? null : $"edited {name}'s roll";
        });
        var report = await RunAsync(Game(dice: [new DiceFix("hit", 0, [6], new RollKey(Actor: actor))], checks: [Checks.Ended(), Checks.Invariants(), check]));
        Assert.True(report.Passed, Why(report));
    }

    [Fact]
    public async Task A_keyed_dice_fix_that_matches_no_roll_fails_the_game_with_its_key()
    {
        var report = await RunAsync(Game(dice: [new DiceFix("hit", 0, [6], new RollKey(Actor: "Green unit 1"))]));
        Assert.Contains(Assert.Single(report.Games).Failures, f => f.Contains("hit#0 by Green unit 1") && f.Contains("never made that roll"));
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
    public async Task Every_game_is_judged_by_ended_invariants_and_fired_whether_or_not_it_lists_them()
    {
        var report = await RunAsync(Game(covers: ["AP", "Furious"], checks: []));       // Furious is on no model: it cannot fire
        Assert.False(report.Passed);
        Assert.Equal(["fired: never fired: Furious"], report.Games.Single().Failures);
        Assert.False(report.Coverage.Single(c => c.Name == "Furious").Fired);

        // A game that lists one of them is not judged by it twice.
        var listed = await RunAsync(Game(covers: ["AP", "Furious"], checks: [Checks.FiredAll(), Checks.Ended()]));
        Assert.Equal(["fired: never fired: Furious"], listed.Games.Single().Failures);
    }

    [Fact]
    public async Task Exact_checks_are_reported_against_the_entries_they_name()
    {
        var report = await RunAsync(Game(checks:
        [
            Checks.Event("p.13 AP: -1 to the block", _ => null, "AP"),
            Checks.Event("p.13 AP: a made-up miss", _ => "expected 2, saw 1", "AP"),
            Checks.Event("about nothing", _ => null),
        ]));
        var ap = report.Coverage.Single(c => c.Name == "AP");
        Assert.Equal([new ExactResult(Name, "p.13 AP: -1 to the block", true), new ExactResult(Name, "p.13 AP: a made-up miss", false)], ap.Exact);
        Assert.Contains($"exact 1/2  FAIL {Name}: p.13 AP: a made-up miss", report.Table());
        Assert.Contains("\"exact\"", report.ToJson());
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
    public async Task The_suite_command_exits_on_the_verdict_lists_games_and_refuses_an_unknown_selection()
    {
        var directory = TempDir();
        try
        {
            Assert.Equal(0, await Program.RunSuiteAsync(SuiteArguments.Parse(["--only", Name, "--out", directory]), new StringWriter(), new SuiteRunner([Game()]), Ct));
            Assert.Equal(1, await Program.RunSuiteAsync(SuiteArguments.Parse(["--out", directory]), new StringWriter(), new SuiteRunner([Game()]), Ct));   // full run: coverage

            var listed = new StringWriter();
            Assert.Equal(0, await Program.RunSuiteAsync(SuiteArguments.Parse(["--list"]), listed, new SuiteRunner([Game()]), Ct));
            Assert.Equal($"{Name}  [{Group}]  AP", listed.ToString().Trim());

            await Assert.ThrowsAsync<UsageException>(() => Program.RunSuiteAsync(SuiteArguments.Parse(["--only", "nope", "--out", directory]), new StringWriter(), new SuiteRunner([Game()]), Ct));
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
