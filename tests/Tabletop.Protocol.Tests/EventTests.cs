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

    [Fact]
    public void A_roll_record_round_trips_its_cover_verdict_modifier_grantor_and_die_triggers()
    {
        var r = new RollRecord(Guid.CreateVersion7(), "defense", 5, [new RollModifier("AP", -4, "Rending")],
            [new Die(6, 2, true, ["Rending"])], null, null, new CoverVerdict(true, 4, 6, "Blast"));
        var back = System.Text.Json.JsonSerializer.Deserialize<RollRecord>(System.Text.Json.JsonSerializer.Serialize(r, WireJson.Options), WireJson.Options)!;
        Assert.Equal(new CoverVerdict(true, 4, 6, "Blast"), back.Cover);
        Assert.Equal("Rending", back.Modifiers[0].Via);
        Assert.Equal(new Die(6, 2, true, ["Rending"]), back.Dice[0]);                     // value equality over the triggers
        Assert.NotEqual(new Die(6, 2, true, ["Furious +1 hit"]), back.Dice[0]);
        var plain = System.Text.Json.JsonSerializer.Serialize(new Die(1, 1, false), WireJson.Options);
        Assert.DoesNotContain("riggered", plain);                                          // absent in a record written without it
    }
}
