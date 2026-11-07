using Tabletop.Geometry;

namespace Tabletop.Protocol.Tests;

public class WireJsonTests
{
    static void RoundTrips<T>(T value)
    {
        var json = WireJson.Serialize(value);
        var back = WireJson.Deserialize<T>(json);
        Assert.Equal(json, WireJson.Serialize(back));
        Assert.Equal(value!.GetType(), back!.GetType());
    }

    [Fact] public void Messages_round_trip() { foreach (var m in Samples.Messages) RoundTrips<SocketMessage>(m); }
    [Fact] public void Events_round_trip() { foreach (var e in Samples.Events) RoundTrips<EventPayload>(e); }
    [Fact] public void State_changes_round_trip_as_a_list() => RoundTrips<IReadOnlyList<StateChange>>(Samples.Events.OfType<StateChange>().ToList());
    [Fact] public void Commands_round_trip() { foreach (var c in Samples.Commands) RoundTrips<Command>(c); }
    [Fact] public void Edits_round_trip() { foreach (var e in Samples.Edits) RoundTrips<Edit>(e); }
    [Fact] public void Legal_sets_and_answers_round_trip() { foreach (var l in Samples.LegalSets) RoundTrips<LegalSet>(l); foreach (var a in Samples.Answers) RoundTrips<AskAnswer>(a); }

