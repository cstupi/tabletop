using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A move Ask's Separation: how many moved models end within it of another unit's model.</summary>
public class SeparationGateTests
{
    static readonly Guid S0 = TableUx.ModelOf(TableUx.Squad), S1 = TableUx.ModelOf(TableUx.Squad, 1), S2 = TableUx.ModelOf(TableUx.Squad, 2), Orc = TableUx.ModelOf(TableUx.Orcs);
    static MoveRegion Region(double? separation, params Guid[] ids) => new(ids, 30, [], [], null, Separation: separation);
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));

    [Fact]
    public void A_model_ending_within_the_separation_of_another_units_model_is_counted()
    {
        var p = TableUx.After(TableUx.Deploy);                                               // the Orc at (40,30), 1" bases
        Assert.Equal(1, MoveRuler.TooClose(p, Region(1, S0), new Dictionary<Guid, Placement> { [S0] = At(38.2, 30) }));
        Assert.Equal(0, MoveRuler.TooClose(p, Region(1, S0), new Dictionary<Guid, Placement> { [S0] = At(38, 30) }));   // exactly 1" apart
        Assert.Equal(0, MoveRuler.TooClose(p, Region(null, S0), new Dictionary<Guid, Placement> { [S0] = At(38.2, 30) }));
        Assert.Equal(0, MoveRuler.TooClose(p, Region(1, S0, S1), new Dictionary<Guid, Placement> { [S0] = At(20, 20), [S1] = At(21.2, 20) }));   // its own unit
    }

    [Fact]
    public void A_unit_the_mover_already_stood_within_the_separation_of_is_left_to_the_server()
    {
        var p = TableUx.After(TableUx.Deploy, new ModelsMoved([new(Orc, new OnTable(At(40, 30)), new OnTable(At(15.5, 10)))]));   // half an inch from S2
        Assert.Equal(0, MoveRuler.TooClose(p, Region(1, S2), new Dictionary<Guid, Placement> { [S2] = At(14.2, 10) }));
    }

    [Fact]
    public void Gate_names_models_too_close_and_strict_waits()
    {
        Assert.Equal(new MoveGate(false, "1 model within 1\" of another unit."), MoveRuler.Gate([], Enforcement.Strict, tooClose: 1, separation: 1));
        Assert.Equal(new MoveGate(true, "2 models within 1\" of another unit."), MoveRuler.Gate([], Enforcement.Warn, tooClose: 2, separation: 1));
    }
}
