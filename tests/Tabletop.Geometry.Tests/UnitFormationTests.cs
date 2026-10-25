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
}
