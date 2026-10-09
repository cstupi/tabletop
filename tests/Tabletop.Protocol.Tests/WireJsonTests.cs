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
    public void Sight_and_facts_travel_under_their_kinds()
    {
        Assert.StartsWith("{\"kind\":\"sight_query\",", WireJson.Serialize(new SightQuery(Guid.Empty, Guid.Empty, Guid.Empty)));
        Assert.StartsWith("{\"kind\":\"sight_answer\",", WireJson.Serialize(new SightAnswer(Guid.Empty, [], [])));
        var facts = WireJson.Serialize(new ModelFacts(new Dictionary<Guid, int> { [Guid.Empty] = 3 }));
        Assert.Equal("{\"kind\":\"model_facts\",\"maxWounds\":{\"00000000-0000-0000-0000-000000000000\":3}}", facts);
    }

    [Fact]
    public void A_sequence_without_an_outline_writes_no_outline_key()
    {
        var json = WireJson.Serialize<EventPayload>(new SequenceStarted(Guid.Empty, "hold", new SequenceContext(null, [], [], null)));
        Assert.DoesNotContain("outline", json);
    }
}
