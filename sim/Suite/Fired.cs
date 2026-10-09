using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// "It fired" (rules suite spec → Attribution): the entry's name in some StepCompleted.Applied or in a RollModifier.Source of a
/// completed step's roll, in the effective log. The log is reduced to its effective form here too, so an undone step never counts
/// whichever log a caller hands in.
/// </summary>
public static class Fired
{
    public static bool In(IReadOnlyList<GameEvent> effectiveLog, string entry) =>
        EventApplier.EffectiveLog(effectiveLog).Select(e => e.Payload).OfType<StepCompleted>().Any(step =>
            (step.Applied?.Contains(entry) ?? false) || step.Rolls.Any(r => r.Modifiers.Any(m => m.Source == entry)));
}
