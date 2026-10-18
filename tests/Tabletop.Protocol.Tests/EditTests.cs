namespace Tabletop.Protocol.Tests;

public class EditTests
{
    [Fact]
    public void Every_edit_is_registered_once()
    {
        var registered = StateChangeTests.Registered(typeof(Edit));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(Edit)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} missing");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
    }

    [Fact]
    public void Lobby_and_setup_families_partition_the_edits()
    {
        var all = StateChangeTests.ConcreteSubclasses(typeof(Edit)).ToList();
        Assert.All(all, t => Assert.True(t.IsSubclassOf(typeof(LobbyEdit)) ^ t.IsSubclassOf(typeof(SetupEdit))));
        Assert.Equal(7, all.Count(t => t.IsSubclassOf(typeof(LobbyEdit))));
    }

    [Fact]
    public void A_ping_is_not_a_presence_field() =>
        Assert.DoesNotContain(typeof(Presence).GetProperties(), p => p.PropertyType == typeof(Ping));
}
