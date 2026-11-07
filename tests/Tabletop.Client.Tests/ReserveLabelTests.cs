using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A unit the army holds off the table until a property places it says so on its roster row and its card: the property and where it is held.</summary>
public class ReserveLabelTests
{
    static ModelSlot M(string name) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, []);
    static Unit U(string name, ModelSlot m, UnitReserve? reserve = null) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [], [m], reserve);
    static readonly ModelSlot Mother = M("Mother");
    static readonly Unit Brood = U("Brood", Mother);
    static readonly Unit Spawned = U("Spawnlings", M("S"), new UnitReserve("Spawn", Mother.Id));     // held at a model
    static readonly Unit Copy = U("Second wave", M("W"), new UnitReserve("Reinforcement", Brood.Id)); // held at a unit
    static readonly Unit Plain = U("Plain", M("P"));

    static Projection P()
    {
        var game = TableUx.Game;
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(TableUx.P1, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", [Brood, Spawned, Copy, Plain]), Guid.CreateVersion7()))]);
        return new EventApplier(board, game).Projection;
    }

    [Fact]
    public void The_property_and_the_unit_it_is_held_at_whether_a_model_or_a_unit()
    {
        var p = P();
        Assert.Equal("Reserve: Spawn (Brood)", RosterView.ReserveOf(p, p.Units[Spawned.Id]));
        Assert.Equal("Reserve: Reinforcement (Brood)", RosterView.ReserveOf(p, p.Units[Copy.Id]));
        Assert.Null(RosterView.ReserveOf(p, p.Units[Plain.Id]));
        var rows = RosterView.Of(p, TableUx.Game, new Names(TableUx.Game, p), TableUx.T1, new Dictionary<Guid, int>()).Single(t => t.TeamId == TableUx.T1).Rows;
        Assert.Equal([null, "Reserve: Spawn (Brood)", "Reserve: Reinforcement (Brood)", null],
            new[] { Brood.Id, Spawned.Id, Copy.Id, Plain.Id }.Select(id => rows.Single(r => r.UnitId == id).Reserve));
    }

    /// <summary>Placed by its property, the unit is no longer held off the table: no chip.</summary>
    [Fact]
    public void A_placed_unit_drops_it()
    {
        var game = TableUx.Game;
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(TableUx.P1, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", [Brood, Spawned, Copy, Plain]), Guid.CreateVersion7()))]);
        var a = new EventApplier(board, game);
        var spawned = Spawned.Models[0].Id;
        a.ApplyAll([new GameEvent(1, null, new ModelsMoved([new ModelRelocation(spawned, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0))))]), null)]);
        Assert.Null(RosterView.ReserveOf(a.Projection, a.Projection.Units[Spawned.Id]));
        Assert.Equal("Reserve: Reinforcement (Brood)", RosterView.ReserveOf(a.Projection, a.Projection.Units[Copy.Id]));
    }

    [Fact]
    public void The_card_carries_it_as_a_chip()
    {
        var p = P();
        var sheet = UnitSheet.Of(p, new Names(TableUx.Game, p), Spawned.Id, RuleTexts.Empty, new Dictionary<Guid, int>());
        Assert.Contains(new SheetChip(ChipKind.Reserve, "Reserve: Spawn (Brood)"), sheet.Chips);
    }
}
