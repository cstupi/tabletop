# Task 9: Windows — offered, invoked, and the simultaneous token spend

**Files:**
- Create: `server/game/Interpreter/Windows.cs`
- Modify: `server/game/Interpreter/StepContext.cs` (`SealedAsk`)
- Test: `tests/Tabletop.GameServer.Tests/WindowTests.cs`

**Interfaces:**
- Consumes: Task 8; `AskQuestions.React`, `AskQuestions.SpendTokens`, `TokenSpend`, `TokensSpent`, `Passed`, `Chosen` (Protocol); `Projection.Scheduler.Order`.
- Produces: `static class Windows` with `IRL<Guid> ReactionOrder(GameState state, Guid? actorTeam, IRL<Guid> reactingTeams)` (**the one reaction-ordering policy**, shared with the scheduler plan's Task 8: `state.Rules.OrderReactions(state, actorTeam, reactingTeams)` when it returns non-null; otherwise the scheduler's order rotated to start after the actor's team — the actor's own team last, since OPR's casting window is offered to it — teams missing from the order following in `Info.Teams` order, restricted to `reactingTeams`, with teams in `Projection.Scheduler.Passed` dropped; scheduler → Windows), `StepOutcome? Run(StepContext ctx, string point)` (null when the window is done: nobody can react, or every eligible team has passed or its reaction finished; otherwise `OpenAsks` of the next team's react-or-pass Ask, or `Push` of the next chosen reaction), `StepOutcome? SimultaneousSpend(StepContext ctx, string windowKind, IRD<Guid,IRD<Guid,int>> capsByTeam, string prompt)` (one sealed `TokenSpend` Ask to every team with an eligible model; null once it is closed), `IRD<Guid,IRD<Guid,int>> Spends(StepContext ctx, string windowKind, IRD<Guid,IRD<Guid,int>> capsByTeam)` (each team's revealed `TokensSpent.Counts`, keys the team does not own dropped and each count cut to the Ask's own cap for that model, empty for a pass); `StepContext.SealedAsk(string question, string prompt, IRL<Guid> teams, LegalSet legal, string windowKind)`.
- A window is one Ask per eligible team, `WindowKind = point`, `AllowPass = true`, `Options` = that team's reactions; teams are asked in `ReactionOrder`, one at a time, and a chosen reaction is pushed before the next team is asked, so reactions resolve in turn order after the actor (scheduler → Windows). The k-th chosen reaction is the k-th child in the step's history — that is how a re-run step knows which reactions have been pushed. Nothing is asked when `ReactionsAt` is empty (action sequences → Windows: "If nothing can react, no window opens").
- The simultaneous token spend is one Ask addressed to every eligible team with `Sealed = true`; the dispatcher withholds answers until all have answered and closes with every answer (skeleton Task 5–6). Its legal set is `TokenSpend(MaxByModel)`, a cap per model rather than one number: p.13 lets a player choose which of its models pays, so the answer is `TokensSpent.Counts` keyed by model (ruling 14; contracts → the two kinds are maps). One map covers every addressed team, since an Ask has one legal set for all of its teams, and it hides nothing, because `ModelInPlay.SpellTokens` is in every client's projection. **There is no per-team maximum**, no `Windows.MaxFor` and no `Values["tokens_max_<team>"]`: a team's maximum is the sum of its own entries, so no clamp derives it and no Ask carries another team's total. Two checks remain the step's rather than `LegalSetMembership`'s, on the precedent that `MoveRegion` checks model ids and leaves distance to the move step: a key belongs to the answering team, and a count is within that key's recorded cap. Both are applied whatever the enforcement setting, because spending tokens a model does not hold would drive its counter below zero. The caps are read back off the Ask itself, which the log holds, rather than recomputed.

