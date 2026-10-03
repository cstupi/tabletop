using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class EnforcementPolicyTests
{
    [Theory]
    [InlineData(Enforcement.Silent, Legality.OutsideLegalSet, true, false)]
    [InlineData(Enforcement.Warn, Legality.OutsideLegalSet, true, true)]
    [InlineData(Enforcement.Strict, Legality.OutsideLegalSet, false, false)]
    [InlineData(Enforcement.Strict, Legality.Illegal, false, false)]
    [InlineData(Enforcement.Warn, Legality.Illegal, true, true)]
    [InlineData(Enforcement.Silent, Legality.Illegal, true, false)]
    [InlineData(Enforcement.Strict, Legality.Legal, true, false)]
    [InlineData(Enforcement.Strict, Legality.AlwaysAccepted, true, false)]
    [InlineData(Enforcement.Warn, Legality.AlwaysAccepted, true, false)]
    public void Three_positions(Enforcement enforcement, Legality legality, bool accepted, bool warned)
    {
        var verdict = EnforcementPolicy.Decide(enforcement, legality, "unit already activated");
        Assert.Equal(accepted, verdict is Accept);
        Assert.Equal(warned, verdict is Accept { Warning: not null });
        if (verdict is Refuse r) Assert.Equal(legality == Legality.Illegal ? RefusalReason.Illegal : RefusalReason.OutsideLegalSet, r.Reason);
    }

    static readonly Breach Ends = new("mission_length", "4 rounds have been played", new StrictEndsGame([Samples.Team1]));
    static readonly Breach Refuses = new("outside_legal_set", "not an option", new StrictRefuses(RefusalReason.OutsideLegalSet));

    [Fact]
    public void A_breach_takes_the_settings_three_positions()
    {
        Assert.Equal(new Accept(null), EnforcementPolicy.Decide(Enforcement.Silent, Ends));
        Assert.Equal(new Accept("4 rounds have been played"), EnforcementPolicy.Decide(Enforcement.Warn, Ends));
        var end = Assert.IsType<EndGame>(EnforcementPolicy.Decide(Enforcement.Strict, Ends));
        Assert.Equal("4 rounds have been played", end.Reason);
        Assert.Equal([Samples.Team1], end.Winners);

        Assert.Equal(new Accept(null), EnforcementPolicy.Decide(Enforcement.Silent, Refuses));
        Assert.Equal(new Accept("not an option"), EnforcementPolicy.Decide(Enforcement.Warn, Refuses));
        Assert.Equal(new Refuse(RefusalReason.OutsideLegalSet, "not an option"), EnforcementPolicy.Decide(Enforcement.Strict, Refuses));

        foreach (var e in Enum.GetValues<Enforcement>()) Assert.Equal(new Accept(null), EnforcementPolicy.Decide(e, (Breach?)null));
    }

    [Fact]
    public void Legality_never_ends_the_game_under_any_setting()
    {
        foreach (var e in Enum.GetValues<Enforcement>())
            foreach (var l in Enum.GetValues<Legality>())
                Assert.IsNotType<EndGame>(EnforcementPolicy.Decide(e, l, "detail"));
    }

    [Fact]
    public void Legality_maps_to_the_two_generic_rules()
    {
        Assert.Null(EnforcementPolicy.BreachOf(Legality.Legal, "d"));
        Assert.Null(EnforcementPolicy.BreachOf(Legality.AlwaysAccepted, "d"));
        Assert.Equal(new Breach(EnforcementPolicy.OutsideLegalSetRule, "d", new StrictRefuses(RefusalReason.OutsideLegalSet)), EnforcementPolicy.BreachOf(Legality.OutsideLegalSet, "d"));
        Assert.Equal(new Breach(EnforcementPolicy.IllegalRule, "d", new StrictRefuses(RefusalReason.Illegal)), EnforcementPolicy.BreachOf(Legality.Illegal, "d"));
    }

    [Fact]
    public void Ending_the_game_is_game_ended_alone_and_only_at_rest()
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        var payloads = EnforcementPolicy.EndGamePayloads(applier.Projection, "over", [Samples.Team2]);
        var only = Assert.IsType<GameEnded>(Assert.Single(payloads));
        Assert.Equal("over", only.Reason);
        Assert.Equal([Samples.Team2], only.Winners);

        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Samples.Team1], new Confirmation(), false, false, null, null);
        applier.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        Assert.Throws<InvalidOperationException>(() => EnforcementPolicy.EndGamePayloads(applier.Projection, "over", []));

        var framed = new EventApplier(Samples.Board, Samples.Game);
        framed.Apply(new GameEvent(1, null, new SequenceStarted(Guid.CreateVersion7(), "roll-off", new SequenceContext(null, [], [], null)), null));
        Assert.Throws<InvalidOperationException>(() => EnforcementPolicy.EndGamePayloads(framed.Projection, "over", []));
    }
}
