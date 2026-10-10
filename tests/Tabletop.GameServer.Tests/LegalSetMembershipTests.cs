using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class LegalSetMembershipTests
{
    static Ask With(LegalSet legal, bool allowPass = false) => new(Guid.CreateVersion7(), "q", "p", [Samples.Team1], legal, allowPass, false, null, null);
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();

    [Fact] public void Options_by_id() { var ask = With(new Options([new AskOption("x", "X", [])])); Assert.True(LegalSetMembership.Contains(ask, new Chosen("x"))); Assert.False(LegalSetMembership.Contains(ask, new Chosen("y"))); }
    [Fact] public void Pieces_within_bounds_and_eligible()
    {
        var ask = With(new PieceSelection([A, B], 1, 1));
        Assert.True(LegalSetMembership.Contains(ask, new SelectedPieces([A])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([A, B])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([C])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([])));
    }
    [Fact] public void Allocation_sums_to_amount_over_targets()
    {
        var ask = With(new Allocation([A, B], 3));
        Assert.True(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [A] = 2, [B] = 1 })));
        Assert.False(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [A] = 3, [B] = 1 })));
        Assert.False(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [C] = 3 })));
        var one = With(new Allocation([A, B], 3, MaxTargets: 1));
        Assert.True(LegalSetMembership.Contains(one, new Allocated(new Dictionary<Guid, int> { [B] = 3, [A] = 0 })));
        Assert.False(LegalSetMembership.Contains(one, new Allocated(new Dictionary<Guid, int> { [A] = 2, [B] = 1 })));
    }
    [Fact] public void Assignment_covers_every_item_with_at_most_n_distinct_targets()
    {
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", [])], [A, B, C], 2));
        Assert.True(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = B })));
        Assert.False(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A })));
        Assert.False(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = Guid.CreateVersion7() })));
        var one = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", [])], [A, B], 1));
        Assert.False(LegalSetMembership.Contains(one, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = B })));
    }
    [Fact] public void Assignment_gives_each_item_only_a_target_on_its_own_list()
    {
        // p.8: a group's target must be one it can shoot ("has a weapon that is within range of that model"), so Pistols at a unit only the Rifles reach is out of set.
        var ask = With(new Assignment([new AskOption("Rifles", "", []), new AskOption("Pistols", "", [])], [A, B], 2,
            new Dictionary<string, IReadOnlyList<Guid>> { ["Rifles"] = [A, B], ["Pistols"] = [A] }));
        Assert.True(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["Rifles"] = B, ["Pistols"] = A })));
        Assert.True(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["Rifles"] = A, ["Pistols"] = A })));
        Assert.False(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["Rifles"] = B, ["Pistols"] = B })));
    }
    [Fact] public void Move_region_checks_model_ids_only()
    {
        var ask = With(new MoveRegion([Samples.Model], 6, [], [], null));
        Assert.True(LegalSetMembership.Contains(ask, new Destinations([Samples.Deploy])));
        Assert.False(LegalSetMembership.Contains(ask, new Destinations([Samples.Deploy with { ModelId = C }])));
    }
    [Fact] public void Token_spend_by_key_and_per_key_cap_with_no_total_confirmation_and_pass()
    {
        var spend = With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2, [B] = 3 }));
        Assert.True(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 2 })));
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [C] = 1 })));      // C is not a spender
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 3 })));      // over A's own cap
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = -1 })));
        Assert.True(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 2, [B] = 3 })));  // five, over either cap: each entry is within its own and there is no total to violate
        Assert.True(LegalSetMembership.Contains(With(new Confirmation()), new Confirmed()));
        Assert.False(LegalSetMembership.Contains(With(new Confirmation()), new Chosen("x")));
        Assert.True(LegalSetMembership.Contains(With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2 }), allowPass: true), new Passed()));
        Assert.False(LegalSetMembership.Contains(With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2 })), new Passed()));
    }
}