Spec: action sequences → Windows (offered, invoked, sealed; passing is a real answer, logged; the window closes when every offered team has passed or its reaction has finished); scheduler → Windows (ordering: turn order from the team after the actor; muting is the client's); `architecture.md` → per-player filtered streams (the simultaneous token spend is their one use).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class WindowTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Main() => new("main", [new LambdaStep("window", ctx => Windows.Run(ctx, "charge-declared") ?? new Complete([]))]);
    static Sequence Reaction() => new("overwatch", [new LambdaStep("shoot", _ => new Complete([new ScoreSet(Samples.Team2, 1)]))]);
    /// <summary>p.13's cap is each model's own token count, per team, which is what GfCasting.SpendCapacity answers (Task 17).</summary>
    static readonly Dictionary<Guid, IReadOnlyDictionary<Guid, int>> Caps = new()
    {
        [Samples.Team1] = new Dictionary<Guid, int> { [A.Models[0].Id] = 1, [A.Models[1].Id] = 2 },
        [Samples.Team2] = new Dictionary<Guid, int> { [B.Models[0].Id] = 3 },
    };
    static StepOutcome Spent(StepContext ctx)
    {
        var spends = Windows.Spends(ctx, "casting", Caps);
        var values = spends.ToDictionary(t => t.Key.ToString("N"), t => t.Value.Values.Sum());
        foreach (var (_, counts) in spends) foreach (var (model, tokens) in counts) values[model.ToString("N")] = tokens;
        return new Complete([], values);
    }
    static Sequence Spending() => new("spending", [
        new LambdaStep("spend-tokens", ctx => Windows.SimultaneousSpend(ctx, "casting", Caps, "Spend tokens") ?? Spent(ctx))]);

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
        Assert.Equal([Samples.Team2, Samples.Team1], Windows.ReactionOrder(state, Samples.Team1, [Samples.Team1, Samples.Team2]));   // the actor's own team last
        Assert.Equal([Samples.Team2], Windows.ReactionOrder(state, Samples.Team1, [Samples.Team2]));                                   // only the reacting teams
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
```

- [ ] **Step 2: Run; expect compile errors. Write `Windows.cs` and add `SealedAsk` to `StepContext`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>An Ask about interrupting. One Ask per eligible team, asked in reaction order, each chosen reaction pushed before the next team is asked.</summary>
public static class Windows
{
    /// <summary>
    /// Who reacts first (scheduler → Windows): GameRules.OrderReactions when it answers; else turn order starting after the actor's team
    /// (the actor's own team last), teams the order does not list following in Info.Teams order, only the reacting teams, passed teams dropped.
    /// </summary>
    public static IReadOnlyList<Guid> ReactionOrder(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)
    {
        if (state.Rules.OrderReactions(state, actorTeam, reactingTeams) is { } replaced) return replaced;
        var s = state.Projection.Scheduler;
        var order = s.Order.Count > 0 ? s.Order.ToList() : state.Info.Teams.Select(t => t.Id).ToList();
        var start = actorTeam is { } a && order.Contains(a) ? order.IndexOf(a) + 1 : 0;
        var rotated = order.Skip(start).Concat(order.Take(start)).Concat(state.Info.Teams.Select(t => t.Id).Where(t => !order.Contains(t))).ToList();
        return rotated.Where(t => reactingTeams.Contains(t) && !s.Passed.Contains(t)).ToList();
    }

    public static StepOutcome? Run(StepContext ctx, string point)
    {
        var reactions = ctx.Rules.ReactionsAt(ctx.State, ctx.Properties, point, ctx.Frame);
        if (reactions.Count == 0) return null;
        var asks = ctx.History.Asks.Where(a => a.Ask.WindowKind == point && a.Ask.Question == AskQuestions.React).ToList();
        var pushed = 0;
        foreach (var team in ReactionOrder(ctx.State, ctx.ActorTeam, reactions.Select(r => r.TeamId).Distinct().ToList()))
        {
            var mine = reactions.Where(r => r.TeamId == team).ToList();
            if (mine.Count == 0) continue;
            var ask = asks.FirstOrDefault(a => a.Ask.Teams[0] == team);
            if (ask is null)
                return new OpenAsks([ctx.Ask(AskQuestions.React, $"React at {point}?", team, new Options(mine.Select(r => new AskOption(r.OptionId, r.Label, [r.Context.ActorUnitId ?? Guid.Empty])).ToList()), allowPass: true, windowKind: point)]);
            if (!ask.Closed) return new OpenAsks([]);   // unreachable: Advance returns before running a waiting step
            if (ask.Single is not Chosen chosen) continue;   // passed, or an out-of-set answer accepted under warn/silent
            var reaction = mine.FirstOrDefault(r => r.OptionId == chosen.OptionId) ?? mine[0];
            if (ctx.History.Children.Count <= pushed) return new Push(reaction.Sequence, reaction.Context);
            pushed++;   // its child exists and, since this frame is on top, has finished or was abandoned
        }
        return null;
    }

    /// <summary>
    /// p.13's spend: one sealed Ask to every team holding an eligible model, whose legal set is a cap per model — the tokens that
    /// model holds — for every addressed team's models in one map. There is no total to carry, so nothing here reads a team's own
    /// maximum: it is the sum of that team's entries.
    /// </summary>
    public static StepOutcome? SimultaneousSpend(StepContext ctx, string windowKind, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> capsByTeam, string prompt)
    {
        var teams = capsByTeam.Where(kv => kv.Value.Any(c => c.Value > 0)).Select(kv => kv.Key).ToList();
        if (teams.Count == 0) return null;
        var existing = ctx.History.Asks.FirstOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.SpendTokens);
        if (existing is null)
        {
            var caps = teams.SelectMany(t => capsByTeam[t]).Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);
            return new OpenAsks([ctx.SealedAsk(AskQuestions.SpendTokens, prompt, teams, new TokenSpend(caps), windowKind)]);
        }
        return existing.Closed ? null : new OpenAsks([]);
    }

    /// <summary>
    /// Every team's revealed spend, per model. Two checks are the step's rather than LegalSetMembership's, which has no state to
    /// answer them with (the MoveRegion precedent): a key the answering team does not own is dropped, and a count is cut to the
    /// cap the Ask recorded for that key. The caps come off the Ask, which the log holds, so a replay never recomputes them.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> Spends(StepContext ctx, string windowKind, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> capsByTeam)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<Guid, int>>();
        var ask = ctx.History.Asks.LastOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.SpendTokens && a.Closed);
        if (ask is null) return result;
        var caps = ((TokenSpend)ask.Ask.Legal).MaxByModel;
        foreach (var team in ask.Ask.Teams)
        {
            var owned = capsByTeam.GetValueOrDefault(team) ?? new Dictionary<Guid, int>();
            var counts = new Dictionary<Guid, int>();
            if (ask.Answers!.GetValueOrDefault(team) is TokensSpent spent)
                foreach (var (model, asked) in spent.Counts.Where(c => owned.ContainsKey(c.Key)))
                {
                    var take = Math.Clamp(asked, 0, caps.GetValueOrDefault(model));
                    if (take > 0) counts[model] = take;
                }
            result[team] = counts;
        }
        return result;
    }
}
```

```csharp
    // StepContext
    public Ask SealedAsk(string question, string prompt, IReadOnlyList<Guid> teams, LegalSet legal, string windowKind) =>
        new(Guid.CreateVersion7(), question, prompt, teams, legal, true, true, windowKind, Frame.Id);
```

`SimultaneousSpend` records nothing itself and no step records a maximum for it: the caps travel in the Ask, and the caller passes the same per-team map to both calls so that ownership of a key has an answer. `Windows` therefore reads no game state for the spend, and there is no `Values["tokens_max_<team>"]` and no `MaxFor`.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter WindowTests`; expect 4 passed. Commit: `Open windows: react-or-pass Asks in reaction order, and the simultaneous token spend`.**
