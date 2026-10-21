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
}
