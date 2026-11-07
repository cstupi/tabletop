using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A shot to draw: from a shooting model at half its height to the point of the target model the server saw, in its attack
/// frame (the frame whose `hit` roll sounds its volley); Foot, the height the target model stands at.</summary>
public sealed record Shot(Guid ShooterUnitId, Guid ShooterModelId, Point3 From, Point3 To, Guid FrameId = default, Guid TargetModelId = default, double Foot = 0);

/// <summary>
/// The lasers a shooting group fires, read off the server's aims as its `determine-attacks` step completes in an attack-sequence frame under a
/// shooting frame (melee pushes the same sequence under a melee frame and draws nothing). Read then, the target's models are still on the table.
/// Each `aim_&lt;shooter&gt;_&lt;target&gt;` value is the index into `LineOfSight.TargetPoints` of the target model's point the shooter's clear line
/// reaches, so a beam never runs to a model the server found hidden, nor through what hides it.
/// </summary>
public static class ShotLines
{
    const string Shooting = "shooting", AttackSequence = "attack-sequence", DetermineAttacks = "determine-attacks", Aim = "aim_";

    public static IReadOnlyList<Shot> Of(Projection p, StepCompleted step)
    {
        if (step.Step != DetermineAttacks || step.Values is not { } values) return [];
        var at = p.Stack.ToList().FindIndex(f => f.Id == step.FrameId);
        if (at < 1 || p.Stack[at] is not { Sequence: AttackSequence } frame || p.Stack[at - 1].Sequence != Shooting) return [];
        if (frame.Context.ActorUnitId is not { } unit) return [];
        var shots = new List<Shot>();
        foreach (var (key, point) in values)
        {
            if (!key.StartsWith(Aim, StringComparison.Ordinal) || key[Aim.Length..].Split('_') is not [var s, var t]
                || !Guid.TryParse(s, out var shooter) || !Guid.TryParse(t, out var target)) continue;
            if (p.Models.GetValueOrDefault(shooter) is not { Location: OnTable } from || p.Models.GetValueOrDefault(target) is not { Location: OnTable } to) continue;
            var points = LineOfSight.TargetPoints(Placed(to)).ToList();
            if (point < 0 || point >= points.Count) continue;
            shots.Add(new Shot(unit, shooter, Chest(from), points[point], step.FrameId, target, ((OnTable)to.Location).Placement.Position.Z));
        }
        return shots;
    }

    static PlacedModel Placed(ModelInPlay m) => new(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);

    static Point3 Chest(ModelInPlay m) => ((OnTable)m.Location).Placement.Position is var at ? at with { Z = at.Z + m.Slot.Height / 2 } : default;
}
