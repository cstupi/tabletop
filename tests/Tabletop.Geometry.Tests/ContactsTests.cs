namespace Tabletop.Geometry.Tests;

public class ContactsTests
{
    static readonly Guid UnitA = Guid.CreateVersion7(), UnitB = Guid.CreateVersion7();
    static ContactBody Body(Guid unit, double x, double y, double diameter = 1) =>
        new(Guid.CreateVersion7(), unit, new ModelBody(Base.Circle(diameter), 1.5), new Placement(new Point3(x, y, 0), new Yaw(0)));
    static ContactBody Off(ContactBody b) => b with { At = null };
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    static Dictionary<Guid, Placement> To(params (ContactBody M, Placement At)[] moves) => moves.ToDictionary(m => m.M.Id, m => m.At);
    static ContactTerms Terms(double? separation = null, bool passes = true, bool landing = false, ContactMove? move = null, TableSize? size = null, double? unit = null) =>
        new(separation, passes, landing, move, size, unit);

    [Fact]
    public void A_landing_on_a_base_is_OnBase_with_that_unit()
    {
        var mover = Body(UnitA, 10, 10); var other = Body(UnitB, 30, 30);
        var found = Contacts.Of([mover, other], To((mover, At(30.5, 30))), [], Terms());
        Assert.Equal(new Contact(ContactKind.OnBase, UnitB), found[mover.Id]);
    }

    [Fact]
    public void Models_proposed_where_they_stand_have_no_contact_whatever_they_stand_on_or_beside()
    {
        // A move Ask opens with every ghost where its model stands: answered as it stands, even on a base, in a wall or too close.
        var mover = Body(UnitA, 30, 30); var other = Body(UnitB, 30.5, 30); var near = Body(UnitB, 31.6, 30);
        var wall = Fx.Box(30, 30, 4, 4, 0, 3, d: TerrainDeclarations.Impassable);
        var found = Contacts.Of([mover, other, near], To((mover, At(30, 30))), [wall], Terms(separation: 1, passes: false, landing: true, move: new ContactMove(6, [], [], null)));
        Assert.Empty(found);
        var moved = Contacts.Of([mover, other, near], To((mover, At(30, 30.2))), [wall], Terms(separation: 1, passes: false, landing: true, move: new ContactMove(6, [], [], null)));
        Assert.True(moved.ContainsKey(mover.Id));                                                                    // moved a fifth of an inch: judged
    }

    [Fact]
    public void A_landing_in_an_impassable_solid_is_InImpassable()
    {
        var mover = Body(UnitA, 10, 10);
        var wall = Fx.Box(30, 30, 4, 4, 0, 3, d: TerrainDeclarations.Impassable);
        var found = Contacts.Of([mover], To((mover, At(30, 30))), [wall], Terms(landing: true));
        Assert.Equal(new Contact(ContactKind.InImpassable, null), found[mover.Id]);
    }

    [Fact]
    public void Landing_false_ignores_impassable()
    {
        var mover = Body(UnitA, 10, 10);
        var wall = Fx.Box(30, 30, 4, 4, 0, 3, d: TerrainDeclarations.Impassable);
        Assert.Empty(Contacts.Of([mover], To((mover, At(30, 30))), [wall], Terms(landing: false)));
    }

    [Fact]
    public void A_path_only_through_a_model_is_Crosses()
    {
        var mover = Body(UnitA, 10, 10);
        var ahead = Body(UnitB, 12, 10); var up = Body(UnitB, 10, 12); var down = Body(UnitB, 10, 8);
        var move = new ContactMove(3, [], [], null);
        var found = Contacts.Of([mover, ahead, up, down], To((mover, At(13, 10))), [], Terms(passes: false, move: move));
        Assert.Equal(new Contact(ContactKind.Crosses, null), found[mover.Id]);
    }

    [Fact]
    public void A_steep_volume_entered_caps_the_way_round_a_model()
    {
        // Round the line of five is about 7.2", within 8" but over the 6" cap that going onto the hill brings (Movement.SteepEntering).
        var mover = Body(UnitA, 10, 10);
        ContactBody[] line = [Body(UnitB, 12, 8), Body(UnitB, 12, 9), Body(UnitB, 12, 10), Body(UnitB, 12, 11), Body(UnitB, 12, 12)];
        var hill = Fx.Cylinder(14, 10, 1.5, 0, 0.1, VolumeKind.Area, TerrainDeclarations.Steep);
        var open = Contacts.Of([mover, .. line], To((mover, At(14, 10))), [hill], Terms(passes: false, move: new ContactMove(8, [], [], 6)));
        Assert.Empty(open);
        var steep = Contacts.Of([mover, .. line], To((mover, At(14, 10))), [hill], Terms(passes: false, move: new ContactMove(8, [], [], 6, [hill.Id])));
        Assert.Equal(new Contact(ContactKind.Crosses, null), steep[mover.Id]);
    }

    [Fact]
    public void Within_separation_of_another_unit_is_TooClose()
    {
        var mover = Body(UnitA, 10, 10); var other = Body(UnitB, 30, 30);
        var found = Contacts.Of([mover, other], To((mover, At(30, 32))), [], Terms(separation: 3));
        Assert.Equal(new Contact(ContactKind.TooClose, UnitB), found[mover.Id]);
    }

    [Fact]
    public void A_unit_already_that_close_at_the_start_is_left_to_the_server()
    {
        var mover = Body(UnitA, 10, 10); var other = Body(UnitB, 10, 12);
        Assert.Empty(Contacts.Of([mover, other], To((mover, At(10, 11.9))), [], Terms(separation: 3)).Where(kv => kv.Value.Kind == ContactKind.TooClose));
    }

    [Fact]
    public void A_model_answered_where_it_stands_has_no_contact()
    {
        var mover = Body(UnitA, 10, 10); var other = Body(UnitB, 10.5, 10);
        Assert.Empty(Contacts.Of([mover, other], To((mover, At(10, 10))), [], Terms()));
    }
}
