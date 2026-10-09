using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

public class SuiteSelectionTests
{
    static readonly IReadOnlyList<TestGame> Games =
    [
        SuiteFixtures.Game(name: "melee-brawl", group: "core"),
        SuiteFixtures.Game(name: "volley", group: "core"),
        SuiteFixtures.Game(name: "furious-counter", group: "special"),
        SuiteFixtures.Game(name: "orc-rules", group: "army:orc"),
        SuiteFixtures.Game(name: "orc-spells", group: "spells:orc"),
        SuiteFixtures.Game(name: "elf-spells", group: "spells:elf"),
    ];

    static IEnumerable<string> Names(params string[] only) => SuiteCatalog.Select(Games, only).Select(g => g.Name);

    [Fact]
    public void Only_selects_by_game_name_and_by_group()
    {
        Assert.Equal(Games.Select(g => g.Name), Names());
        Assert.Equal(["melee-brawl", "volley"], Names("core"));
        Assert.Equal(["furious-counter"], Names("special"));
        Assert.Equal(["orc-rules"], Names("army:orc"));
        Assert.Equal(["orc-spells"], Names("spells:orc"));
        Assert.Equal(["volley", "orc-rules", "elf-spells"], Names("elf-spells", "volley", "army:orc"));   // the games' own order
        Assert.Equal(["volley"], Names("volley", "volley"));
    }

    [Fact]
    public void An_item_that_matches_nothing_is_a_usage_error()
    {
        var error = Assert.Throws<UsageException>(() => Names("core", "spells:dwarf"));
        Assert.Contains("spells:dwarf", error.Message);
    }

    [Fact]
    public void The_suite_arguments_parse()
    {
        var plain = SuiteArguments.Parse([]);
        Assert.Empty(plain.Only);
        Assert.Equal((DriverKind.InProcess, false, false, (string?)null), (plain.Driver, plain.Keep, plain.List, plain.Out));

        var full = SuiteArguments.Parse(["--only", "core, spells:orc", "--only", "volley", "--driver", "socket", "--keep", "--out", "/tmp/s", "--list"]);
        Assert.Equal(["core", "spells:orc", "volley"], full.Only);
        Assert.Equal((DriverKind.Socket, true, true, "/tmp/s"), (full.Driver, full.Keep, full.List, full.Out));

        Assert.Throws<UsageException>(() => SuiteArguments.Parse(["--driver", "carrier-pigeon"]));
        Assert.Throws<UsageException>(() => SuiteArguments.Parse(["--only"]));
        Assert.Throws<UsageException>(() => SuiteArguments.Parse(["core"]));
    }

    [Fact]
    public void Replay_reruns_one_game_alone_with_keep()
    {
        Assert.Equal("dotnet run --project sim -- suite --only volley --keep", SuiteRunner.ReplayOf(Games[1], DriverKind.InProcess));
        Assert.Equal("dotnet run --project sim -- suite --only \"two words\" --keep --driver socket",
            SuiteRunner.ReplayOf(SuiteFixtures.Game(name: "two words"), DriverKind.Socket));
    }

    /// <summary>Guards for the four group files: what --only, the replay line and coverage rely on.</summary>
    [Fact]
    public void Catalog_games_have_unique_selectable_names_known_groups_and_covers_in_the_catalog()
    {
        var all = SuiteCatalog.All;
        Assert.Equal(all.Count, all.Select(g => g.Name).Distinct().Count());
        Assert.All(all, g => Assert.DoesNotContain(',', g.Name));
        Assert.All(all, g => Assert.True(g.Name.Length <= 56, $"{g.Name}: a socket run mints sim-<name>-<n>, and management takes 64 characters"));
        Assert.All(all, g => Assert.True(g.Group is SuiteCatalog.CoreGroup or SuiteCatalog.SpecialGroup
            || g.Group.StartsWith(SuiteCatalog.ArmyPrefix) || g.Group.StartsWith(SuiteCatalog.SpellsPrefix), $"{g.Name}: group '{g.Group}'"));
        Assert.All(all, g => Assert.Empty(g.Covers.Where(c => ScenarioCompiler.CoreProperties.Entry(c) is null)));
        Assert.All(all, g => Assert.DoesNotContain(all, other => other.Name == g.Group));    // a name never shadows a group
    }
}
