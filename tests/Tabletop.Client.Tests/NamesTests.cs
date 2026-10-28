using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class NamesTests
{
    static Marker MarkerOf(MarkerKind kind, string? label) =>
        new(Guid.CreateVersion7(), kind, new Point3(10, 10, 0), new Yaw(0), 1.57, label, VisualRef.OfBuiltin("objective"));

    [Fact]
    public void An_objective_with_no_label_is_named_by_its_place_in_setup()
    {
        Marker first = MarkerOf(MarkerKind.Objective, null), ridge = MarkerOf(MarkerKind.Objective, "Ridge"), third = MarkerOf(MarkerKind.Objective, " ");
        var board = Samples.Board with { Table = Samples.Board.Table with { Markers = [first, ridge, third] } };
        var names = new Names(Samples.Game, new EventApplier(board, Samples.Game).Projection);

        Assert.Equal("Objective 1", names.Marker(first.Id));
        Assert.Equal("Ridge", names.Marker(ridge.Id));
        Assert.Equal("Objective 3", names.Marker(third.Id));
        Assert.Equal("Objective 1", names.Piece(first.Id));
    }
}
