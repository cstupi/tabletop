using System.Numerics;

namespace Tabletop.TerrainMeshes.Tests;

public class BakeTests
{
    static readonly Vector4 Grey = Paint.Rgb(0.5f, 0.5f, 0.5f);
    static float Value(Vector4 c) => (c.X + c.Y + c.Z) / 3;

    [Fact]
    public void Occlusion_washes_darker_and_warmer()
    {
        var open = Bake.Finish(Grey, 0, 0, 0, false); var deep = Bake.Finish(Grey, 0.8f, 0, 0, false);
        Assert.True(Value(deep) < Value(open) * 0.7f);
        Assert.True(deep.X > deep.Z, "a brown wash, not a grey one");
    }

    /// <summary>Light passes through leaves: a canopy's inside is washed less than a stone recess as deep.</summary>
    [Fact]
    public void Foliage_is_washed_lighter_than_stone() =>
        Assert.True(Value(Bake.Finish(Grey, 0.8f, 0, 0, true)) > Value(Bake.Finish(Grey, 0.8f, 0, 0, false)) + 0.05f);

    [Fact]
    public void A_convex_edge_is_drybrushed_paler_and_foliage_less_so()
    {
        var flat = Bake.Finish(Grey, 0, 0, 0, false);
        var edge = Bake.Finish(Grey, 0, 1, 0, false); var leaf = Bake.Finish(Grey, 0, 1, 0, true);
        Assert.True(Value(edge) > Value(flat) + 0.1f);
        Assert.True(Value(leaf) > Value(flat) && Value(leaf) < Value(edge));
    }

    /// <summary>A small post on the table: its foot sits in the corner with the table and is darker than its top; its top's rim is a convex edge.</summary>
    [Fact]
    public void A_post_is_dark_at_its_foot_and_pale_on_its_top_rim()
    {
        var model = new Model("post", round: false, new Vector3(2, 2, 2));
        var m = new Material("stone", 0.9f);
        Shapes.Box(model[m], new Vector3(-0.1f, 0, -0.1f), new Vector3(0.1f, 0.25f, 0.1f), Grey);   // a brush-wide post: its rim takes the drybrush
        Bake.Apply(model, model.Inches);
        var part = model[m];
        var sides = Enumerable.Range(0, part.Positions.Count).Where(i => MathF.Abs(part.Normals[i].Y) < 0.1f).ToList();
        var foot = sides.Where(i => part.Positions[i].Y < 0.01f).Average(i => Value(part.Colours[i]));
        var head = sides.Where(i => part.Positions[i].Y > 0.12f).Average(i => Value(part.Colours[i]));
        Assert.True(foot < head - 0.08f, $"foot {foot}, head {head}");
        Assert.True(head > 0.5f, "the top rim is drybrushed");
    }
}
