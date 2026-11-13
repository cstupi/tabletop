using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// The player a whole-list game is played by: the uniform policy over exactly the legal set each Ask carries, made to close with the enemy so
/// four rounds on a full table hold shooting, melee and morale. It narrows, never widens: a charge is taken when an activate Ask offers one, a
/// shot when shoot_or_not offers it, and a move keeps the legal sample nearest the closest enemy model (a MoveGoal). Every other Ask, the
/// army rules' Asks among them (use_rule, extra_attacks, remove_tokens, reinforcement_remove, choose_effect, crossing_target, reanimation,
/// takedown_strike, caster_group, the deploy family),
/// goes to the uniform policy as the server shaped it. It reads labels, names and positions; it computes no rule.
/// </summary>
public static class LifecyclePlayer
{
    /// <summary>The activate option for a charge, labelled "&lt;unit&gt;: Charge"; shoot_or_not's Shoot.</summary>
    public static AnswerScript Script { get; } = AnswerScript.Build()
        .Match(AskQuestions.Activate, (ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Charge", StringComparison.Ordinal)))
        .Match("shoot_or_not", (ask, _) => AskPicks.Option(ask, c => c.Id == "shoot"))
        .Build();

    public static AnswerPolicy Policy(ulong seed) => new(new Chooser(seed), goal: NearestEnemy);

    /// <summary>The centre of the closest model on the table of a unit of another team; null with none, or for a model off the table.</summary>
    public static Point2? NearestEnemy(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed || view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => m.Location is OnTable && view.TeamOf(m.UnitId) is { } other && other != team)
            .Select(m => (Point2?)((OnTable)m.Location).Placement.Position.Horizontal)
            .MinBy(p => p!.Value.DistanceTo(at));
    }
}
