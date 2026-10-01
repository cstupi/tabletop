using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Win = Tabletop.GameServer.Interpreter.Windows;   // a bare Windows binds to a global namespace of that name in this project

namespace Tabletop.GameServer.Tests;

public class WindowTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Main() => new("main", [new LambdaStep("window", ctx => Win.Run(ctx, "charge-declared") ?? new Complete([]))]);
    static Sequence Reaction() => new("overwatch", [new LambdaStep("shoot", _ => new Complete([new ScoreSet(Samples.Team2, 1)]))]);
    /// <summary>p.13's cap is each model's own token count, per team, which is what GfCasting.SpendCapacity answers (Task 17).</summary>
    static readonly Dictionary<Guid, IReadOnlyDictionary<Guid, int>> Caps = new()
    {
        [Samples.Team1] = new Dictionary<Guid, int> { [A.Models[0].Id] = 1, [A.Models[1].Id] = 2 },
        [Samples.Team2] = new Dictionary<Guid, int> { [B.Models[0].Id] = 3 },
    };
    static StepOutcome Spent(StepContext ctx)
    {
        var spends = Win.Spends(ctx, "casting", Caps);
        var values = spends.ToDictionary(t => t.Key.ToString("N"), t => t.Value.Values.Sum());
        foreach (var (_, counts) in spends) foreach (var (model, tokens) in counts) values[model.ToString("N")] = tokens;
        return new Complete([], values);
    }
    static Sequence Spending() => new("spending", [
        new LambdaStep("spend-tokens", ctx => Win.SimultaneousSpend(ctx, "casting", Caps, "Spend tokens") ?? Spent(ctx))]);

    static StubRules Rules(bool reactive) => new(Main(), Reaction(), Spending())
    {
        Reactions = (state, point, frame) => reactive && point == "charge-declared" ? [new Reaction(Samples.Team2, "overwatch", "Overwatch", "overwatch", new SequenceContext(B.Id, [frame.Context.ActorUnitId!.Value], [], null))] : [],
    };

    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
    static List<EventPayload> Run(GameState state, SequenceInterpreter i) { var all = new List<EventPayload>(); for (var n = 0; n < 50; n++) { var ev = i.Advance(state); if (ev.Count == 0) return all; Append(state, ev); all.AddRange(ev); } throw new InvalidOperationException(); }
    static void Close(GameState state, Ask ask, params (Guid Team, AskAnswer Answer)[] answers)
    {
        foreach (var (t, a) in answers) Append(state, [new AskAnswered(ask.Id, t, a, false)]);
        Append(state, [new AskClosed(ask.Id, answers.Select(x => new TeamAnswer(x.Team, x.Answer)).ToList())]);
    }

    [Fact]
    public void A_reaction_is_offered_to_the_other_team_pushed_when_chosen_and_the_window_closes_after_it()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: true), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "main", new SequenceContext(A.Id, [], [], null)));
        var ask = ((AskOpened)Run(state, i).Single()).Ask;
        Assert.Equal((AskQuestions.React, "charge-declared", Samples.Team2, true), (ask.Question, ask.WindowKind, ask.Teams.Single(), ask.AllowPass));
        Assert.Equal("overwatch", ((Options)ask.Legal).Choices.Single().Id);
        Close(state, ask, (Samples.Team2, new Chosen("overwatch")));
        var events = Run(state, i);
        Assert.Equal(["sequence_started", "step_completed", "sequence_finished", "step_completed", "sequence_finished"], events.Select(EventKinds.Of));
        Assert.Equal("overwatch", ((SequenceStarted)events[0]).Sequence);
        Assert.Equal(1, state.Projection.Scores[Samples.Team2]);
    }

    [Fact]
    public void Passing_closes_the_window_and_nothing_is_asked_when_nobody_can_react()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: true), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "main", new SequenceContext(A.Id, [], [], null)));
        Close(state, ((AskOpened)Run(state, i).Single()).Ask, (Samples.Team2, new Passed()));
        Assert.Equal(["step_completed", "sequence_finished"], Run(state, i).Select(EventKinds.Of));
        var quiet = State([A], [B]);
        var j = new SequenceInterpreter(Rules(reactive: false), TestProperties.Core, new ScriptedDice(1));
        Append(quiet, j.Start(quiet, "main", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "sequence_finished"], Run(quiet, j).Select(EventKinds.Of));
        Assert.Equal([Samples.Team2, Samples.Team1], Win.ReactionOrder(state, Samples.Team1, [Samples.Team1, Samples.Team2]));   // the actor's own team last
        Assert.Equal([Samples.Team2], Win.ReactionOrder(state, Samples.Team1, [Samples.Team2]));                                   // only the reacting teams
    }

    [Fact]
    public void A_simultaneous_spend_is_one_ask_to_every_team_and_reads_back_clamped()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: false), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "spending", new SequenceContext(A.Id, [], [], null)));
        var ask = ((AskOpened)Run(state, i).Last()).Ask;
        var legal = Assert.IsType<TokenSpend>(ask.Legal);
        Assert.True(ask.Sealed); Assert.Equal(2, ask.Teams.Count); Assert.Equal("casting", ask.WindowKind);
        Assert.Equal([1, 2, 3], legal.MaxByModel.Values.OrderBy(v => v));                                              // one cap per model, both teams in one map, no total
        Close(state, ask, (Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [A.Models[0].Id] = 1 })), (Samples.Team2, new Passed()));
        var done = (StepCompleted)Run(state, i)[0];
        Assert.Equal((1, 0), (done.Values![Samples.Team1.ToString("N")], done.Values[Samples.Team2.ToString("N")]));   // one of the two it could have spent; a pass spends nothing
        Assert.Equal(1, done.Values[A.Models[0].Id.ToString("N")]);
        Assert.False(done.Values.ContainsKey(A.Models[1].Id.ToString("N")));                                            // the model that kept its tokens for its own spell
    }

    [Fact]
    public void A_spend_naming_another_teams_model_or_more_than_a_model_holds_is_dropped_and_cut()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: false), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "spending", new SequenceContext(A.Id, [], [], null)));
        var ask = ((AskOpened)Run(state, i).Last()).Ask;
        // Appended straight to the log, so nothing judged membership: these are the answers warn or silent would accept.
        Close(state, ask,
            (Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [A.Models[0].Id] = 3, [B.Models[0].Id] = 3 })),
            (Samples.Team2, new TokensSpent(new Dictionary<Guid, int> { [B.Models[0].Id] = 3 })));
        var done = (StepCompleted)Run(state, i)[0];
        Assert.Equal((1, 3), (done.Values![Samples.Team1.ToString("N")], done.Values[Samples.Team2.ToString("N")]));   // 3 cut to that model's cap of 1; the enemy model dropped
        Assert.Equal(3, done.Values[B.Models[0].Id.ToString("N")]);                                                     // spent by the team that owns it
    }
}
