using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>The unit coherency a placement's ghosts show, measured as the server measures it: Squad's three 1" bases at (10..14, 10), 1" apart
/// edge to edge; Reserve's one model on the shelf. The rule set's coherency is p.7's 1" and 9", p.11's 3".</summary>
public class CoherencyViewTests
{
    static readonly UnitCoherency Opr = new(1, 3, 9);
    static Placement At(double x, double y, double z = 0) => new(new Point3(x, y, z), new Yaw(0));
    static string Name(Guid id) => id == TableUx.ModelOf(TableUx.Squad, 0) ? "M1" : id == TableUx.ModelOf(TableUx.Squad, 1) ? "M2" : "M3";
    static Guid S(int i) => TableUx.ModelOf(TableUx.Squad, i);
    static CoherencyPicture Of(Dictionary<Guid, Placement> placed, bool judged = true) => CoherencyView.Of(TableUx.After(TableUx.Deploy), placed, Opr, Name, judged);

    [Fact]
    public void Each_ghost_has_a_ring_one_inch_out_from_its_base_edge()
    {
        var picture = Of(new() { [S(2)] = At(13.9, 10) });
        var ring = Assert.Single(picture.Rings);
        Assert.Equal(S(2), ring.ModelId);
        Assert.All(ring.Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.Equal(1.5, q.DistanceTo(new Point2(13.9, 10)), 3));
        Assert.Empty(picture.Lines);
        Assert.Empty(picture.Words);
    }

    // M3 dragged 2.4" from M2's base: out of the chain, measured to M2, the nearest model of it, with the two bases' closest points.
    [Fact]
    public void A_straggler_gets_a_line_to_its_nearest_model_and_words_saying_what_to_do()
    {
        var picture = Of(new() { [S(2)] = At(15.4, 10) });
        var line = Assert.Single(picture.Lines);
        Assert.Equal((S(2), S(1), CoherencyBreak.Chain), (line.ModelId, line.ToId, line.Break));
        Assert.Equal(new Point3(14.9, 10, 0), line.From);
        Assert.Equal(new Point3(12.5, 10, 0), line.To);
        Assert.Equal("2.4\" apart, needs 1\"", line.Text);
        Assert.Equal("M3 out of coherency: move within 1\" of another model", picture.Words[S(2)]);
        Assert.Equal("Out of coherency", MarkWords.Short(picture.Words[S(2)]));
        Assert.True(picture.Blocks);   // the unit was in coherency: the server refuses this (GfMoves.CoherencyBreach)
    }

    // The spread, under a 5" chain so three bases make one chain 10" end to end: M1 and M3 are each measured to the other, the farthest model.
    [Fact]
    public void A_spread_break_gets_a_line_to_the_farthest_model()
    {
        var far = CoherencyView.Of(TableUx.After(TableUx.Deploy), new Dictionary<Guid, Placement> { [S(0)] = At(4, 10), [S(1)] = At(9.5, 10), [S(2)] = At(15, 10) },
            new UnitCoherency(5, 5, 9), Name, judged: true);
        Assert.Equal(new[] { (S(0), S(2)), (S(2), S(0)) }, far.Lines.Select(l => (l.ModelId, l.ToId)));
        Assert.All(far.Lines, l => Assert.Equal(CoherencyBreak.Spread, l.Break));
        Assert.Equal("10.0\" apart, needs 9\"", far.Lines[0].Text);
        Assert.Equal("M1 out of coherency: move within 9\" of M3", far.Words[S(0)]);
    }

    // Deployment ghosts are not judged by the server: the words warn and never hold the placement.
    [Fact]
    public void An_unjudged_placement_only_warns()
    {
        var picture = Of(new() { [S(2)] = At(15.4, 10) }, judged: false);
        Assert.False(picture.Blocks);
        Assert.Empty(CoherencyView.With(new Dictionary<Guid, string>(), picture));
        Assert.Equal(picture.Words[S(2)], CoherencyView.Warnings(new Dictionary<Guid, string>(), picture)[S(2)]);
    }

    // A unit already out of coherency before the move is the server's to judge (regroup, closer): the client only warns.
    [Fact]
    public void A_unit_out_of_coherency_before_the_move_only_warns()
    {
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new ModelRelocation(S(2), new OnTable(At(14, 10)), new OnTable(At(20, 10)))]));
        var picture = CoherencyView.Of(p, new Dictionary<Guid, Placement> { [S(2)] = At(19, 10) }, Opr, Name, judged: true);
        Assert.NotEmpty(picture.Words);
        Assert.False(picture.Blocks);
    }

    [Fact]
    public void A_contact_mark_comes_before_the_coherency_words()
    {
        var picture = Of(new() { [S(2)] = At(15.4, 10) });
        var marks = CoherencyView.With(new Dictionary<Guid, string> { [S(2)] = "overlaps a base" }, picture);
        Assert.Equal("overlaps a base", marks[S(2)]);
    }

    [Fact]
    public void No_coherency_from_the_rule_set_measures_nothing() =>
        Assert.Same(CoherencyPicture.None, CoherencyView.Of(TableUx.After(TableUx.Deploy), new Dictionary<Guid, Placement> { [S(2)] = At(30, 10) }, null, Name, true));
}
