using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// Which unit the unit card shows (client UX spec → Unit card, Which unit): the selected unit; with no unit selected, my team's
/// activating unit; otherwise none, and the card hides. A selected terrain piece takes the slot: the terrain card shows it, so none. A selection stays until the player clears it, so an activation never
/// changes it, and another team's activation never takes the card: the header names that one. While deploying, a deploy pick's tile under the
/// pointer (hovered) takes the card first, then the unit my team is placing (placing, PlacingFocus's), so a unit not on the table shows.
/// </summary>
public static class SheetFocus
{
    public static Guid? Of(Projection p, GameInfo game, Guid? myTeamId, Guid? selectedPiece, Guid? hovered = null, Guid? placing = null)
    {
        if (hovered is { } h && p.Units.ContainsKey(h)) return h;
        if (placing is { } placed && p.Units.ContainsKey(placed)) return placed;
        if (selectedPiece is { } selected && p.Units.ContainsKey(selected)) return selected;
        if (selectedPiece is { } piece && p.Board.Table.Terrain.Any(t => t.Id == piece)) return null;
        return myTeamId is { } mine && HeaderView.ActionFrame(p)?.Context.ActorUnitId is { } actor && TeamOf(p, game, actor)?.Id == mine
            ? actor : null;
    }

    public static Team? TeamOf(Projection p, GameInfo game, Guid unitId) =>
        p.Units.GetValueOrDefault(unitId) is { } unit && game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId is { } team
            ? game.Teams.FirstOrDefault(t => t.Id == team) : null;
}
