namespace Tabletop.Geometry.Tests;

/// <summary>
/// p.7: "Models may never be within 1” of models from other units, unless they are using a Charge action, and may never move through other models
/// or units (friendly or enemy), even if they are using a Charge action." Owner ruling (2026-11-27): both clauses hold along the whole path. Every
/// unit but the mover's own and a charge's target blocks as its perimeter (the convex hull of its bases), grown by the separation outside a Charge.
/// </summary>
public class MoveThroughUnitsTests
{
    static readonly TableSize Opr = new(72, 48);
    static readonly Guid Mine = Guid.CreateVersion7(), Other = Guid.CreateVersion7(), Third = Guid.CreateVersion7();
    // Two models of another unit 3.2" apart centre to centre (1.94" between the bases): a 1.26" base fits between them.
    static readonly PlacedModel[] Pair = [Fx.Model(10, 19.4), Fx.Model(10, 22.6)];
    static readonly PlacedModel Start = Fx.Model(5, 21);
    static readonly Point2 Beyond = new(15, 21);

    static MoveUnits Units(double? separation = null, Guid? own = null, IReadOnlyList<Guid>? bases = null) =>
        new([Other, Other], own ?? Mine, separation, bases);

    [Fact]
    public void Without_units_a_base_passes_between_two_models()
    {
        var check = Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, Pair);
        Assert.False(check.CrossesModels);
        Assert.Equal(10, check.Distance, 6);
    }

    [Fact]
    public void A_base_cannot_pass_between_two_models_of_another_unit()
    {
        var check = Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, Pair, units: Units());
        Assert.True(check.CrossesModels);
        Assert.False(check.NearModels);
        Assert.Equal(Other, check.Unit);
    }

    [Fact]
    public void A_path_round_the_unit_is_measured_round_it()
    {
        var check = Movement.Check(Start, Beyond, 12, [], [], null, Opr, Pair, units: Units());
        Assert.True(check is { WithinAllowance: true, CrossesModels: false, NearModels: false });
        Assert.InRange(check.Distance, 11.5, 11.7);                                         // over the top model's rounded side: 11.58
        Assert.True(check.Path.Count > 2);
    }

    [Fact]
    public void Outside_a_charge_the_path_keeps_the_separation_from_the_unit()
    {
        var near = Movement.Check(Start, Beyond, 12, [], [], null, Opr, Pair, units: Units(separation: 1));
        Assert.True(near is { NearModels: true, CrossesModels: false });
        Assert.Equal(Other, near.Unit);
        var round = Movement.Check(Start, Beyond, 13.5, [], [], null, Opr, Pair, units: Units(separation: 1));
        Assert.True(round is { WithinAllowance: true, NearModels: false, CrossesModels: false });
        Assert.InRange(round.Distance, 12.85, 13);                                          // 1" farther out: 12.89
    }

    [Fact]
    public void The_movers_own_unit_and_a_charge_target_block_as_bases()
    {
        Assert.Equal(10, Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, Pair, units: Units(own: Other)).Distance, 6);
        var charge = Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, Pair, units: Units(bases: [Other]));
        Assert.True(charge is { CrossesModels: false, NearModels: false });
        Assert.Equal(10, charge.Distance, 6);
    }

    [Fact]
    public void A_model_starting_within_the_separation_is_held_to_the_perimeter()
    {
        var close = Fx.Model(10, 24.3);                                                     // 0.44" from the top model
        Assert.True(Movement.Check(close, new(10, 30), 6, [], [], null, Opr, Pair, units: Units(separation: 1)) is { WithinAllowance: true, NearModels: false, CrossesModels: false });
        Assert.True(Movement.Check(close, new(16, 24.3), 6.5, [], [], null, Opr, Pair, units: Units(separation: 1)) is { WithinAllowance: true, NearModels: false, CrossesModels: false });
        Assert.True(Movement.Check(close, new(10, 16.5), 7.9, [], [], null, Opr, Pair, units: Units(separation: 1)).CrossesModels);
    }

    [Fact]
    public void A_model_starting_inside_the_perimeter_is_held_to_the_bases()
    {
        var inside = Fx.Model(10, 21);                                                      // between the two models
        Assert.True(Movement.Check(inside, Beyond, 6, [], [], null, Opr, Pair, units: Units(separation: 1)) is { WithinAllowance: true, NearModels: false, CrossesModels: false });
        var onBase = Fx.Model(10.5, 22.2);                                                  // on the top model's base
        Assert.True(Movement.Check(onBase, new(10.5, 30), 8, [], [], null, Opr, Pair, units: Units(separation: 1)) is { WithinAllowance: true, CrossesModels: false });
    }

    [Fact]
    public void Each_other_unit_blocks_alone()
    {
        // (15, 24.5) lies inside the hull of all three models, clear of each unit's own.
        PlacedModel[] models = [.. Pair, Fx.Model(20, 30)];
        var from = Fx.Model(12, 27);
        Assert.True(Movement.Check(from, new(15, 24.5), 5, [], [], null, Opr, models, units: new MoveUnits([Other, Other, Third], Mine, 1))
            is { WithinAllowance: true, NearModels: false, CrossesModels: false });
        Assert.True(Movement.Check(from, new(15, 24.5), 5, [], [], null, Opr, models, units: new MoveUnits([Other, Other, Other], Mine, 1)).CrossesModels);
    }

    [Fact]
    public void The_reach_area_goes_round_the_unit()
    {
        Assert.True(MoveReach.Build(Start, 10.5, [], [], null, Opr, Pair).Reaches(Beyond));
        var reach = MoveReach.Build(Start, 10.5, [], [], null, Opr, Pair, units: Units());
        Assert.False(reach.Reaches(Beyond));
        var round = MoveReach.Build(Start, 12, [], [], null, Opr, Pair, units: Units());
        Assert.True(round.Reaches(Beyond));
        Assert.InRange(round.Route(Beyond).Distance, 11.5, 11.7);
        Assert.False(MoveReach.Build(Start, 12, [], [], null, Opr, Pair, units: Units(separation: 1)).Reaches(Beyond));
    }

    [Fact]
    public void A_charge_path_between_two_models_of_another_unit_does_not_reach_the_target()
    {
        var target = Fx.Model(15, 21);
        PlacedModel[] standing = [.. Pair, target];
        var units = new MoveUnits([Other, Other, Third], Mine, null, [Third]);
        Assert.NotNull(Movement.ContactDistance(Start, target, 9.5, [], Opr));
        Assert.Null(Movement.ContactDistance(Start, target, 9.5, [], Opr, standing, units));
        Assert.InRange(Movement.ContactDistance(Start, target, 11, [], Opr, standing, units)!.Value, 9.6, 10.9);
        Assert.False(Movement.ContactInReach(Start, target, 9.5, [], Opr, [], 3, models: standing, units: units));
        Assert.True(Movement.ContactInReach(Start, target, 11, [], Opr, [], 3, models: standing, units: units));
    }

    [Fact]
    public void An_oval_unit_and_a_mixed_unit_block_as_their_hull()
    {
        PlacedModel[] ovals = [Fx.Model(Base.Oval(2.36, 1.38), 10, 19.2), Fx.Model(10, 22.8, diameter: 1.57)];
        Assert.False(Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, ovals).CrossesModels);
        Assert.True(Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, ovals, units: Units()).CrossesModels);
        PlacedModel[] mixed = [Fx.Model(10, 19.4, diameter: 1), Fx.Model(10, 22.6, diameter: 1.57)];
        Assert.True(Movement.Check(Start, Beyond, 10.5, [], [], null, Opr, mixed, units: Units()).CrossesModels);
    }

    static readonly Guid MoverId = Guid.CreateVersion7();
    static ContactBody Body(Guid id, Guid unit, PlacedModel m) => new(id, unit, m.Body, m.Placement);

    [Fact]
    public void Contacts_name_the_unit_a_path_goes_through_or_passes_near()
    {
        ContactBody[] bodies = [Body(MoverId, Mine, Start), Body(Guid.CreateVersion7(), Other, Pair[0]), Body(Guid.CreateVersion7(), Other, Pair[1])];
        Placement To(Point2 p) => new(new Point3(p.X, p.Y, 0), new Yaw(0));
        ContactTerms Terms(double allowance, double? separation) => new(separation, false, true, new ContactMove(allowance, [], [], null), Opr);
        Assert.Equal(new Contact(ContactKind.Crosses, Other), Contacts.Of(bodies, new Dictionary<Guid, Placement> { [MoverId] = To(Beyond) }, [], Terms(10.5, null))[MoverId]);
        Assert.Equal(new Contact(ContactKind.TooClose, Other), Contacts.Of(bodies, new Dictionary<Guid, Placement> { [MoverId] = To(Beyond) }, [], Terms(12, 1))[MoverId]);
        Assert.Empty(Contacts.Of(bodies, new Dictionary<Guid, Placement> { [MoverId] = To(Beyond) }, [], Terms(13, 1)));
        // A landing within the separation reads TooClose, judged before the path.
        Assert.Equal(new Contact(ContactKind.TooClose, Other), Contacts.Of(bodies, new Dictionary<Guid, Placement> { [MoverId] = To(new Point2(11.5, 22.6)) }, [], Terms(10.5, 1))[MoverId]);
        // A charge's target blocks as bases.
        var charge = new ContactTerms(null, false, true, new ContactMove(10.5, [], [], null), Opr, Bases: [Other]);
        Assert.Empty(Contacts.Of(bodies, new Dictionary<Guid, Placement> { [MoverId] = To(Beyond) }, [], charge));
    }
}
