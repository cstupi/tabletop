using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Xunit;

namespace Tabletop.Client.Tests;

public class MoveWarmupTests
{
    [Fact]
    public void The_warm_up_moves_the_first_unit_on_the_table_by_its_models()
    {
        var p = TableUx.After(TableUx.Deploy);                                             // the Squad at (10,10), (12,10), (14,10); the Orc at (40,30)
        var legal = MoveWarmup.Legal(p);
        Assert.NotNull(legal);
        var unit = p.Models[legal.ModelIds[0]].UnitId;
        Assert.All(legal.ModelIds, id => Assert.Equal(unit, p.Models[id].UnitId));
        Assert.All(legal.ModelIds, id => Assert.IsType<OnTable>(p.Models[id].Location));
        Assert.Equal(p.Models.Values.Count(m => m.UnitId == unit && m.Location is OnTable), legal.ModelIds.Count);
        Assert.Equal(MoveWarmup.Allowance, legal.Allowance);
    }

    [Fact]
    public void No_unit_on_the_table_has_no_warm_up()
    {
        Assert.Null(MoveWarmup.Legal(TableUx.After()));
    }

    [Fact]
    public void A_run_measures_one_drag_frame_of_the_move_and_changes_nothing()
    {
        var p = TableUx.After(TableUx.Deploy);
        var before = p.Models.ToDictionary(kv => kv.Key, kv => kv.Value.Location);
        var run = MoveWarmup.Run(p, [], new TableSize(72, 48));
        Assert.NotNull(run);
        Assert.True(run.Value.Gate.Allowed);                                               // 2" of a 6" move, clear of everyone
        Assert.Empty(run.Value.Contacts);
        Assert.Equal(before, p.Models.ToDictionary(kv => kv.Key, kv => kv.Value.Location));
    }
}
