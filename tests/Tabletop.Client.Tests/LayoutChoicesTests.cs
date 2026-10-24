using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class LayoutChoicesTests
{
    static readonly TableState Sample = Tabletop.Content.Tests.TableStateTests.Sample();

    [Fact]
    public void Builtins_come_first_then_the_accounts()
    {
        var builtin = Sample with { Name = "Hospitable planet", Terrain = [.. Enumerable.Repeat(Sample.Terrain[0], 17)] };
        var saved = new LayoutSummary(Guid.CreateVersion7(), "Mine", new TableSize(72, 48), 3, 0);
        var choices = LayoutChoices.Of([builtin], [saved]);
        Assert.Equal(["Hospitable planet (built-in, 72×48, 17 terrain)", "Mine (72×48, 3 terrain)"], choices.Select(c => c.Label));
        Assert.Same(builtin, choices[0].Builtin);
        Assert.Null(choices[0].SavedId);
        Assert.Null(choices[1].Builtin);
        Assert.Equal(saved.Id, choices[1].SavedId);
    }

    [Fact]
    public void Random_keeps_everything_but_terrain()
    {
        var table = new RandomTable(9, [Sample.Terrain[0] with { Id = Guid.CreateVersion7() }], [], []);
        var draft = Sample with { Terrain = [] };
        var next = LayoutChoices.WithTerrain(draft, table);
        Assert.Same(table.Terrain, next.Terrain);
        Assert.Equal(draft.Id, next.Id);
        Assert.Equal(draft.Name, next.Name);
        Assert.Equal(draft.Size, next.Size);
        Assert.Equal(draft.Texture, next.Texture);
        Assert.Same(draft.Markers, next.Markers);
        Assert.Same(draft.Zones, next.Zones);
    }
}
