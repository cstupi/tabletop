using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum GameOverStage { None, Choose, Waiting, Ended }

/// <summary>What the Game over popup shows. Choose and Waiting: the game_over Ask's prompt, with its choices while my team has not answered.
/// Ended: the result and reason. Rounds: the rounds played. WaitingFor: the teams yet to answer while my team waits, each drawn with its band.</summary>
public sealed record GameOverModel(GameOverStage Stage, Guid? AskId, string? Result, string Reason, IReadOnlyList<AskOption> Choices, string? Note,
    int Rounds = 0, IReadOnlyList<Guid>? WaitingFor = null)
{
    /// <summary>`Team 1 wins`: the result alone, since the reason under the scores says why and after how many rounds; null before the end.</summary>
    public string? Headline => Result;
}

public static class GameOverView
{
    public const string WaitingNote = "Waiting for the other player";
    /// <summary>`Waiting for Team 2`, the teams named; WaitingNote when none is named.</summary>
    public static string WaitingText(IReadOnlyList<Guid> teams, Names n) => teams.Count == 0 ? WaitingNote : "Waiting for " + string.Join(", ", teams.Select(n.Team));

    public static AnswerAsk Answer(GameOverModel model, string optionId, Guid? team) => new(model.AskId!.Value, new Chosen(optionId), team);

    public static GameOverModel Of(Projection p, GameInfo game, Guid? myTeam)
    {
        var rounds = p.Scheduler.Round;
        if (p.Ended is { } ended) return new(GameOverStage.Ended, null, HeaderView.Outcome(ended, new Names(game, p)), ended.Reason, [], null, rounds);
        if (p.OpenAsks.Values.FirstOrDefault(a => a.Ask.Question == AskQuestions.GameOver) is not { } open)
            return new(GameOverStage.None, null, null, "", [], null);
        var mine = myTeam is { } team && open.Ask.Teams.Contains(team) && !open.Answers.ContainsKey(team);
        var waiting = open.Ask.Teams.Where(t => !open.Answers.ContainsKey(t)).ToList();
        return mine
            ? new(GameOverStage.Choose, open.Ask.Id, null, open.Ask.Prompt, ((Options)open.Ask.Legal).Choices, null, rounds, waiting)
            : new(GameOverStage.Waiting, open.Ask.Id, null, open.Ask.Prompt, [], WaitingText(waiting, new Names(game, p)), rounds, waiting);
    }
}
