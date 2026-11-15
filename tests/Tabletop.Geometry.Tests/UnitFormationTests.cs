namespace Tabletop.Geometry.Tests;

public class UnitFormationTests
{
    static readonly TableSize Opr = new(72, 48);
    static FormationTerms Terms(double allowance, IReadOnlyList<WorldVolume>? impassable = null, IReadOnlyList<PlacedModel>? standing = null) =>
        new(allowance, impassable ?? [], [], 6, [], impassable ?? [], Opr, standing ?? [], false, 1, 3, 9);

    static readonly (Guid Id, PlacedModel Start)[] Pair = [(Guid.NewGuid(), Fx.Model(10, 10)), (Guid.NewGuid(), Fx.Model(10, 11.5))];

    [Fact]
    public void A_unit_in_reach_forms_up_with_one_model_in_base_contact_and_the_rest_in_coherency()
    {
        var target = Fx.Model(19, 10);
        var formed = UnitFormation.InContact(Pair, [], [target], Terms(12))!;
        var landed = Pair.Select(p => Fx.Model(formed[p.Id].X, formed[p.Id].Y)).ToList();
        Assert.Contains(landed, l => Distance.EdgeToEdge(l, target) <= 2 * UnitFormation.ContactGap);
        Assert.True(Coherency.Check(landed, 1, 3, 9).Coherent);
        Assert.All(Pair, p => Assert.True(p.Start.Placement.Position.Horizontal.DistanceTo(formed[p.Id]) <= 12 + 1e-6));
    }

    [Fact]
    public void Contact_out_of_reach_or_behind_a_wall_with_no_way_round_gives_nothing()
    {
        var target = Fx.Model(19, 10);
        Assert.Null(UnitFormation.InContact(Pair, [], [target], Terms(5)));
        var wall = Fx.Box(15, 10, 1, 40, 0, 4, d: TerrainDeclarations.Impassable);
        Assert.Null(UnitFormation.InContact(Pair, [], [target], Terms(12, [wall])));
    }

    /// <summary>p.7: a model out of coherency "must use an action so that the model gets back into coherency". Regroup leaves the unit's largest
    /// chain where it stands and lands each model outside it beside the chain, inside its own move.</summary>
    [Fact]
    public void A_straggler_in_reach_rejoins_the_chain_and_the_chain_stays()
    {
        (Guid Id, PlacedModel Start)[] unit = [(Guid.NewGuid(), Fx.Model(10, 10)), (Guid.NewGuid(), Fx.Model(10, 11.5)), (Guid.NewGuid(), Fx.Model(10, 17))];
        Assert.False(Coherency.Check(unit.Select(u => u.Start).ToList(), 1, 3, 9).Coherent);
        var formed = UnitFormation.Regroup(unit, [], Terms(6))!;
        Assert.Equal((unit[0].Start.Placement.Position.Horizontal, unit[1].Start.Placement.Position.Horizontal), (formed[unit[0].Id], formed[unit[1].Id]));
        Assert.True(Coherency.Check(unit.Select(u => Fx.Model(formed[u.Id].X, formed[u.Id].Y)).ToList(), 1, 3, 9).Coherent);
        Assert.True(unit[2].Start.Placement.Position.Horizontal.DistanceTo(formed[unit[2].Id]) <= 6 + 1e-6);
        Assert.True(UnitFormation.CanClose(unit, [], Terms(6)));
    }

    [Fact]
    public void A_straggler_out_of_reach_cannot_regroup_but_can_close_the_gap()
    {
        (Guid Id, PlacedModel Start)[] unit = [(Guid.NewGuid(), Fx.Model(10, 10)), (Guid.NewGuid(), Fx.Model(10, 11.5)), (Guid.NewGuid(), Fx.Model(10, 30))];
        Assert.Null(UnitFormation.Regroup(unit, [], Terms(6)));
        Assert.True(UnitFormation.CanClose(unit, [], Terms(6)));
        Assert.False(UnitFormation.CanClose(unit, [], Terms(0)));                                // no move, no closing
        var before = Coherency.Shortfall(unit.Select(u => u.Start).ToList(), 1, 3, 9);
        var after = Coherency.Shortfall([unit[0].Start, unit[1].Start, Fx.Model(10, 25)], 1, 3, 9);
        Assert.True(after < before && before > 0);
        Assert.Equal(0, Coherency.Shortfall([unit[0].Start, unit[1].Start], 1, 3, 9));
    }
}
