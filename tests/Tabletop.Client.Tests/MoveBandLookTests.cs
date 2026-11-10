using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class MoveBandLookTests
{
    static double Hue(Rgba c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B)), d = max - min;
        var h = max == c.R ? (c.G - c.B) / d % 6 : max == c.G ? (c.B - c.R) / d + 2 : (c.R - c.G) / d + 4;
        return (h * 60 + 360) % 360;
    }

    static double Apart(Rgba a, Rgba b) { var d = Math.Abs(Hue(a) - Hue(b)); return Math.Min(d, 360 - d); }

    [Fact]
    public void The_clear_band_is_the_teams_colour_and_the_difficult_band_a_muted_fainter_one()
    {
        var team = UiThemes.SciFi.Team(2);
        Assert.Equal(team with { A = MoveBandLook.ClearAlpha }, MoveBandLook.Colour(MoveBand.Clear, team));
        var muted = MoveBandLook.Colour(MoveBand.Difficult, team);
        Assert.True(Math.Abs(Hue(muted) - Hue(team)) < 1);                                        // the same hue
        Assert.True(muted.A < MoveBandLook.ClearAlpha);
        Assert.True(Math.Max(muted.R, Math.Max(muted.G, muted.B)) - Math.Min(muted.R, Math.Min(muted.G, muted.B))
            < Math.Max(team.R, Math.Max(team.G, team.B)) - Math.Min(team.R, Math.Min(team.G, team.B)));   // less saturated
    }

    [Fact]
    public void The_dangerous_band_is_striped_in_the_terrains_dangerous_colour_and_reads_apart_from_the_activated_green() => Assert.All(UiThemes.All, t =>
    {
        var danger = MoveBandLook.Colour(MoveBand.Dangerous, t.Team(1));
        Assert.Equal(TerrainEffects.Of(TerrainDeclarations.Dangerous).Colour with { A = MoveBandLook.DangerousAlpha }, danger);
        Assert.Equal(danger, MoveBandLook.Colour(MoveBand.Dangerous, t.Team(2)));                 // whatever the team
        Assert.True(Apart(danger, t.Success) >= 60, $"{t.Id}: {Apart(danger, t.Success):0}° from the success green");
        Assert.True(MoveBandLook.Striped(MoveBand.Dangerous));
        Assert.False(MoveBandLook.Striped(MoveBand.Clear) || MoveBandLook.Striped(MoveBand.Difficult));
    });

    [Fact]
    public void Layers_draw_clear_then_difficult_then_dangerous_for_every_model()
    {
        List<Point2> a = [new(0, 0)], b = [new(1, 0)], c = [new(2, 0)];
        var layers = MoveBandLook.Layers([new MoveBands(a, b, c), new MoveBands(c, a, b)]);
        Assert.Equal([MoveBand.Clear, MoveBand.Difficult, MoveBand.Dangerous], layers.Select(l => l.Band));
        Assert.Equal([a, c], layers[0].Areas);
        Assert.Equal([c, b], layers[2].Areas);
    }
}
