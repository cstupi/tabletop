using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using Tabletop.GameServer.Log;

namespace Tabletop.GameServer.Tests;

public class EventFilterTests
{
    static readonly Guid Sealed = Guid.CreateVersion7(), Open = Guid.CreateVersion7();
    /// <summary>Two model ids out of Samples, so the spend maps are concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static GameEvent Ev(long seq, EventPayload p) => new(seq, null, p, null);
    static readonly GameEvent[] Log =
    [
        Ev(1, new AskOpened(new Ask(Sealed, AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame))),
        Ev(2, new AskOpened(new Ask(Open, AskQuestions.Activate, "Activate", [Samples.Team1], new Options([]), true, false, null, null))),
        Ev(3, new AskAnswered(Sealed, Samples.Team1, SpentByTeam1, false)),
        Ev(4, new AskAnswered(Open, Samples.Team1, new Passed(), false)),
        Ev(5, new AskAnswered(Sealed, Samples.Team2, SpentByTeam2, false)),
        Ev(6, new AskClosed(Sealed, [new TeamAnswer(Samples.Team1, SpentByTeam1), new TeamAnswer(Samples.Team2, SpentByTeam2)])),
    ];

    [Fact]
    public void A_sealed_answer_is_withheld_from_other_teams_while_the_window_is_open()
    {
        var forTeam2 = EventFilter.ForViewer(Log[..5], Samples.Team2, new HashSet<Guid>());
        Assert.Null(((AskAnswered)forTeam2[2].Payload).Answer);        // team 1's spend, hidden from team 2
        Assert.NotNull(((AskAnswered)forTeam2[3].Payload).Answer);     // the unsealed ask is never withheld
        Assert.NotNull(((AskAnswered)forTeam2[4].Payload).Answer);     // team 2's own spend
        var forTeam1 = EventFilter.ForViewer(Log[..5], Samples.Team1, new HashSet<Guid>());
        Assert.NotNull(((AskAnswered)forTeam1[2].Payload).Answer);
        Assert.Null(((AskAnswered)forTeam1[4].Payload).Answer);
        var spectator = EventFilter.ForViewer(Log[..5], null, new HashSet<Guid>());
        Assert.Null(((AskAnswered)spectator[2].Payload).Answer);
        Assert.Null(((AskAnswered)spectator[4].Payload).Answer);
    }

    [Fact]
    public void A_replayed_log_withholds_in_place_and_reveals_through_ask_closed()
    {
        var replayed = EventFilter.ForViewer(Log, Samples.Team2, new HashSet<Guid>());
        Assert.Null(((AskAnswered)replayed[2].Payload).Answer);                 // still hidden where it happened
        Assert.Equal(2, ((AskClosed)replayed[5].Payload).Answers.Count);      // revealed where the window closed
        Assert.Equal(WireJson.Serialize(Log[5]), WireJson.Serialize(replayed[5]));
    }

    [Fact]
    public void A_live_batch_uses_the_asks_open_before_it()
    {
        var batch = new[] { Ev(7, new AskAnswered(Sealed, Samples.Team1, SpentByTeam1, false)) };
        Assert.Null(((AskAnswered)EventFilter.ForViewer(batch, Samples.Team2, new HashSet<Guid> { Sealed })[0].Payload).Answer);
        Assert.NotNull(((AskAnswered)EventFilter.ForViewer(batch, Samples.Team2, new HashSet<Guid>())[0].Payload).Answer);
    }

    [Fact]
    public void SealedOpenAsks_extracts_sealed_ask_ids_from_projection()
    {
        var sealedAskId = Guid.CreateVersion7();
        var openAskId = Guid.CreateVersion7();
        var closedSealedAskId = Guid.CreateVersion7();
        var events = new[]
        {
            Ev(1, new AskOpened(new Ask(sealedAskId, AskQuestions.SpendTokens, "Sealed spend", [Samples.Team1], new TokenSpend(new Dictionary<Guid, int> { [Samples.Model] = 2 }), true, true, null, null))),
            Ev(2, new AskOpened(new Ask(openAskId, AskQuestions.Activate, "Open activate", [Samples.Team1], new Options([]), true, false, null, null))),
            Ev(3, new AskOpened(new Ask(closedSealedAskId, AskQuestions.Activate, "Closed sealed", [Samples.Team1], new Options([]), true, true, null, null))),
            Ev(4, new AskClosed(closedSealedAskId, [new TeamAnswer(Samples.Team1, new Passed())])),
        };

        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(events);
        var sealedAsks = EventFilter.SealedOpenAsks(applier.Projection);

        Assert.Contains(sealedAskId, sealedAsks);
        Assert.DoesNotContain(openAskId, sealedAsks);
        Assert.DoesNotContain(closedSealedAskId, sealedAsks);
    }

    /// <summary>An Undone discards every event with Seq &gt; ToSeq (EventApplier.EffectiveLog), so undoing past an ask_closed
    /// puts the sealed window back: the answers that follow are withheld again.</summary>
    [Fact]
    public void Undoing_past_an_ask_closed_reopens_the_sealed_window()
    {
        GameEvent[] log =
        [
            Ev(1, new AskOpened(new Ask(Sealed, AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame))),
            Ev(2, new AskAnswered(Sealed, Samples.Team1, SpentByTeam1, false)),
            Ev(3, new AskClosed(Sealed, [new TeamAnswer(Samples.Team1, SpentByTeam1)])),
            Ev(4, new Undone(2)),
            Ev(5, new AskAnswered(Sealed, Samples.Team2, SpentByTeam2, false)),
        ];
        Assert.Null(((AskAnswered)EventFilter.ForViewer(log, Samples.Team1, new HashSet<Guid>())[4].Payload).Answer);      // team 2's spend, still sealed from team 1
        Assert.NotNull(((AskAnswered)EventFilter.ForViewer(log, Samples.Team2, new HashSet<Guid>())[4].Payload).Answer);   // the answering team always sees its own
        Assert.Null(((AskAnswered)EventFilter.ForViewer(log, null, new HashSet<Guid>())[4].Payload).Answer);               // and a spectator sees none of it
    }

    /// <summary>The mirror: an undone sealed ask_opened is not open any more, so nothing it would have sealed is withheld.</summary>
    [Fact]
    public void Undoing_past_a_sealed_ask_opened_stops_withholding()
    {
        GameEvent[] log =
        [
            Ev(1, new AskOpened(new Ask(Sealed, AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame))),
            Ev(2, new Undone(0)),
            Ev(3, new AskOpened(new Ask(Sealed, AskQuestions.Activate, "Activate", [Samples.Team1], new Options([]), true, false, null, null))),   // the same id, reopened unsealed
            Ev(4, new AskAnswered(Sealed, Samples.Team1, new Passed(), false)),
        ];
        Assert.NotNull(((AskAnswered)EventFilter.ForViewer(log, Samples.Team2, new HashSet<Guid>())[3].Payload).Answer);
    }
}
