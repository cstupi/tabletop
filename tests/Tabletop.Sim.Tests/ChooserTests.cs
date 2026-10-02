namespace Tabletop.Sim.Tests;

public class ChooserTests
{
    [Fact]
    public void Two_choosers_on_one_seed_make_the_same_choices()
    {
        var a = new Chooser(3); var b = new Chooser(3);
        for (var i = 0; i < 100; i++) Assert.Equal(a.Next(17), b.Next(17));
        Assert.Equal(100, a.Drawn);
    }

    [Fact]
    public void Next_stays_in_range_and_covers_it()
    {
        var c = new Chooser(1);
        var seen = new HashSet<int>();
        for (var i = 0; i < 500; i++) { var n = c.Next(4); Assert.InRange(n, 0, 3); seen.Add(n); }
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void Shuffle_and_subset_permute_without_losing_or_repeating()
    {
        var c = new Chooser(9);
        int[] items = [1, 2, 3, 4, 5];
        var shuffled = c.Shuffle(items);
        Assert.Equal([1, 2, 3, 4, 5], shuffled.Order());
        var three = c.Subset(items, 3);
        Assert.Equal(3, three.Count);
        Assert.Equal(3, three.Distinct().Count());
    }

    [Fact]
    public void Next_of_one_draws_nothing_and_next_of_zero_throws()
    {
        var c = new Chooser(1);
        Assert.Equal(0, c.Next(1));
        Assert.Equal(0, c.Drawn);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Next(0));
    }
}
