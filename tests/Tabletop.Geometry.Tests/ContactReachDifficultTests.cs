namespace Tabletop.Geometry.Tests;

/// <summary>
/// A charge's reach to base contact held to the difficult cap (rulebook p.11, 3.6.0 p.12: "If any model in a unit moves in or through Difficult
/// terrain at any point of its move, then all models in the unit may not move more than 6” for that movement"): a path round the difficult ground
/// within the limit, else one through it within the cap.
/// </summary>
public class ContactReachDifficultTests
{
    static readonly TableSize Opr = new(72, 48);

    // A wood 8" square about (14, 24); a model standing inside it.
    static readonly WorldVolume Wood = Fx.Box(14, 24, 8, 8, 0, 3, VolumeKind.Area, TerrainDeclarations.Difficult);
    // A wood 6" deep across the whole table length at x 15..21: no way round.
    static readonly WorldVolume Band = Fx.Box(18, 24, 6, 48, 0, 3, VolumeKind.Area, TerrainDeclarations.Difficult);

    [Fact]
    public void A_model_standing_in_difficult_ground_reaches_no_target_past_the_cap()
    {
        var target = Fx.Model(25, 24);
        Assert.True(Movement.ContactInReach(Fx.Model(14, 24), target, 16, [], Opr, [Wood], 3));   // no cap: 11" away
        Assert.False(Movement.ContactInReach(Fx.Model(14, 24), target, 16, [], Opr, [Wood], 3, [Wood], 6));
    }

    [Fact]
    public void A_model_in_difficult_ground_reaches_a_target_within_the_cap()
    {
        Assert.True(Movement.ContactInReach(Fx.Model(14, 24), Fx.Model(20, 24), 16, [], Opr, [Wood], 3, [Wood], 6));
    }

    [Fact]
    public void A_path_round_the_difficult_ground_within_the_limit_is_not_capped()
    {
        // Along the wood's edge, clear of it: 12.7" to contact, within 16", so the cap does not bind.
        Assert.True(Movement.ContactInReach(Fx.Model(8, 29), Fx.Model(22, 29), 16, [], Opr, [Wood], 3, [Wood], 6));
    }

    [Fact]
    public void Difficult_ground_with_no_way_round_caps_the_reach()
    {
        Assert.False(Movement.ContactInReach(Fx.Model(12, 24), Fx.Model(24, 24), 16, [], Opr, [Band], 3, [Band], 6));
        Assert.True(Movement.ContactInReach(Fx.Model(12, 24), Fx.Model(24, 24), 16, [], Opr, [Band], 3));
    }
}