    /// <summary>An enum is read by name only: an integer would name a member no switch handles.</summary>
    [Fact]
    public void An_enum_given_as_an_integer_is_unreadable() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WireJson.Deserialize<Edit>("""{"kind":"set_configuration","name":"x","propertiesHash":"h","schedulerShape":7,"enforcement":"warn","waitsBetweenSteps":false}"""));

    /// <summary>A marker is an objective: no token kind, and no command or event places or removes one in play.</summary>
    [Fact]
    public void Tokens_are_not_on_the_wire()
    {
        const string Token = """{"id":"019a0000-0000-7000-8000-000000000001","kind":"token","position":{"x":1,"y":1,"z":0},"yaw":{"degrees":0},"diameter":1,"visual":{"builtin":"chip"}}""";
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WireJson.Deserialize<Edit>($$"""{"kind":"add_setup_marker","marker":{{Token}}}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WireJson.Deserialize<Command>($$"""{"kind":"place_marker","marker":{{Token}}}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WireJson.Deserialize<Command>("""{"kind":"remove_marker","markerId":"019a0000-0000-7000-8000-000000000001"}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WireJson.Deserialize<EventPayload>("""{"kind":"marker_removed","markerId":"019a0000-0000-7000-8000-000000000001"}"""));
    }

    /// <summary>A measurement's mode is optional: a client that sends none measures horizontally, and one that does names it.</summary>
    [Fact]
    public void A_measurement_s_mode_is_optional()
    {
        var plain = WireJson.Serialize(new PresenceMessage(new Presence(Guid.Empty, null, null, [], new Measurement(new Point3(0, 0, 0), new Point3(6, 0, 0)))));
        Assert.DoesNotContain("mode", plain);
        Assert.Null(((PresenceMessage)WireJson.Deserialize(plain)).Presence.Measurement!.Mode);
        var straight = WireJson.Serialize(new PresenceMessage(new Presence(Guid.Empty, null, null, [], new Measurement(new Point3(0, 0, 0), new Point3(6, 0, 3), MeasureMode.Straight))));
        Assert.Contains("\"mode\":\"straight\"", straight);
        Assert.Equal(MeasureMode.Straight, ((PresenceMessage)WireJson.Deserialize(straight)).Presence.Measurement!.Mode);
    }

    [Fact]
    public void Every_concrete_kind_has_a_sample()
    {
        (Type Base, IEnumerable<object> Samples)[] unions =
        [
            (typeof(SocketMessage), Samples.Messages), (typeof(EventPayload), Samples.Events), (typeof(Command), Samples.Commands),
            (typeof(Edit), Samples.Edits), (typeof(LegalSet), Samples.LegalSets), (typeof(AskAnswer), Samples.Answers),
        ];
        foreach (var (baseType, samples) in unions)
        {
            var sampled = samples.Select(s => s.GetType()).ToHashSet();
            foreach (var t in StateChangeTests.ConcreteSubclasses(baseType))
                Assert.True(sampled.Contains(t), $"no sample for {t.Name}");
        }
    }

    [Fact]
    public void Wire_shape_is_camel_case_with_a_kind_discriminator()
    {
        var json = WireJson.Serialize(new EventsMessage([new GameEvent(1, null, new TurnStarted(Samples.Team1), null)]));
        Assert.StartsWith("{\"kind\":\"events\",\"events\":[{\"seq\":1,\"payload\":{\"kind\":\"turn_started\",\"teamId\":\"", json);
    }

    [Fact]
    public void Reauth_travels_under_its_kind() =>
        Assert.Equal("{\"kind\":\"reauth\",\"reauthId\":\"00000000-0000-0000-0000-000000000000\",\"sessionJwt\":\"jwt\"}",
            WireJson.Serialize(new Reauth(Guid.Empty, "jwt")));

    [Fact]
    public void Sight_and_facts_travel_under_their_kinds()
    {
        Assert.StartsWith("{\"kind\":\"sight_query\",", WireJson.Serialize(new SightQuery(Guid.Empty, Guid.Empty, Guid.Empty)));
        Assert.StartsWith("{\"kind\":\"sight_answer\",", WireJson.Serialize(new SightAnswer(Guid.Empty, [], [])));
        var facts = WireJson.Serialize(new ModelFacts(new Dictionary<Guid, int> { [Guid.Empty] = 3 }));
        Assert.Equal("{\"kind\":\"model_facts\",\"maxWounds\":{\"00000000-0000-0000-0000-000000000000\":3}}", facts);
    }

    [Fact]
    public void A_unit_threat_carries_its_charge_its_shooting_move_and_each_models_range()
    {
        var threats = new Dictionary<Guid, UnitThreat> { [Guid.Empty] = new(null, 0, new Dictionary<Guid, double> { [Guid.Empty] = 24.5 }) };
        var json = WireJson.Serialize(new ModelFacts(new Dictionary<Guid, int>(), null, null, threats));
        Assert.Contains("\"threats\":{\"00000000-0000-0000-0000-000000000000\":{\"shootMove\":0,\"ranges\":{\"00000000-0000-0000-0000-000000000000\":24.5}}}", json);
        var back = Assert.IsType<ModelFacts>(WireJson.Deserialize<SocketMessage>(json));
        Assert.Equal((null, 0.0, 24.5), (back.Threats![Guid.Empty].Charge, back.Threats[Guid.Empty].ShootMove, back.Threats[Guid.Empty].Ranges[Guid.Empty]));
    }

    [Fact]
    public void A_sequence_without_an_outline_writes_no_outline_key()
    {
        var json = WireJson.Serialize<EventPayload>(new SequenceStarted(Guid.Empty, "hold", new SequenceContext(null, [], [], null)));
        Assert.DoesNotContain("outline", json);
    }

    [Fact]
    public void A_roll_carries_its_threshold_basis_and_a_record_written_before_it_reads_none()
    {
        var basis = new ThresholdBasis("Quality", 5, [new ThresholdChange("Reliable", 2, 0), new ThresholdChange("Steady", null, -1)]);
        var with = new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit with { Basis = basis }], []);
        RoundTrips<EventPayload>(with);
        Assert.Contains("\"printed\":5", WireJson.Serialize<EventPayload>(with));
        var old = WireJson.Serialize<EventPayload>(new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []));
        Assert.DoesNotContain("\"basis\"", old);
        Assert.Null(Assert.IsType<StepCompleted>(WireJson.Deserialize<EventPayload>(old)).Rolls[0].Basis);
    }
}
