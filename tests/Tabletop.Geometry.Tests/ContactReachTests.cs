namespace Tabletop.Geometry.Tests;

/// <summary>
/// A base contact landing held to a vertical reach of the target: the landing rests on its open surface nearest the target's level
/// (Collision.RestNear), and is contact only where that rest is within the reach of the target's base.
/// </summary>
public class ContactReachTests
{
    static readonly TableSize Opr = new(72, 48);

    // A target on a pillar its own size, 5" up: every landing beside it rests on the table.
    static readonly WorldVolume Pillar = Fx.Cylinder(19, 10, 0.63, 0, 5);
    static readonly PlacedModel OnPillar = Fx.Model(19, 10, 5);

    // A target on a platform 4" high, 6" square: landings beside it rest on the platform's top.
    static readonly WorldVolume Platform = Fx.Box(19, 10, 6, 6, 0, 4);
    static readonly PlacedModel OnPlatform = Fx.Model(19, 10, 4);

    static readonly (Guid Id, PlacedModel Start)[] Pair = [(Guid.NewGuid(), Fx.Model(10, 10)), (Guid.NewGuid(), Fx.Model(10, 11.5))];

    static FormationTerms Terms(IReadOnlyList<WorldVolume> volumes, double? vertical) =>
        new(12, [], [], null, [], volumes, Opr, [], false, 1, 3, 9, ContactVertical: vertical);

    [Fact]
    public void A_target_above_the_reach_from_every_landing_beside_it_is_not_reached()
    {
        Assert.False(Movement.ContactInReach(Fx.Model(10, 10), OnPillar, 12, [], Opr, [Pillar], 3));
        Assert.Null(UnitFormation.InContact(Pair, [], [OnPillar], Terms([Pillar], 3)));
    }

    [Fact]
    public void A_target_within_the_reach_of_a_landing_beside_it_is_reached()
    {
        Assert.True(Movement.ContactInReach(Fx.Model(10, 10), OnPillar, 12, [], Opr, [Pillar], 6));
        var formed = UnitFormation.InContact(Pair, [], [OnPillar], Terms([Pillar], 6))!;
        Assert.Contains(Pair, p => Distance.EdgeToEdge(Fx.Model(formed[p.Id].X, formed[p.Id].Y, formed[p.Id].Z), OnPillar) <= 2 * UnitFormation.ContactGap);
    }

    [Fact]
    public void A_landing_on_the_targets_floor_reaches_it_and_rests_there()
    {
        Assert.True(Movement.ContactInReach(Fx.Model(10, 10), OnPlatform, 12, [], Opr, [Platform], 3));
        var formed = UnitFormation.InContact(Pair, [], [OnPlatform], Terms([Platform], 3))!;
        var lead = Pair.Select(p => Fx.Model(formed[p.Id].X, formed[p.Id].Y, formed[p.Id].Z)).MinBy(l => Distance.EdgeToEdge(l, OnPlatform))!;
        Assert.Equal(4, lead.Bottom(), 6);
    }

    [Fact]
    public void Without_a_reach_contact_is_judged_in_plan()
    {
        Assert.True(Movement.ContactInReach(Fx.Model(10, 10), OnPillar, 12, [], Opr, [Pillar], double.PositiveInfinity));
        Assert.NotNull(UnitFormation.InContact(Pair, [], [OnPillar], Terms([Pillar], null)));
    }

    [Fact]
    public void A_target_out_of_plan_reach_is_not_reached_whatever_the_height()
    {
        Assert.False(Movement.ContactInReach(Fx.Model(10, 10), Fx.Model(30, 10), 12, [], Opr, [], 3));
    }

    [Fact]
    public void An_abutment_does_not_draw_a_landing_against_a_model_beyond_its_vertical_reach()
    {
        var charger = Fx.Model(10, 10);
        var dropped = new Point2(19 - 1.26 - 0.3, 10);                       // 0.3" short of the pillar's target, on the table
        var flush = Collision.Land(charger, dropped, [OnPillar], [], [Pillar], null, new Abutment([OnPillar], 0.5));
        Assert.InRange(Distance.EdgeToEdge(Fx.Model(flush.X, flush.Y, flush.Z), OnPillar), 0, 1e-6);
        var held = Collision.Land(charger, dropped, [OnPillar], [], [Pillar], null, new Abutment([OnPillar], 0.5, 3));
        Assert.Equal(new Point3(dropped.X, dropped.Y, 0), held);
    }
}
