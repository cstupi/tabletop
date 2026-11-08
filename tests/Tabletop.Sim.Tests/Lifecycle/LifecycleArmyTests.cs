using Tabletop.Content;
using Tabletop.Sim.Lifecycle;

namespace Tabletop.Sim.Tests.Lifecycle;

public class LifecycleArmyTests
{
    static List<string> Names(System.Text.Json.Nodes.JsonObject list) => list["units"]!.AsArray().Select(u => (string)u!["name"]!).ToList();
    static List<string> Rules(System.Text.Json.Nodes.JsonObject list, string unit) =>
        list["units"]!.AsArray().Single(u => (string)u!["name"]! == unit)!["rules"]!.AsArray().Select(r => (string)r!["name"]!).ToList();

    [Theory]
    [InlineData(ArmyEditions.V36, "Blood Beast Riders")]
    [InlineData(ArmyEditions.V35, "Beast Riders")]
    public void Book_0_keeps_the_first_unjoined_unit_of_each_name(string edition, string riders)
    {
        var list = LifecycleArmy.List(edition: edition);
        Assert.Equal(["Blood Warriors", "Blood Hounds", riders], Names(list));
        Assert.Equal(riders, LifecycleArmy.AmbushUnit(edition));
        Assert.All(list["units"]!.AsArray(), u => Assert.Null(u!["joinToUnit"]));
        Assert.Equal("gf", (string)list["gameSystem"]!);
        Assert.DoesNotContain("Ambush", Rules(list, riders));
    }

    [Theory]
    [InlineData(ArmyEditions.V36)]
    [InlineData(ArmyEditions.V35)]
    public void The_ambush_list_gives_the_riders_ambush_and_nothing_else_changes(string edition)
    {
        var plain = LifecycleArmy.List(edition: edition);
        var ambush = LifecycleArmy.List(ambush: true, edition: edition);
        var riders = LifecycleArmy.AmbushUnit(edition);
        Assert.Equal([.. Rules(plain, riders), "Ambush"], Rules(ambush, riders));
        Assert.Equal(Rules(plain, "Blood Hounds"), Rules(ambush, "Blood Hounds"));
        Assert.Equal(Names(plain), Names(ambush));
        Assert.Throws<ArgumentException>(() => LifecycleArmy.List(1, ambush: true, edition: edition));
    }

    [Theory]
    [InlineData(ArmyEditions.V36)]
    [InlineData(ArmyEditions.V35)]
    public void Four_books_each_give_a_list_of_three_unjoined_units_of_that_book(string edition)
    {
        Assert.Equal(4, LifecycleArmy.Books.Count);
        Assert.Equal(4, LifecycleArmy.Books.Distinct().Count());
        for (var book = 0; book < LifecycleArmy.Books.Count; book++)
        {
            var list = LifecycleArmy.List(book, edition: edition);
            Assert.Equal(3, list["units"]!.AsArray().Count);
            Assert.Equal("gf", (string)list["gameSystem"]!);
            Assert.All(list["units"]!.AsArray(), u =>
            {
                Assert.Null(u!["joinToUnit"]);
                if (edition == ArmyEditions.V36 || book > 0) Assert.Equal(LifecycleArmy.Books[book], (string)u["armyId"]!);
            });
        }
    }

    [Fact]
    public void The_lists_are_3_6_by_default_and_differ_from_the_3_5_cut_of_each_book()
    {
        for (var book = 0; book < LifecycleArmy.Books.Count; book++)
        {
            Assert.Equal(LifecycleArmy.List(book, edition: ArmyEditions.V36).ToJsonString(), LifecycleArmy.List(book).ToJsonString());
            Assert.NotEqual(LifecycleArmy.List(book, edition: ArmyEditions.V35).ToJsonString(), LifecycleArmy.List(book).ToJsonString());
        }
    }
}
