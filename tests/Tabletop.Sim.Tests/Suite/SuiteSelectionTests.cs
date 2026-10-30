using Tabletop.Content;
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
    public void A_two_edition_game_expands_to_a_bare_and_an_at_3_6_name_and_only_selects_either()
    {
        var games = SuiteCatalog.Expand(
        [
            SuiteFixtures.Game(name: "volley", group: "core") with { Editions = [RuleSets.Gf351, RuleSets.Gf360] },
            SuiteFixtures.Game(name: "brawl", group: "core"),
            SuiteFixtures.Game(name: "orc-rules", group: SuiteCatalog.ArmyGroup(RuleSets.Gf351, "orc")) with { Editions = [RuleSets.Gf351, RuleSets.Gf360] },
        ]);

        Assert.Equal(["volley", "volley@3.6", "brawl", "orc-rules", "orc-rules@3.6"], games.Select(g => g.Name));
        Assert.Equal([RuleSets.Gf351, RuleSets.Gf360, RuleSets.Gf351, RuleSets.Gf351, RuleSets.Gf360], games.Select(g => g.Edition));
        Assert.All(games, g => Assert.Equal(g.Edition, g.Scenario.Rules));                  // the run compiles against its edition
        Assert.Equal(["core", "core", "core", "army:3.5:orc", "army:3.6:orc"], games.Select(g => g.Group));
        Assert.Equal("orc", SuiteCatalog.BookOf(games[4].Group));

        IEnumerable<string> Picked(params string[] only) => SuiteCatalog.Select(games, only).Select(g => g.Name);
        Assert.Equal(["volley"], Picked("volley"));
        Assert.Equal(["volley@3.6"], Picked("volley@3.6"));
        Assert.Equal(["volley", "volley@3.6", "brawl"], Picked("core"));
        Assert.Equal(["orc-rules@3.6"], Picked("army:3.6:orc"));
    }

    [Fact]
    public void A_game_under_3_6_alone_is_named_at_3_6()
    {
        var game = Assert.Single(SuiteCatalog.Expand([SuiteFixtures.Game(name: "c1", group: "core") with { Edition = RuleSets.Gf360 }]));
        Assert.Equal(("c1@3.6", RuleSets.Gf360, RuleSets.Gf360), (game.Name, game.Edition, game.Scenario.Rules));
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
        Assert.Equal("dotnet run --project sim -- suite --only 'two words' --keep --driver socket",
            SuiteRunner.ReplayOf(SuiteFixtures.Game(name: "two words"), DriverKind.Socket));
        Assert.Equal("dotnet run --project sim -- suite --only 'it'\\''s $HOME' --keep",          // single quotes: the shell expands nothing
            SuiteRunner.ReplayOf(SuiteFixtures.Game(name: "it's $HOME"), DriverKind.InProcess));
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
        Assert.All(all, g => Assert.Empty(g.Covers.Where(c => ScenarioCompiler.CorePropertiesOf(g.Edition).Entry(c) is null)));
        Assert.All(all, g => Assert.Equal(g.Edition, g.Scenario.Rules));
        Assert.All(all, g => Assert.DoesNotContain(all, other => other.Name == g.Group));    // a name never shadows a group
    }
}
