namespace Tabletop.Protocol.Tests;

public class EventTests
{
    [Fact]
    public void Every_payload_is_registered_once_including_state_changes()
    {
        var registered = StateChangeTests.Registered(typeof(EventPayload));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(EventPayload)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} is not a [JsonDerivedType] of EventPayload");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        Assert.Equal(registered.Count, StateChangeTests.ConcreteSubclasses(typeof(EventPayload)).Count());
    }

    [Fact]
    public void State_change_kinds_match_on_both_bases()
    {
        var onEvent = StateChangeTests.Registered(typeof(EventPayload));
        foreach (var (type, kind) in StateChangeTests.Registered(typeof(StateChange)))
            Assert.Equal(kind, onEvent[type]);
    }

    [Fact]
    public void Kind_is_the_registered_discriminator()
    {
        var e = new GameEvent(7, null, new TurnStarted(Guid.CreateVersion7()), null);
        Assert.Equal("turn_started", e.Kind);
        Assert.Equal("models_moved", EventKinds.Of(typeof(ModelsMoved)));
        Assert.Contains("undone", EventKinds.All);
    }
}
