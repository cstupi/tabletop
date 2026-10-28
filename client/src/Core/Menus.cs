using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What every play menu reads: the browser's MenuContext.</summary>
public sealed record MenuContext(GameInfo Game, Projection P, IReadOnlyList<GameEvent> Log, bool Watching, Guid? MyTeam, AskView? Mine,
    IReadOnlyList<GrantChoice> Grantable, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside, IReadOnlySet<Guid> MaySetAside, MarkerLocks Locks);

/// <summary>Which play menu opens with which items (the browser's play/menus.ts). A watcher changes nothing: every menu is empty.</summary>
public static class Menus
{
    /// <summary>A model: boarding, then Shift the unit's items, else the model's and the frame's; else the marker's; [] for a watcher.</summary>
    public static IReadOnlyList<MenuItem> Piece(MenuContext ctx, Guid id, bool shift)
    {
        if (ctx.Watching) return [];
        if (ctx.P.Models.TryGetValue(id, out var model))
        {
            var known = MenuModel.KnownActionNames(ctx.P, ctx.Log);
            var boarding = MenuModel.ForBoarding(ctx.P, ctx.Mine?.Ask, id);
            return [.. boarding, .. shift ? MenuModel.ForUnit(ctx.P, model.UnitId, known, ctx.Grantable) : MenuModel.ForModel(ctx.P, id, known).Concat(MenuModel.ForFrame(ctx.P))];
        }
        return MenuModel.ForMarker(ctx.Game, ctx.P, id, ctx.Locks);
    }

    /// <summary>The team's items, then each of its models off the table; [] for a watcher.</summary>
    public static IReadOnlyList<MenuItem> Team(MenuContext ctx, Guid teamId) =>
        ctx.Watching ? [] : [.. MenuModel.ForTeam(ctx.Game, ctx.P, teamId), .. MenuModel.OffTable(ctx.P, teamId, new Names(ctx.Game, ctx.P))];

    /// <summary>MenuModel.ForRosterUnit for MyTeam; [] for a watcher.</summary>
    public static IReadOnlyList<MenuItem> Roster(MenuContext ctx, Guid unitId) =>
        ctx.Watching ? [] : MenuModel.ForRosterUnit(ctx.Game, ctx.P, unitId, ctx.MyTeam, ctx.DeployInside, ctx.MaySetAside);
}
