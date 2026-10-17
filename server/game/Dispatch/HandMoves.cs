using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// A hand move (MoveModels) under the enforcement setting (in-game UX spec → Hand moves under enforcement). Legal only as deployment
/// placement: every relocation takes a model of a unit the actor's team picked in the deploy-family Ask the scheduler is waiting on from
/// the shelf to the table, or takes a model of the actor's team from the shelf into a container the rules allow (IGameRules.MayDeployInside: the deploying
/// unit's Transport, or one not yet deployed — passengers spec), or back out of one still on the shelf. The scheduler waits for placement without opening another Ask (a deploy activation is
/// incomplete while a model is on the shelf; the Ambusher boundary step waits likewise), so the pick is live exactly while the newest
/// AskOpened in the effective log is that deploy Ask and it has closed. Everything else is the hand_move breach, which strict refuses. A placement
/// the ruleset judges physically impossible (IGameRules.PlacementBreach) breaches too, legal or not.
/// </summary>
public static class HandMoves
{
    public const string Rule = "hand_move";
    static readonly HashSet<string> DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement];
    static readonly HashSet<Guid> NoneDeploying = [];

    /// <summary>Null when legal. A model the projection lacks is skipped: MenuTranslation has already refused it as Unknown.</summary>
    public static Breach? Judge(GameState state, MoveModels command, Actor actor)
    {
        var p = state.Projection;
        var placing = actor.TeamId is { } team ? Placing(state, team) : new HashSet<Guid>();
        Breach? outside = null;
        foreach (var move in command.Moves)
        {
            if (!p.Models.TryGetValue(move.ModelId, out var model)) continue;
            var name = p.Units[model.UnitId].Definition.Name;
            var mine = actor.TeamId is { } t && Teams.Of(state, model.UnitId) == t;
            if (model.Location is OnShelf && move.To is Inside i)
            {
                if (mine && state.Rules.MayDeployInside(state, model.UnitId, i.ContainerId, placing)) continue;
                outside = new Breach(Rule, $"{name} deployed inside a container the rules do not allow", new StrictRefuses(RefusalReason.Illegal));
                break;
            }
            // Passengers spec → Take out of transport: back to the shelf from a container still on the shelf, while the rules would let it go in.
            if (model.Location is Inside from && move.To is OnShelf && mine && p.Models.TryGetValue(from.ContainerId, out var holder) && holder.Location is OnShelf
                && state.Rules.MayDeployInside(state, model.UnitId, from.ContainerId, NoneDeploying)) continue;
            if (model.Location is OnShelf && move.To is OnTable && placing.Contains(model.UnitId))
            {
                // A Transport placed with units inside: they deploy with it only while the rules still allow them in (set aside since they went in: not).
                var refused = Lookup.OccupantUnits(state, model.Id).FirstOrDefault(u => !state.Rules.MayDeployInside(state, u, model.Id, placing));
                if (refused == Guid.Empty) continue;
                outside = new Breach(Rule, $"{p.Units[refused].Definition.Name} deployed inside a container the rules do not allow", new StrictRefuses(RefusalReason.Illegal));
                break;
            }
            outside = new Breach(Rule, $"{name} moved by hand outside a move step", new StrictRefuses(RefusalReason.Illegal));
            break;
        }
        var overlap = state.Rules.PlacementBreach(state, command.Moves);
        if (outside is { } b) return overlap is null ? b : b with { Detail = $"{b.Detail}; {overlap.Detail}" };
        return overlap is null ? null : new Breach(overlap.Rule, overlap.Detail, new StrictRefuses(RefusalReason.Illegal));
    }

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
