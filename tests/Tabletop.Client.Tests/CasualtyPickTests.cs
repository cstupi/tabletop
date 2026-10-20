using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class CasualtyPickTests
{
    static readonly Guid M1 = Guid.Parse("0198b7ac-0011-7000-8000-000000000011"), M2 = Guid.Parse("0198b7ac-0012-7000-8000-000000000012"),
        M3 = Guid.Parse("0198b7ac-0013-7000-8000-000000000013"), Elsewhere = Guid.Parse("0198b7ac-0019-7000-8000-000000000019");

    /// <summary>Review Focus 5: a click on a model already at its wounds left does not count, and Complete is exactly the amount.</summary>
    [Fact]
    public void Add_stops_at_wounds_left()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 3), new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 });
        c.Add(M2); c.Add(M2);
        Assert.Equal(1, c.Counts[M2]);
        Assert.Equal((1, false), (c.Chosen, c.Complete));
        c.Add(M1);
        Assert.False(c.Complete);
        c.Add(M1); c.Add(M1);
        Assert.Equal((2, 3, true), (c.Counts[M1], c.Chosen, c.Complete));
    }

    [Fact]
    public void Nothing_past_the_amount_and_nothing_outside_the_targets()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 1), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1 });
        c.Add(Elsewhere);
        Assert.Equal(0, c.Chosen);
        c.Add(M1); c.Add(M2);
        Assert.Equal((1, true, false), (c.Chosen, c.Complete, c.Counts.ContainsKey(M2)));
    }

    [Fact]
    public void Remove_takes_one_away_and_an_absent_count_is_one_wound()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 2), new Dictionary<Guid, int>());     // no facts: one wound each
        Assert.Equal(2, c.Required);
        c.Add(M1); c.Add(M1);
        Assert.Equal(1, c.Counts[M1]);
        c.Remove(M1); c.Remove(M1); c.Remove(M2);
        Assert.Empty(c.Counts);
        Assert.Null(c.Answer());
    }

    [Fact]
    public void Auto_fills_what_is_left_in_the_ask_s_order()
    {
        var c = new CasualtyPick(new Allocation([M1, M2, M3], 2), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1, [M3] = 1 });
        c.Add(M3);
        c.Auto();
        Assert.Equal(new Dictionary<Guid, int> { [M3] = 1, [M1] = 1 }, c.Counts);
        var answer = Assert.IsType<Allocated>(c.Answer());
        Assert.Equal(new Dictionary<Guid, int> { [M3] = 1, [M1] = 1 }, answer.Counts);
    }

    /// <summary>p.8: one model per wound. More wounds than the offered models can take: every model takes what it can, and the
    /// answer still sums to the amount, the rest on the last target, for the server's assign step to carry over.</summary>
    [Fact]
    public void Wounds_past_every_model_s_capacity_complete_at_capacity_and_answer_the_whole_amount()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 5), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1 });
        Assert.Equal(2, c.Required);
        c.Auto();
        Assert.True(c.Complete);
        var answer = Assert.IsType<Allocated>(c.Answer());
        Assert.Equal(new Dictionary<Guid, int> { [M1] = 1, [M2] = 4 }, answer.Counts);
        Assert.Equal(2, c.Chosen);            // the pick itself still shows what was clicked
    }

    /// <summary>p.15 through the Ask's MaxTargets of one: wounds go on one Tough model, a click on another moves the pick there, and
    /// the surplus past that model's wounds left is answered on it for the server to carry over.</summary>
    [Fact]
    public void Under_a_max_of_one_target_a_click_on_another_model_moves_the_pick_and_the_surplus_stays_on_it()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 4, MaxTargets: 1), new Dictionary<Guid, int> { [M1] = 3, [M2] = 3 });
        Assert.Equal((3, "Assign 4 wounds to one model"), (c.Required, c.Title));
        c.Add(M1); c.Add(M1); c.Add(M2);
        Assert.Equal(new Dictionary<Guid, int> { [M2] = 1 }, c.Counts);
        c.Add(M2); c.Add(M2); c.Add(M2);
        Assert.True(c.Complete);
        Assert.Equal(new Dictionary<Guid, int> { [M2] = 4 }, Assert.IsType<Allocated>(c.Answer()).Counts);
        var auto = new CasualtyPick(new Allocation([M1, M2], 2, MaxTargets: 1), new Dictionary<Guid, int> { [M1] = 3, [M2] = 3 });
        auto.Auto();
        Assert.Equal(new Dictionary<Guid, int> { [M1] = 2 }, auto.Counts);
    }

    /// <summary>A click on a picked model that can take no more puts it back: a one-wound model toggles, a Tough one fills then clears.</summary>
    [Fact]
    public void A_click_on_a_model_that_can_take_no_more_puts_it_back()
    {
        var c = new CasualtyPick(new Allocation([M1, M2, M3], 2), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1, [M3] = 1 });
        c.Click(M1); c.Click(M1);
        Assert.Empty(c.Counts);
        c.Click(M1); c.Click(M2); c.Click(M2);
        Assert.Equal(new Dictionary<Guid, int> { [M1] = 1 }, c.Counts);
        var tough = new CasualtyPick(new Allocation([M1, M2], 3), new Dictionary<Guid, int> { [M1] = 2, [M2] = 3 });
        tough.Click(M1); tough.Click(M1);
        Assert.Equal(2, tough.Counts[M1]);
        tough.Click(M1);
        Assert.Empty(tough.Counts);
        var one = new CasualtyPick(new Allocation([M1, M2], 4, MaxTargets: 1), new Dictionary<Guid, int> { [M1] = 3, [M2] = 3 });
        one.Click(M1); one.Click(M1); one.Click(M1); one.Click(M2);
        Assert.Equal(new Dictionary<Guid, int> { [M2] = 1 }, one.Counts);     // another model still moves the pick
    }

    [Fact]
    public void Clear_puts_every_model_back()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 2), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1 });
        c.Auto();
        c.Clear();
        Assert.Equal((0, false), (c.Chosen, c.Complete));
    }

    /// <summary>p.8: "For each wound that the unit has taken, the defending player must remove one model as a casualty." One-wound
    /// models read as models to remove, counted up to the models there are.</summary>
    [Fact]
    public void One_wound_models_read_as_models_to_remove()
    {
        var c = new CasualtyPick(new Allocation([M1, M2, M3], 7), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1, [M3] = 1 });
        Assert.Equal((true, "Remove 3 models", "0 of 3 chosen"), (c.OneEach, c.Title, c.Status));
        Assert.StartsWith("Click a model to remove it", c.Hint);
        var tough = new CasualtyPick(new Allocation([M1, M2], 2), new Dictionary<Guid, int> { [M1] = 1, [M2] = 3 });
        Assert.Equal((false, "Assign 2 wounds"), (tough.OneEach, tough.Title));
    }
}
