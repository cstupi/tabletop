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
