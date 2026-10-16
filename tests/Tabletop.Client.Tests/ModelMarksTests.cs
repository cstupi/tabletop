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
}
