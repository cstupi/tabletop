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
    /// <summary>Spec → Shooting targets: with two targets taken (p.8: up to two), no other unit glows.</summary>
    [Fact]
    public void Two_targets_taken_leave_only_those_two_as_candidates()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        Assert.Equal([T1, T2, T3], t.Candidates);
        t.Aim(T1);
        Assert.Equal([T1, T2, T3], t.Candidates);
        t.PickOnly("Plasma Gun"); t.Aim(T2);
        Assert.Equal([T1, T2], t.Candidates);
        Assert.True(t.Aim(T1));                                         // re-aiming at a taken target stays open
        Assert.Equal([T1, T2, T3], t.Candidates);                       // one target again
    }

    /// <summary>Rifles reach T1 and T2, Pistols only T1 (p.8: a target is valid for a group with a weapon of it in range).</summary>
    static Assignment Split() => new([new AskOption("Rifles", "Rifles", []), new AskOption("Pistols", "Pistols", [])], [T1, T2], 2,
        new Dictionary<string, IReadOnlyList<Guid>> { ["Rifles"] = [T1, T2], ["Pistols"] = [T1] });

    /// <summary>Spec → Shooting targets: a click aims only the picked groups that can shoot the unit; the others keep their aim.</summary>
    [Fact]
    public void A_click_aims_only_the_picked_groups_that_can_shoot_the_unit()
    {
        var t = new TargetAssignment(Split());
        Assert.True(t.Aim(T2));
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T2 }, t.Assigned);                        // the Pistols cannot reach T2
        Assert.Null(t.Answer());
        Assert.True(t.Aim(T1));
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T1, ["Pistols"] = T1 }, t.Assigned);
        t.PickOnly("Rifles"); t.Aim(T2);
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T2, ["Pistols"] = T1 }, t.Assigned);
        Assert.NotNull(t.Answer());
    }

    /// <summary>Spec → Shooting targets: a unit glows only when a picked group can shoot it.</summary>
    [Fact]
    public void A_unit_no_picked_group_can_shoot_is_not_a_candidate()
    {
        var t = new TargetAssignment(Split());
        Assert.Equal([T1, T2], t.Candidates);
        t.PickOnly("Pistols");
        Assert.False(t.CanTarget(T2));
        Assert.Equal([T1], t.Candidates);
        Assert.False(t.Aim(T2));
        Assert.Empty(t.Assigned);
    }

    /// <summary>Spec → Shooting targets: chips read "Rifles ×4", the count of weapons in the group (its pieces).</summary>
    [Fact]
    public void A_chip_names_its_group_and_how_many_weapons_it_holds()
    {
        var rifles = new AskOption("Rifles", "Rifles", [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()]);
        Assert.Equal("Rifles ×4", TargetAssignment.Chip(rifles));
        Assert.Equal("Plasma Gun ×1", TargetAssignment.Chip(new AskOption("Plasma Gun", "Plasma Gun", [Guid.CreateVersion7()])));
    }
}
