using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class TargetAssignmentTests
{
    static readonly Guid T1 = Guid.Parse("0198b7ac-0001-7000-8000-000000000001"), T2 = Guid.Parse("0198b7ac-0002-7000-8000-000000000002"),
        T3 = Guid.Parse("0198b7ac-0003-7000-8000-000000000003"), NotOffered = Guid.Parse("0198b7ac-0009-7000-8000-000000000009");
    static Assignment Legal(params string[] groups) => new(groups.Select(g => new AskOption(g, g, [])).ToList(), [T1, T2, T3], 2);

    [Fact]
    public void Every_group_starts_picked_and_one_click_aims_them_all()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        Assert.Equal(["Rifles", "Plasma Gun"], t.Groups.Select(g => g.Id));
        Assert.Equal(new HashSet<string> { "Rifles", "Plasma Gun" }, t.Picked);
        Assert.Null(t.Answer());
        Assert.True(t.Aim(T1));
        var answer = Assert.IsType<Assigned>(t.Answer());
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T1, ["Plasma Gun"] = T1 }, answer.Targets);
    }

    [Fact]
    public void A_chip_picks_its_group_alone_for_a_second_target()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        t.Aim(T1);
        t.PickOnly("Plasma Gun");
        Assert.Equal(new HashSet<string> { "Plasma Gun" }, t.Picked);
        Assert.True(t.Aim(T2));
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T1, ["Plasma Gun"] = T2 }, t.Assigned);
        Assert.True(t.CanTarget(T3));      // the plasma gun's target can still move: Rifles and the new one make two
    }

    [Fact]
    public void Two_targets_taken_and_no_other_unit_glows()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun", "Grenades"));
        t.Aim(T1);
        t.PickOnly("Plasma Gun"); t.Aim(T2);
        t.PickOnly("Grenades");
        Assert.False(t.CanTarget(T3));
        Assert.False(t.Aim(T3));
        Assert.True(t.CanTarget(T1)); Assert.True(t.CanTarget(T2));
        Assert.Equal(T1, t.Assigned["Grenades"]);    // unchanged by the refused aim
    }

    [Fact]
    public void A_unit_the_ask_did_not_offer_is_never_a_target()
    {
        var t = new TargetAssignment(Legal("Rifles"));
        Assert.False(t.CanTarget(NotOffered));
        Assert.False(t.Aim(NotOffered));
        Assert.Empty(t.Assigned);
    }

    [Fact]
    public void Every_group_needs_a_target_before_it_fires_and_clear_starts_over()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        t.PickOnly("Rifles"); t.Aim(T1);
        Assert.Null(t.Answer());
        t.Clear();
        Assert.Empty(t.Assigned);
        Assert.Equal(new HashSet<string> { "Rifles", "Plasma Gun" }, t.Picked);
    }
}
