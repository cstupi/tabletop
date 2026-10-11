using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A laser to draw: from a shooting model to the target model nearest it, both at half their height.</summary>
public sealed record Shot(Guid ShooterUnitId, Guid ShooterModelId, Point3 From, Point3 To);

/// <summary>
/// The lasers a shooting group fires, read off the projection just after its attack-sequence frame is pushed under a shooting frame
/// (melee pushes the same sequence under a melee frame and draws nothing). Read then, the target's models are still on the table.
/// The shooters are the actor's models on the table carrying one of the frame's weapons.
/// </summary>
public static class ShotLines
{
    const string Shooting = "shooting", AttackSequence = "attack-sequence";

    public static IReadOnlyList<Shot> Of(Projection p)
    {
        if (p.Stack.Count < 2 || p.Stack[^1] is not { Sequence: AttackSequence } frame || p.Stack[^2].Sequence != Shooting) return [];
        if (frame.Context.ActorUnitId is not { } unit) return [];
        var weapons = frame.Context.WeaponIds.ToHashSet();
        var targets = p.Models.Values.Where(m => frame.Context.TargetUnitIds.Contains(m.UnitId) && m.Location is OnTable).ToList();
        if (targets.Count == 0) return [];
        return p.Models.Values
            .Where(m => m.UnitId == unit && m.Location is OnTable && m.Slot.Weapons.Any(w => weapons.Contains(w.Id)))
            .Select(m =>
            {
                var from = Placed(m);
                var nearest = targets.MinBy(t => Distance.ClosestPoints(from, Placed(t)) is var (a, b) ? a.Horizontal.DistanceTo(b.Horizontal) : 0)!;
                return new Shot(unit, m.Id, Chest(m), Chest(nearest));
            })
            .ToList();
    }

    static PlacedModel Placed(ModelInPlay m) => new(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);

    static Point3 Chest(ModelInPlay m) => ((OnTable)m.Location).Placement.Position is var at ? at with { Z = at.Z + m.Slot.Height / 2 } : default;
}
