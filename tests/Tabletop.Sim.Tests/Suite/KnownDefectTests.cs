using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>A game pinned to an engine defect reports its failure as that defect's, apart from failures nobody expected.</summary>
public class KnownDefectTests
{
    static async Task<SuiteReport> RunAsync(TestGame game) =>
        await new SuiteRunner([game]).RunAsync(new SuiteOptions([game.Name], DriverKind.InProcess, false, TempDir()), null, Ct);

    static GameCheck Failing(string name) => Checks.Event(name, _ => "the engine got it wrong");

    [Fact]
    public async Task A_failure_the_game_pins_is_reported_as_its_known_defect()
    {
        var game = Game(checks: [Failing("p.5 quoted")]) with { Known = [new KnownDefect("D9", "p.5 quoted", "got it wrong")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.False(result.Passed);
        Assert.Equal(["D9"], result.Known);
        Assert.Equal(["known defect D9: p.5 quoted: the engine got it wrong"], result.Failures);
    }

    [Fact]
    public async Task A_failure_no_pin_matches_leaves_the_game_unexpected()
    {
        var game = Game(checks: [Failing("p.5 quoted"), Failing("p.8 other")]) with { Known = [new KnownDefect("D9", "p.5 quoted", "got it wrong")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Contains("p.8 other: the engine got it wrong", result.Failures);
        Assert.Contains("known defect D9: p.5 quoted: the engine got it wrong", result.Failures);
    }

    [Fact]
    public async Task A_pinned_game_that_passes_says_its_defect_no_longer_shows()
    {
        var game = Game() with { Known = [new KnownDefect("D9", "p.5 quoted", "got it wrong")] };
        var report = await RunAsync(game);
        var result = Assert.Single(report.Games);
        Assert.True(result.Passed);
        Assert.Null(result.Known);
        Assert.Equal(["D9: p.5 quoted"], result.Stale);
        Assert.Contains("pinned D9 did not fail", report.Table());
    }

    [Fact]
    public async Task The_pinned_check_failing_for_another_reason_is_unexpected()
    {
        var game = Game(checks: [Failing("p.5 quoted")]) with { Known = [new KnownDefect("D9", "p.5 quoted", "raw 1 judged a success")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Equal(["p.5 quoted: the engine got it wrong"], result.Failures);
        Assert.Equal(["D9: p.5 quoted"], result.Stale);
    }

    /// <summary>A check that fails for several reasons reports each on its own line, so a pin naming one leaves the others unexpected.</summary>
    [Fact]
    public async Task A_second_reason_beside_a_pinned_one_is_unexpected()
    {
        var check = GameCheck.Each("p.5 quoted", _ => ["raw 1 judged a success", "a new failure"]);
        var game = Game(checks: [check]) with { Known = [new KnownDefect("D9", "p.5 quoted", "raw 1 judged a success")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Equal(["known defect D9: p.5 quoted: raw 1 judged a success", "p.5 quoted: a new failure"], result.Failures);
    }

    [Fact]
    public async Task An_entry_going_silent_beside_a_pinned_silent_one_is_unexpected()
    {
        var game = Game(covers: ["Slayer", "Tear"]) with { Known = [new KnownDefect("D12", "fired", "never fired: Slayer")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Contains("known defect D12: fired: never fired: Slayer", result.Failures);
        Assert.Contains("fired: never fired: Tear", result.Failures);
    }

    [Fact]
    public async Task A_pin_that_no_longer_fails_is_reported_while_the_others_still_do()
    {
        var game = Game(checks: [Failing("p.13 one")]) with
        {
            Known = [new KnownDefect("D7", "p.13 two", "allowance is 6"), new KnownDefect("D8", "p.13 one", "got it wrong")],
        };
        var report = await RunAsync(game);
        var result = Assert.Single(report.Games);
        Assert.Equal(["D8"], result.Known);
        Assert.Equal(["D7: p.13 two"], result.Stale);
        Assert.Contains("(pinned D7 did not fail: p.13 two)", report.Table());
    }

    [Fact]
    public async Task A_pin_for_the_other_driver_does_not_apply()
    {
        var game = Game(checks: [Failing("p.5 quoted")]) with { Known = [new KnownDefect("D12", "p.5 quoted", "got it wrong", DriverKind.Socket)] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Null(result.Pinned);
        Assert.Equal(["p.5 quoted: the engine got it wrong"], result.Failures);
    }

    [Fact]
    public async Task The_table_counts_known_defects_apart_from_unexpected_failures()
    {
        var pinned = Game(checks: [Failing("p.5 quoted")], name: "pinned") with { Known = [new KnownDefect("D9", "p.5 quoted", "got it wrong")] };
        var broken = Game(checks: [Failing("p.8 other")], name: "broken");
        var report = await new SuiteRunner([pinned, broken]).RunAsync(new SuiteOptions(["pinned", "broken"], DriverKind.InProcess, false, TempDir()), null, Ct);
        var table = report.Table();
        Assert.Contains("games (0/2 passed, 1 failing on known defects, 1 unexpected)", table);
        Assert.Contains("  KNOWN D9  pinned", table);
        Assert.Contains("  FAIL  broken", table);
    }
}
