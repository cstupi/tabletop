using Tabletop.Geometry;

namespace Tabletop.Sim.Answers;

/// <summary>
/// The objectives scenario's goal: the centre of the nearest objective marker the model's team does not control (neutral, or held by
/// another team). Null when the team holds every marker, when there is none, or when the model is off the table or the view has no
/// GameInfo. It reads team and control to choose a point; whether a unit seizes is the engine's call at the round end (p.6), not this.
/// </summary>
public static class ObjectiveGoal
{
    public static Point2? Nearest(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed) return null;
        if (view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
        var at = placed.Placement.Position.Horizontal;
        return view.ObjectiveMarkers
            .Where(m => !(view.Projection.Control.TryGetValue(m.Id, out var holder) && holder == team))
            .Select(m => (Point2?)m.Position.Horizontal)
            .MinBy(p => p!.Value.DistanceTo(at));
    }
}
