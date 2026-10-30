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
