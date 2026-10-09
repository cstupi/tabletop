using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// A hand move (MoveModels) under the enforcement setting (in-game UX spec → Hand moves under enforcement). Legal only as deployment
/// placement: every relocation takes a model from the shelf to the table or into a container the rules allow, and the model's unit is one the actor's team picked in the
/// deploy-family Ask the scheduler is waiting on. The scheduler waits for placement without opening another Ask (a deploy activation is
/// incomplete while a model is on the shelf; the Ambusher boundary step waits likewise), so the pick is live exactly while the newest
/// AskOpened in the effective log is that deploy Ask and it has closed. Everything else is the hand_move breach, which strict refuses.
/// </summary>
public static class HandMoves
{
    public const string Rule = "hand_move";
    static readonly HashSet<string> DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    /// <summary>Null when legal. A model the projection lacks is skipped: MenuTranslation has already refused it as Unknown.</summary>
    public static Breach? Judge(GameState state, MoveModels command, Actor actor)
    {
        var p = state.Projection;
        var placing = actor.TeamId is { } team ? Placing(state, team) : new HashSet<Guid>();
        foreach (var move in command.Moves)
        {
            if (!p.Models.TryGetValue(move.ModelId, out var model)) continue;
            if (model.Location is OnShelf && placing.Contains(model.UnitId) && Placed(state, move.To, model.UnitId)) continue;
            return new Breach(Rule, $"{p.Units[model.UnitId].Definition.Name} moved by hand outside a move step", new StrictRefuses(RefusalReason.Illegal));
        }
        return null;
    }

    /// <summary>Onto the table, or inside a container the rules allow (IGameRules.MayDeployInside).</summary>
    static bool Placed(GameState state, Location to, Guid unit) => to switch
    {
        OnTable => true,
        Inside i => state.Rules.MayDeployInside(state, unit, i.ContainerId),
        _ => false,
    };

    /// <summary>The units the team picked in the deploy-family Ask the scheduler waits on; empty when any Ask has opened since, or none was answered.</summary>
    static HashSet<Guid> Placing(GameState state, Guid team)
    {
        var deployAsks = new HashSet<Guid>();
        var picked = new HashSet<Guid>();
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log))
            switch (e.Payload)
            {
                case AskOpened o:
                    picked = [];
                    if (o.Ask.FrameId is null && DeployQuestions.Contains(o.Ask.Question)) deployAsks.Add(o.Ask.Id);
                    break;
                case AskClosed c when deployAsks.Contains(c.AskId):
                    picked = c.Answers.Where(a => a.TeamId == team).Select(a => a.Answer).OfType<SelectedPieces>().SelectMany(s => s.PieceIds).ToHashSet();
                    break;
            }
        return picked;
    }
}
