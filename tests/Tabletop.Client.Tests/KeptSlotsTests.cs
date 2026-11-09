using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>The Hud's kept children (Hud/Kept.cs over KeptSlots): a render takes each child by key in order, reusing the one made for that key,
/// and every child it did not take is left over, hidden by the box.</summary>
public class KeptSlotsTests
{
    sealed class Item(string name) { public string Name = name; public override string ToString() => Name; }
    sealed class Other(string name) { public string Name = name; }

    static int _made;
    static Func<Item> Make(string name) => () => { _made++; return new Item(name); };

    [Fact]
    public void A_second_render_reuses_every_child_by_key_in_order()
    {
        var slots = new KeptSlots<object>();
        slots.Begin();
        var a = slots.Take<Item>("a", Make("a1")).Item; var b = slots.Take<Item>("b", Make("b1")).Item;
        slots.End();
        slots.Begin();
        var a2 = slots.Take<Item>("a", Make("a2")); var b2 = slots.Take<Item>("b", Make("b2"));
        Assert.Same(a, a2.Item); Assert.Same(b, b2.Item);
        Assert.False(a2.Made); Assert.Null(a2.MovedTo); Assert.Null(b2.MovedTo);
        Assert.Empty(slots.End());
    }

    [Fact]
    public void A_render_that_takes_fewer_leaves_the_rest_over_to_hide_and_takes_them_again_later()
    {
        var slots = new KeptSlots<object>();
        slots.Begin();
        var items = Enumerable.Range(0, 4).Select(i => slots.Take<Item>("tile", Make($"t{i}")).Item).ToList();
        slots.End();
        slots.Begin();
        Assert.Same(items[0], slots.Take<Item>("tile", Make("x")).Item);
        Assert.Same(items[1], slots.Take<Item>("tile", Make("x")).Item);
        Assert.Equal(new object[] { items[2], items[3] }, slots.End());
        Assert.Equal(2, slots.Taken);
        slots.Begin();
        var again = Enumerable.Range(0, 4).Select(_ => slots.Take<Item>("tile", Make("x"))).ToList();
        Assert.Equal(items, again.Select(t => t.Item));
        Assert.All(again, t => Assert.False(t.Made));
        Assert.Empty(slots.End());
    }

    [Fact]
    public void A_key_taken_out_of_order_is_moved_to_where_it_is_taken()
    {
        var slots = new KeptSlots<object>();
        slots.Begin();
        var band = slots.Take<Item>("band", Make("band")).Item; var icon = slots.Take<Item>("icon", Make("icon")).Item; var line = slots.Take<Item>("line", Make("line")).Item;
        slots.End();
        slots.Begin();                                   // the header's wait line: mine draws icon, line; another team's band, icon, line
        var i = slots.Take<Item>("icon", Make("x")); var l = slots.Take<Item>("line", Make("x"));
        Assert.Same(icon, i.Item); Assert.Equal(0, i.MovedTo);
        Assert.Same(line, l.Item); Assert.Equal(1, l.MovedTo);
        Assert.Equal(new object[] { band }, slots.End());
        Assert.Equal(new object[] { icon, line, band }, slots.Items);
    }

    [Fact]
    public void A_new_key_is_made_at_the_taking_position_without_dropping_the_others()
    {
        var slots = new KeptSlots<object>();
        slots.Begin();
        var name = slots.Take<Item>("name", Make("name")).Item; var line = slots.Take<Item>("line", Make("line")).Item;
        slots.End();
        slots.Begin();
        slots.Take<Item>("name", Make("x"));
        var made = slots.Take<Item>("note", Make("note"));
        Assert.True(made.Made); Assert.Equal(1, made.MovedTo);
        Assert.Same(line, slots.Take<Item>("line", Make("x")).Item);
        Assert.Empty(slots.End());
        Assert.Equal(new object[] { name, made.Item, line }, slots.Items);
    }

    [Fact]
    public void A_key_whose_kept_child_is_of_another_type_gets_a_new_child()
    {
        var slots = new KeptSlots<object>();
        slots.Begin();
        var other = slots.Take<Other>("cell", () => new Other("o")).Item;
        slots.End();
        slots.Begin();
        var item = slots.Take<Item>("cell", Make("i"));
        Assert.True(item.Made);
        Assert.Equal(new object[] { other }, slots.End());
    }

    [Fact]
    public void Clear_forgets_every_child_so_the_next_render_makes_them_again()
    {
        var slots = new KeptSlots<object>();
        slots.Begin(); slots.Take<Item>("a", Make("a")); slots.End();
        var gone = slots.Clear();
        Assert.Single(gone);
        slots.Begin();
        Assert.True(slots.Take<Item>("a", Make("a")).Made);
    }
}
