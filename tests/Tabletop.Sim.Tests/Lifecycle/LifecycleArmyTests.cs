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

    [Theory]
    [InlineData("goblin-reclaimers", "Goblin Reclaimers", 8, 5)]
    [InlineData("war-disciples", "War Disciples", 6, 5)]
    [InlineData("hdf-artillery-blob", "HDF Artillery Blob", 7, 5)]
    [InlineData("ratmen-clans", "Ratmen Clans", 6, 4)]
    public void A_whole_list_is_the_army_forge_export_with_joined_entries_counted_once(string list, string name, int entries, int units)
    {
        var body = LifecycleArmy.Whole(list);
        Assert.Equal(name, (string)body["name"]!);
        Assert.Equal("gf", (string)body["gameSystem"]!);
        Assert.Equal(1000, (int)body["pointsLimit"]!);
        Assert.Equal(entries, body["units"]!.AsArray().Count);
        Assert.Equal(units, LifecycleArmy.UnitsOf(body));
        Assert.Equal(body.ToJsonString(), LifecycleArmy.For(new Seat(0, 1, List: list), false, ArmyEditions.V36).ToJsonString());
    }

    [Fact]
    public void A_seat_without_a_list_takes_its_books_cut_and_a_whole_list_takes_no_ambush_and_no_other_edition()
    {
        Assert.Equal(LifecycleArmy.List(2).ToJsonString(), LifecycleArmy.For(new Seat(0, 1, 2), false, ArmyEditions.V36).ToJsonString());
        Assert.Equal(3, LifecycleArmy.UnitsOf(LifecycleArmy.List(2)));
        Assert.Throws<ArgumentException>(() => LifecycleArmy.For(new Seat(0, 1, List: "ratmen-clans"), true, ArmyEditions.V36));
        Assert.Throws<ArgumentException>(() => LifecycleArmy.For(new Seat(0, 1, List: "ratmen-clans"), false, ArmyEditions.V35));
        Assert.Throws<ArgumentException>(() => LifecycleArmy.Whole("nope"));
    }

    [Fact]
    public void Every_list_plays_once_in_a_two_player_whole_game_and_the_self_play_one_seats_ratmen_and_hdf_on_one_account()
    {
        var whole = LifecycleShapes.All.Where(s => s.Whole).ToList();
        Assert.Equal(["goblins-vs-disciples", "hdf-vs-ratmen", "self-play-ratmen-hdf"], whole.Select(s => s.Name));
        Assert.All(whole, s => Assert.Equal(Tabletop.Content.RuleSets.Gf360, s.Rules));
        Assert.Equal(LifecycleArmy.Lists.Order(), whole.Where(s => s.Accounts.Count == 2).SelectMany(s => s.Seats).Select(s => s.List!).Order());
        var self = whole.Single(s => s.Accounts.Count == 1);
        Assert.Equal(["ratmen-clans", "hdf-artillery-blob"], self.Seats.Select(s => s.List));
        Assert.DoesNotContain(LifecycleShapes.All.Where(s => !s.Whole), s => s.Seats.Any(x => x.List is not null));
    }
}
