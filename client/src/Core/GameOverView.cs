using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum GameOverStage { None, Choose, Waiting, Ended }

/// <summary>What the Game over popup shows. Choose and Waiting: the game_over Ask's prompt, with its choices while my team has not answered.
/// Ended: the result and reason.</summary>
public sealed record GameOverModel(GameOverStage Stage, Guid? AskId, string? Result, string Reason, IReadOnlyList<AskOption> Choices, string? Note);

public static class GameOverView
{
    public const string WaitingNote = "Waiting for the other player";

    public static AnswerAsk Answer(GameOverModel model, string optionId, Guid? team) => new(model.AskId!.Value, new Chosen(optionId), team);

    public static GameOverModel Of(Projection p, GameInfo game, Guid? myTeam)
    {
        if (p.Ended is { } ended) return new(GameOverStage.Ended, null, HeaderView.Outcome(ended, new Names(game, p)), ended.Reason, [], null);
        if (p.OpenAsks.Values.FirstOrDefault(a => a.Ask.Question == AskQuestions.GameOver) is not { } open)
            return new(GameOverStage.None, null, null, "", [], null);
        var mine = myTeam is { } team && open.Ask.Teams.Contains(team) && !open.Answers.ContainsKey(team);
        return mine
            ? new(GameOverStage.Choose, open.Ask.Id, null, open.Ask.Prompt, ((Options)open.Ask.Legal).Choices, null)
            : new(GameOverStage.Waiting, open.Ask.Id, null, open.Ask.Prompt, [], WaitingNote);
    }
}
