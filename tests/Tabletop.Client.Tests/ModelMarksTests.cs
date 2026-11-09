using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class ModelMarksTests
{
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), C = TableUx.ModelOf(TableUx.Squad, 2), Orc = TableUx.ModelOf(TableUx.Orcs);

    [Fact]
    public void A_multi_wound_model_shows_its_wounds_left_and_a_token_holder_its_tokens_above_its_height()
    {
        var p = TableUx.After(TableUx.Deploy, new WoundsSet(B, 1), new SpellTokensSet(C, 2), new SpellTokensSet(TableUx.ModelOf(TableUx.Reserve), 3));
        var dots = ModelMarks.Dots(p, new Dictionary<Guid, int> { [A] = 3, [B] = 3 });                 // Reserve is on the shelf: no dots
        Assert.Equal(new ModelDots[] { new(A, new Point3(10, 10, 1.5), 3, 0), new(B, new Point3(12, 10, 1.5), 2, 0), new(C, new Point3(14, 10, 1.5), 0, 2) },
            dots.OrderBy(d => d.Anchor.X));
    }

    [Fact]
    public void Every_model_on_the_table_of_a_unit_with_the_condition_wears_the_ring()
    {
        Assert.Empty(ModelMarks.Ringed(TableUx.After(TableUx.Deploy), "shaken"));
        var unit = TableUx.After(TableUx.Deploy, new ConditionSet(new Subject(SubjectKind.Unit, TableUx.Squad.Id), "shaken", true));
        Assert.Equal(new[] { A, B, C }.Order(), ModelMarks.Ringed(unit, "shaken").Order());
        var model = TableUx.After(TableUx.Deploy, new ConditionSet(new Subject(SubjectKind.Model, Orc), "shaken", true));
        Assert.Equal(new[] { Orc }, ModelMarks.Ringed(model, "shaken"));
        var cleared = TableUx.After(TableUx.Deploy, new ConditionSet(new Subject(SubjectKind.Unit, TableUx.Squad.Id), "shaken", true),
            new ConditionSet(new Subject(SubjectKind.Unit, TableUx.Squad.Id), "shaken", false));
        Assert.Empty(ModelMarks.Ringed(cleared, "shaken"));
    }

    [Fact]
    public void The_star_ring_runs_from_just_outside_the_base_to_spikes_beyond_it()
    {
        var ring = ModelMarks.StarRing(Base.Circle(1), 8);
        Assert.Equal(16, ring.Count);
        static double R(Point2 q) => Math.Sqrt(q.X * q.X + q.Y * q.Y);
        Assert.All(ring, s => Assert.InRange(R(s.Inner), 0.5, 0.6));
        Assert.Equal(0.5 + ModelMarks.Spike, R(ring[0].Outer), 6);                      // even samples are tips
        Assert.Equal(0.5 + ModelMarks.Valley, R(ring[1].Outer), 6);                     // odd ones the valleys between
        var oval = ModelMarks.StarRing(Base.Oval(4, 2), 8);                             // 4 long along the facing (y), 2 wide (x)
        Assert.Equal(1 + ModelMarks.Spike, oval[0].Outer.X, 6);
        Assert.Equal(2 + ModelMarks.Spike, oval[4].Outer.Y, 6);
    }

    [Fact]
    public void Every_model_on_the_table_of_a_unit_that_acted_this_round_wears_the_activated_band_on_both_teams_until_the_round_turns()
    {
        Assert.Empty(ModelMarks.Activated(TableUx.After(TableUx.Deploy)));
        var squad = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "advance"));
        Assert.Equal(new[] { A, B, C }.Order(), ModelMarks.Activated(squad).Order());
        var both = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "advance"), new ActionRecorded(TableUx.Orcs.Id, "charge"));
        Assert.Equal(new[] { A, B, C, Orc }.Order(), ModelMarks.Activated(both).Order());
        var turned = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "advance"), new ActionRecorded(TableUx.Orcs.Id, "charge"),
            new ActionRecordCleared(TableUx.Squad.Id), new ActionRecordCleared(TableUx.Orcs.Id));
        Assert.Empty(ModelMarks.Activated(turned));
    }

    [Fact]
    public void A_shelved_or_removed_model_of_an_activated_unit_wears_no_band()
    {
        var removed = new ModelsMoved([new ModelRelocation(B, new OnTable(new Placement(new Point3(12, 10, 0), new Yaw(0))), new OnShelf())]);
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "hold"), new ActionRecorded(TableUx.Reserve.Id, "hold"), removed);
        Assert.Equal(new[] { A, C }.Order(), ModelMarks.Activated(p).Order());
    }

    [Fact]
    public void The_activated_band_follows_the_base_just_outside_its_outline()
    {
        static double R(Point2 q) => Math.Sqrt(q.X * q.X + q.Y * q.Y);
        var round = ModelMarks.Band(Base.Circle(1));
        Assert.Equal(ModelMarks.BandSegments, round.Count);
        Assert.All(round, s => Assert.Equal(0.5 + ModelMarks.BandGap, R(s.Inner), 6));
        Assert.All(round, s => Assert.Equal(0.5 + ModelMarks.BandGap + ModelMarks.BandWidth, R(s.Outer), 6));
        var oval = ModelMarks.Band(Base.Oval(4, 2));                                   // 4 long along the facing (y), 2 wide (x)
        Assert.Equal(1 + ModelMarks.BandGap, oval[0].Inner.X, 6);
        Assert.Equal(2 + ModelMarks.BandGap + ModelMarks.BandWidth, oval[ModelMarks.BandSegments / 4].Outer.Y, 6);
        var square = ModelMarks.Band(Base.Rect(2, 3));                                 // 2 long (y), 3 wide (x): the band is a square-cornered frame
        Assert.Equal(4, square.Count);
        var (g, w) = (ModelMarks.BandGap, ModelMarks.BandGap + ModelMarks.BandWidth);
        Assert.Equal(new RingSample(new Point2(1.5 + g, 1 + g), new Point2(1.5 + w, 1 + w)), square[0]);
        Assert.Equal(new RingSample(new Point2(-1.5 - g, -1 - g), new Point2(-1.5 - w, -1 - w)), square[2]);
        Assert.True(ModelMarks.BandGap > 0.04);                                         // clear of the 0.08" base ring on the outline
    }

    [Fact]
    public void The_band_goes_round_a_mesh_base_wider_than_the_declared_base_and_never_inside_the_declared_one()
    {
        var oval = Base.Oval(2.36, 1.38);                                              // the declared 60 x 35 mm oval
        Assert.Equal(oval, ModelMarks.BandBase(oval, 0.6, 1.1));                       // the mesh within the base: the base
        Assert.Equal(Base.Oval(2.95, 1.81), ModelMarks.BandBase(oval, 0.905, 1.475)); // the mesh's own base overhangs: round it
        Assert.Equal(Base.Oval(2.6, 1.38), ModelMarks.BandBase(oval, 0.5, 1.3));       // longer only
        Assert.Equal(Base.Circle(1.6), ModelMarks.BandBase(Base.Circle(1.37), 0.8, 0.7));   // a circle stays a circle, as wide as the wider side
        Assert.Equal(Base.Rect(2, 3.2), ModelMarks.BandBase(Base.Rect(2, 3), 1.6, 0.9));
    }
}
