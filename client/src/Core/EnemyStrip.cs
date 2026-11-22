using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed record RangeCheck(string Weapon, string Range, bool? Within);
public sealed record EnemyStripModel(Guid UnitId, string Name, string? Defense, int Alive, double? Distance, IReadOnlyList<RangeCheck> Ranges);

/// <summary>
/// The strip above the unit card, inside the left column (client UX spec → Unit card, Hovered enemy): the hovered enemy's name, Defense and models alive, the
/// distance between the two units, and each of its own ranged weapons' printed range within or beyond it. No claim about shooting.
/// </summary>
public static class EnemyStrip
{
    /// <summary>The card shows one of my units and the pointer is over a unit of another team.</summary>
    public static bool Shows(Projection p, GameInfo game, Guid? myTeamId, Guid cardUnit, Guid hoveredUnit) =>
        myTeamId is { } mine && SheetFocus.TeamOf(p, game, cardUnit)?.Id == mine
        && SheetFocus.TeamOf(p, game, hoveredUnit) is { } theirs && theirs.Id != mine;

    /// <summary>Which of my units the distance is measured from: my team's acting unit (the activation's actor, the shooter while targets are declared),
    /// else the card's unit.</summary>
    public static Guid Measured(Projection p, GameInfo game, Guid? myTeamId, Guid cardUnit) =>
        myTeamId is { } mine && HeaderView.ActionFrame(p)?.Context.ActorUnitId is { } actor && SheetFocus.TeamOf(p, game, actor)?.Id == mine ? actor : cardUnit;

    public static EnemyStripModel Of(UnitSheetModel hovered, double? distance) =>
        new(hovered.UnitId, hovered.Name, hovered.Defense, hovered.Alive, distance,
            hovered.Weapons.Where(w => w.Range != "-").Select(w => new RangeCheck(w.Name, w.Range, Within(w.Range, distance))).ToList());

    static bool? Within(string range, double? distance) =>
        distance is { } d && int.TryParse(new string(range.TakeWhile(char.IsDigit).ToArray()), out var inches) ? d <= inches : null;
}
