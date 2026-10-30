using Tabletop.Sim.Lifecycle;

namespace Tabletop.Sim.Tests.Lifecycle;

public class LifecycleArmyTests
{
    static List<string> Names(System.Text.Json.Nodes.JsonObject list) => list["units"]!.AsArray().Select(u => (string)u!["name"]!).ToList();
    static List<string> Rules(System.Text.Json.Nodes.JsonObject list, string unit) =>
        list["units"]!.AsArray().Single(u => (string)u!["name"]! == unit)!["rules"]!.AsArray().Select(r => (string)r!["name"]!).ToList();

    [Fact]
    public void The_list_keeps_the_first_unjoined_unit_of_each_name()
    {
        var list = LifecycleArmy.List();
        Assert.Equal(["Blood Warriors", "Blood Hounds", "Beast Riders"], Names(list));
        Assert.All(list["units"]!.AsArray(), u => Assert.Null(u!["joinToUnit"]));
        Assert.Equal("gf", (string)list["gameSystem"]!);
        Assert.DoesNotContain("Ambush", Rules(list, LifecycleArmy.AmbushUnit));
    }

    [Fact]
    public void The_ambush_list_gives_the_beast_riders_ambush_and_nothing_else_changes()
    {
        var plain = LifecycleArmy.List();
        var ambush = LifecycleArmy.List(ambush: true);
        Assert.Equal([.. Rules(plain, LifecycleArmy.AmbushUnit), "Ambush"], Rules(ambush, LifecycleArmy.AmbushUnit));
        Assert.Equal(Rules(plain, "Blood Hounds"), Rules(ambush, "Blood Hounds"));
        Assert.Equal(Names(plain), Names(ambush));
    }

    [Fact]
    public void Four_books_each_give_a_list_of_three_unjoined_units_of_that_book()
    {
        Assert.Equal(4, LifecycleArmy.Books.Count);
        Assert.Equal(4, LifecycleArmy.Books.Distinct().Count());
        for (var book = 0; book < LifecycleArmy.Books.Count; book++)
        {
            var list = LifecycleArmy.List(book);
            Assert.Equal(3, list["units"]!.AsArray().Count);
            Assert.Equal("gf", (string)list["gameSystem"]!);
            Assert.All(list["units"]!.AsArray(), u =>
            {
                Assert.Null(u!["joinToUnit"]);
                Assert.Equal(LifecycleArmy.Books[book], (string)u["armyId"]!);
            });
        }
    }

    [Fact]
    public void The_3_6_list_is_the_3_6_cut_of_the_same_book_and_the_3_5_list_stays_as_it_was()
    {
        var book = LifecycleArmy.Books[3];
        var v36 = LifecycleArmy.List(3, edition: "3.6");
        Assert.Equal(3, v36["units"]!.AsArray().Count);
        Assert.All(v36["units"]!.AsArray(), u => { Assert.Null(u!["joinToUnit"]); Assert.Equal(book, (string)u["armyId"]!); });
        Assert.Equal(Names(LifecycleArmy.List(3)), Names(v36));
        Assert.NotEqual(LifecycleArmy.List(3).ToJsonString(), v36.ToJsonString());
        Assert.Equal(LifecycleArmy.List(3).ToJsonString(), LifecycleArmy.List(3, edition: "3.5").ToJsonString());
        Assert.Throws<ArgumentException>(() => LifecycleArmy.List(1, edition: "3.6"));
    }
}
