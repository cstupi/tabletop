using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>
/// Warn's game_over Ask (enforcement spec → Warn asks to end): one unframed, sealed Ask to every team with a participant, End or Continue.
/// Any End closes it at once and the game ends; the proposal goes through only when every asked team continued. Read from the log since
/// the last round_started, so a round boundary asks once, and the next boundary asks again.
/// </summary>
public static class GameOverAsk
{
    public enum Decision { NotAsked, Waiting, End, Continue }

    public static Ask Of(IReadOnlyList<Guid> teams, string prompt) =>
        new(Guid.CreateVersion7(), AskQuestions.GameOver, prompt, teams,
            new Options([new AskOption(GameOverChoices.End, "End", []), new AskOption(GameOverChoices.Continue, "Continue", [])]),
            AllowPass: false, Sealed: true, WindowKind: null, FrameId: null);

    /// <summary>The teams asked: those with a participant, as at the round boundary.</summary>
    public static IReadOnlyList<Guid> Teams(GameState state) =>
        state.Info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).Where(t => state.Info.Participants.Any(p => p.TeamId == t)).ToList();

    /// <summary>Closes an open game_over Ask that one team ended (with the answers so far), or that a menu set_round left behind (with none).</summary>
    public static IReadOnlyList<EventPayload> Settle(GameState state)
    {
        if (state.Projection.OpenAsks.Values.All(a => a.Ask.Question != AskQuestions.GameOver)) return [];
        var log = SchedulerLog.Of(state);
        var round = log.RoundStartedAt;
        return log.Effective
            .Where(e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver } o && state.Projection.OpenAsks.ContainsKey(o.Ask.Id))
            .Select(e => (Seq: e.Seq, Open: state.Projection.OpenAsks[((AskOpened)e.Payload).Ask.Id]))
            .Where(x => x.Seq < round || x.Open.Answers.Values.Any(IsEnd))
            .Select(x => (EventPayload)new AskClosed(x.Open.Ask.Id, x.Seq < round ? [] : Answered(x.Open)))
            .ToList();
    }

    public static Decision Read(GameState state)
    {
        var log = SchedulerLog.Of(state);
        var opened = log.SinceRound.LastOrDefault(e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver });
        if (opened?.Payload is not AskOpened { Ask: var ask }) return Decision.NotAsked;
        if (log.SinceRound.FirstOrDefault(e => e.Payload is AskClosed c && c.AskId == ask.Id)?.Payload is not AskClosed closed) return Decision.Waiting;
        if (closed.Answers.Any(a => IsEnd(a.Answer))) return Decision.End;
        return ask.Teams.All(t => closed.Answers.Any(a => a.TeamId == t && a.Answer is Chosen { OptionId: GameOverChoices.Continue }))
            ? Decision.Continue : Decision.NotAsked;                                              // closed unanswered (an abandon): ask again
    }

    static bool IsEnd(AskAnswer? answer) => answer is Chosen { OptionId: GameOverChoices.End };

    static IReadOnlyList<TeamAnswer> Answered(OpenAsk open) =>
        open.Ask.Teams.Where(t => open.Answers.GetValueOrDefault(t) is not null).Select(t => new TeamAnswer(t, open.Answers[t]!)).ToList();
}
