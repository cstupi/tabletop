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
        var game = Game(checks: [Failing("p.5 quoted")]) with { Known = [new KnownDefect("D9", "p.5 quoted")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.False(result.Passed);
        Assert.Equal(["D9"], result.Known);
        Assert.Equal(["known defect D9: p.5 quoted: the engine got it wrong"], result.Failures);
    }

    [Fact]
    public async Task A_failure_no_pin_matches_leaves_the_game_unexpected()
    {
        var game = Game(checks: [Failing("p.5 quoted"), Failing("p.8 other")]) with { Known = [new KnownDefect("D9", "p.5 quoted")] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Contains("p.8 other: the engine got it wrong", result.Failures);
        Assert.Contains("known defect D9: p.5 quoted: the engine got it wrong", result.Failures);
    }

    [Fact]
    public async Task A_pinned_game_that_passes_says_its_defect_no_longer_shows()
    {
        var game = Game() with { Known = [new KnownDefect("D9", "p.5 quoted")] };
        var report = await RunAsync(game);
        var result = Assert.Single(report.Games);
        Assert.True(result.Passed);
        Assert.Null(result.Known);
        Assert.Contains("pinned D9 did not fail", report.Table());
    }

    [Fact]
    public async Task A_pin_for_the_other_driver_does_not_apply()
    {
        var game = Game(checks: [Failing("p.5 quoted")]) with { Known = [new KnownDefect("D12", "p.5 quoted", DriverKind.Socket)] };
        var result = Assert.Single((await RunAsync(game)).Games);
        Assert.Null(result.Known);
        Assert.Null(result.Pinned);
        Assert.Equal(["p.5 quoted: the engine got it wrong"], result.Failures);
    }

    [Fact]
    public async Task The_table_counts_known_defects_apart_from_unexpected_failures()
    {
        var pinned = Game(checks: [Failing("p.5 quoted")], name: "pinned") with { Known = [new KnownDefect("D9", "p.5 quoted")] };
        var broken = Game(checks: [Failing("p.8 other")], name: "broken");
        var report = await new SuiteRunner([pinned, broken]).RunAsync(new SuiteOptions(["pinned", "broken"], DriverKind.InProcess, false, TempDir()), null, Ct);
        var table = report.Table();
        Assert.Contains("games (0/2 passed, 1 failing on known defects, 1 unexpected)", table);
        Assert.Contains("  KNOWN D9  pinned", table);
        Assert.Contains("  FAIL  broken", table);
    }
}
