using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One drop's call: a command or a setup edit, never both.</summary>
public sealed record Call(Command? Command, Edit? Edit);

/// <summary>What a drag holds when it drops: the piece under the cursor, the held models, where each was grabbed, where it lands, its yaw.</summary>
public sealed record HeldDrop(Guid PieceId, IReadOnlyList<Guid> ModelIds, IReadOnlyDictionary<Guid, Placement> Grabs,
    IReadOnlyDictionary<Guid, Point3> Current, IReadOnlyDictionary<Guid, Yaw> Yaws);

/// <summary>What a drop sends. The browser's handMove.ts and setup/drag.ts build the same.</summary>
public static class HandMoves
{
    /// <summary>From each grab to its landing and yaw, in ModelIds order.</summary>
    public static MoveModels Models(HeldDrop drop) =>
        new(drop.ModelIds.Select(m => new ModelRelocation(m, new OnTable(drop.Grabs[m]), new OnTable(new Placement(drop.Current[m], drop.Yaws[m])))).ToList());

    /// <summary>The held models' MoveModels when the projection has them all; else in setup a draft terrain piece's MoveTerrain or a draft
    /// marker's MoveSetupMarker; else a play or table marker's MoveMarker; else null.</summary>
    public static Call? Drop(GameStatus? status, Projection? p, TableState? draft, HeldDrop drop)
    {
        var id = drop.PieceId;
        if (p is not null && drop.ModelIds.All(p.Models.ContainsKey)) return new Call(Models(drop), null);
        if (status == GameStatus.Setup && draft is not null && draft.Terrain.Any(t => t.Id == id))
            return new Call(null, new MoveTerrain(id, drop.Current[id], drop.Yaws[id]));
        if (status == GameStatus.Setup && draft is not null && draft.Markers.Any(m => m.Id == id))
            return new Call(null, new MoveSetupMarker(id, drop.Current[id], drop.Yaws[id]));
        if (p is not null && (p.Markers.ContainsKey(id) || p.Board.Table.Markers.Any(m => m.Id == id)))   // a play marker, or a setup one (an unlocked objective)
            return new Call(new MoveMarker(id, drop.Current[id], drop.Yaws[id]), null);
        return null;
    }
}
