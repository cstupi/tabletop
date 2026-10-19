using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>The contact measures a drag shows before the server judges it (GfMoves.ContactBreach, OverlapBreach): a landing on a base, a straight
/// path through a model, a landing within the Ask's Separation of another unit.</summary>
public class ContactCheckTests
{
    static readonly Guid S0 = TableUx.ModelOf(TableUx.Squad), S1 = TableUx.ModelOf(TableUx.Squad, 1), S2 = TableUx.ModelOf(TableUx.Squad, 2),
        Orc = TableUx.ModelOf(TableUx.Orcs), R0 = TableUx.ModelOf(TableUx.Reserve);
    static MoveRegion Region(double? separation, params Guid[] ids) => new(ids, 30, [], [], null, Separation: separation);
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    static Dictionary<Guid, Placement> To(params (Guid Id, Placement At)[] moves) => moves.ToDictionary(m => m.Id, m => m.At);
    static readonly Projection Deployed = TableUx.After(TableUx.Deploy);                      // Squad at (10,10), (12,10), (14,10); the Orc at (40,30); 1" bases

    [Fact]
    public void A_landing_on_another_models_base_overlaps()
    {
        var found = ContactCheck.Move(Deployed, Region(null, S0), To((S0, At(40.5, 30))), []);
        Assert.Equal(new Contact(ContactKind.OnBase, TableUx.Orcs.Id), found[S0]);
    }

    [Fact]
    public void Two_landings_on_each_other_both_overlap()
    {
        var found = ContactCheck.Move(Deployed, Region(null, S0, S1), To((S0, At(20, 20)), (S1, At(20.5, 20))), []);
        Assert.Equal(ContactKind.OnBase, found[S0].Kind);
        Assert.Equal(ContactKind.OnBase, found[S1].Kind);
    }

    [Fact]
    public void A_straight_path_through_a_standing_model_crosses_it_unless_the_region_passes_models()
    {
        Assert.Equal(new Contact(ContactKind.Crosses, null), ContactCheck.Move(Deployed, Region(null, S0), To((S0, At(16, 10))), [])[S0]);   // through S1 and S2
        Assert.Empty(ContactCheck.Move(Deployed, Region(null, S0) with { PassesModels = true }, To((S0, At(16, 10))), []));
        Assert.Empty(ContactCheck.Move(Deployed, Region(null, S0), To((S0, At(10, 16))), []));
    }

    [Fact]
    public void A_landing_within_the_separation_of_another_units_model_is_too_close()
    {
        Assert.Equal(new Contact(ContactKind.TooClose, TableUx.Orcs.Id), ContactCheck.Move(Deployed, Region(1, S0), To((S0, At(38.2, 30))), [])[S0]);
        Assert.Empty(ContactCheck.Move(Deployed, Region(1, S0), To((S0, At(38, 30))), []));                       // exactly 1" apart
        Assert.Empty(ContactCheck.Move(Deployed, Region(null, S0), To((S0, At(38.2, 30))), []));
        Assert.Empty(ContactCheck.Move(Deployed, Region(1, S0, S1), To((S0, At(20, 20)), (S1, At(21.2, 20))), []));   // its own unit
    }

    [Fact]
    public void A_unit_the_mover_already_stood_within_the_separation_of_is_left_to_the_server()
    {
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new(Orc, new OnTable(At(40, 30)), new OnTable(At(15.5, 10)))]));   // half an inch from S2
        Assert.Empty(ContactCheck.Move(p, Region(1, S2), To((S2, At(14.2, 10))), []));
    }

    [Fact]
    public void A_model_answered_where_it_stands_has_not_moved()
    {
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new(Orc, new OnTable(At(40, 30)), new OnTable(At(10.5, 10)))]));   // on S0's base
        Assert.Empty(ContactCheck.Move(p, Region(1, S0), To((S0, At(10, 10))), []));
    }

    [Fact]
    public void A_hand_placement_is_held_to_overlap_only()
    {
        Assert.Equal(new Contact(ContactKind.OnBase, TableUx.Orcs.Id), ContactCheck.Placement(Deployed, To((R0, At(40.5, 30))), [])[R0]);
        Assert.Empty(ContactCheck.Placement(Deployed, To((R0, At(38.2, 30))), []));              // no separation
        Assert.Empty(ContactCheck.Placement(Deployed, To((S0, At(16, 10))), []));                // no path
    }

    [Fact]
    public void A_landing_in_impassable_terrain_is_marked_even_when_the_region_lists_no_wall_to_sweep()
    {
        // p.13 Flying "may move through units and terrain"; p.11 "may not end their move partially overhanging gaps or terrain" (the server's
        // LandsInImpassable, against every impassable volume, whatever the Ask lists), except for an Aircraft (p.13 "when moving and stopping").
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(20, 10, 0), new Yaw(0), 1));
        var flying = Region(null, S0) with { PassesModels = true };
        Assert.Equal(new Contact(ContactKind.InImpassable, null), ContactCheck.Move(Deployed, flying, To((S0, At(20, 10))), [wall])[S0]);
        Assert.Empty(ContactCheck.Move(Deployed, flying, To((S0, At(22, 10))), [wall]));                           // over it
        Assert.Empty(ContactCheck.Move(Deployed, flying with { IgnoresTerrain = true }, To((S0, At(20, 10))), [wall]));
        Assert.Empty(ContactCheck.Placement(Deployed, To((R0, At(20, 10))), [wall]));                              // a hand placement: overlap only
        Assert.Equal("ends in impassable terrain", ContactCheck.Words(new Contact(ContactKind.InImpassable, null), null, _ => ""));
    }

    [Fact]
    public void A_landing_rests_on_the_terrain_before_it_is_measured()
    {
        // A 3" box under the Orc's spot: the landing stands on its top, above the Orc's 1.5" body, so no base overlaps.
        var box = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(40, 30, 0), new Yaw(0), 1));
        Assert.Empty(ContactCheck.Placement(Deployed, To((R0, At(40.5, 30))), [box]));
        Assert.Equal(ContactKind.OnBase, ContactCheck.Placement(Deployed, To((R0, At(40.5, 30))), [])[R0].Kind);
    }

    [Fact]
    public void Words_say_what_each_contact_is()
    {
        string Name(Guid id) => id == TableUx.Orcs.Id ? "Orc Boyz" : "?";
        Assert.Equal("overlaps a base", ContactCheck.Words(new Contact(ContactKind.OnBase, TableUx.Orcs.Id), null, Name));
        Assert.Equal("path crosses a model", ContactCheck.Words(new Contact(ContactKind.Crosses, null), null, Name));
        Assert.Equal("within 1\" of Orc Boyz", ContactCheck.Words(new Contact(ContactKind.TooClose, TableUx.Orcs.Id), 1, Name));
    }
}
