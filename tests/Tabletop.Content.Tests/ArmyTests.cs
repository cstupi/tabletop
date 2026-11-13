using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class ArmyTests
{
    public static Army Sample()
    {
        var rifle = new Weapon(Guid.CreateVersion7(), "Heavy Rifle",
            new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" },
            [new PropertyInstance("AP", 1)]);
        var slot = new ModelSlot(Guid.CreateVersion7(), 1, "Battle Brother",
            new Dictionary<string, string>(), [new PropertyInstance("Tough", 3)],
            Base.Circle(1.26), 1.5, null, [rifle]);
        var unit = new Unit(Guid.CreateVersion7(), 1, "Battle Brothers",
            new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "3+" },
            [new PropertyInstance("Fearless", null)], [slot]);
        return new Army(Guid.CreateVersion7(), "Strike Force", "grimdark-future", "https://army-forge.onepagerules.com/share?id=x", [unit]);
    }

    [Fact]
    public void A_copy_takes_the_bodies_of_the_unit_it_copies_and_another_reserve_keeps_its_own()
    {
        var mesh = Guid.CreateVersion7();
        var army = Sample();
        var of = army.Units[0] with { Models = [army.Units[0].Models[0] with { AssetId = mesh, Base = Base.Circle(2), Height = 3 }] };
        Unit Reserve(string name) => of with { Id = Guid.CreateVersion7(), Name = name, Reserve = new UnitReserve("Rule", of.Id),
            Models = [of.Models[0] with { Id = Guid.CreateVersion7(), AssetId = null, Base = Base.Circle(1), Height = 1 }] };
        var copy = Reserve(of.Name); var spawn = Reserve("Spawned Thing");
        var bodied = (army with { Units = [of, copy, spawn] }).WithCopiesBodied();
        Assert.Equal((mesh, Base.Circle(2), 3.0), (bodied.Units[1].Models[0].AssetId!.Value, bodied.Units[1].Models[0].Base, bodied.Units[1].Models[0].Height));
        Assert.Equal(copy.Models[0].Id, bodied.Units[1].Models[0].Id);
        Assert.Null(bodied.Units[2].Models[0].AssetId);
        Assert.Same(of, bodied.Units[0]);
    }

    [Fact]
    public void An_army_is_3_5_unless_stamped() { Assert.Equal("3.5", Sample().Edition); Assert.Equal("3.6", (Sample() with { Edition = ArmyEditions.V36 }).Edition); }
    [Fact] public void A_snapshot_keeps_the_armys_edition() => Assert.Equal("3.6", ArmySnapshot.Of(Sample() with { Edition = "3.6" }, Guid.NewGuid()).Edition);
    [Fact] public void A_snapshot_written_before_editions_reads_as_3_5()
        => Assert.Equal("3.5", ContentJson.Deserialize<ArmySnapshot>("""{"id":"00000000-0000-0000-0000-000000000001","name":"a","gameSystem":"grimdark-future","units":[]}""")!.Edition);

    [Fact]
    public void Snapshot_copies_units_and_has_no_army_id()
    {
        var army = Sample();
        var id = Guid.CreateVersion7();
        var snapshot = ArmySnapshot.Of(army, id);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(army.Name, snapshot.Name);
        Assert.Equal(army.GameSystem, snapshot.GameSystem);
        Assert.Same(army.Units, snapshot.Units);
        Assert.DoesNotContain(typeof(ArmySnapshot).GetProperties(), p => p.Name is "ArmyId" or "Source");
    }
}
