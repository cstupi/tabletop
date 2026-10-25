using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The move answer's geometry breaches (IGameRules.AnswerBreach): each move judged along the path its base takes.</summary>
public static partial class GfMoves
{
    /// <summary>
    /// p.7 "may never move through other models or units": the models on the table this answer does not move, which a moving model's path goes round;
    /// none for a region that PassesModels (p.13 Flying, Aircraft).
    /// </summary>
    static IReadOnlyList<PlacedModel> Standing(GameState state, MoveRegion region, IReadOnlyList<ModelRelocation> moves)
    {
        if (region.PassesModels) return [];
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        return state.Projection.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable).Select(x => Battlefield.Placed(state, x.Id)!.Value).ToList();
    }

    /// <summary>
    /// One model's move along the path its base takes (Movement.Check), against the region's own parameters: its impassable volumes, its difficult
    /// volumes and cap (or, with `limit`, that length and no cap: the unit is held to it), the standing models in plan and height over the terrain,
    /// and the table. p.5: "always measure so that no part of its base moves further than the total distance".
    /// </summary>
    static MoveCheck Route(GameState state, MoveRegion region, PlacedModel start, Point2 to, IReadOnlyList<PlacedModel> standing, double? limit = null)
    {
        var terrain = Battlefield.Terrain(state);
        var impassable = terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = limit is null ? terrain.Where(v => region.DifficultVolumeIds.Contains(v.Id)).ToList() : [];
        return Movement.Check(start, to, limit ?? region.Allowance, impassable, difficult, limit is null ? region.DifficultCap : null, state.Board.Table.Size,
            standing, terrain);
    }

    /// <summary>
    /// p.11: "If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6"
    /// for that movement." The region's DifficultCap is what is left of that cap in this segment (null for Strider, Flying and Aircraft, which it does not
    /// bind). A model moves in or through difficult terrain when no path round every listed difficult volume fits the allowance (Route: a player going
    /// round a wood never chose to enter it). Once one does, or an earlier segment of the same move did (the move step's EnteredDifficultKey), every
    /// model moving on the table is held to the cap along its shortest path. Null when none is over. A disembark (no start to measure from) and an
    /// embark are not moves on the table and are not judged.
    /// </summary>
    public static AnswerBreachFound? DifficultCapBreach(GameState state, Ask ask, MoveRegion region, Destinations answer)
    {
        if (region.DifficultCap is not { } cap || region.Containment?.ExitingId is not null) return null;
        var moves = GfAnswers.Moves(state, region, answer);
        var standing = Standing(state, region, moves);
        var starts = moves.Where(m => m.To is OnTable && Battlefield.Placed(state, m.ModelId) is not null)
            .Select(m => (m.ModelId, Start: Battlefield.Placed(state, m.ModelId)!.Value, To: ((OnTable)m.To).Placement.Position.Horizontal))
            .Where(m => m.Start.Placement.Position.Horizontal.DistanceTo(m.To) > 1e-6).ToList();
        var earlier = state.Projection.Stack.FirstOrDefault(f => f.Id == ask.FrameId)?.Steps.Any(s => s.Values?.GetValueOrDefault(GfNames.EnteredDifficultKey) > 0) ?? false;
        if (!earlier && !starts.Any(m => Route(state, region, m.Start, m.To, standing).EntersDifficult)) return null;
        var limit = Math.Min(region.Allowance, cap);
        foreach (var (id, start, to) in starts)
            if (Route(state, region, start, to, standing, limit) is { } check && check.Distance > limit + 1e-6)
                return new(GfNames.DifficultCapRule, $"the unit moved in or through difficult terrain, so no model may move more than {limit:0.#}\"{(cap < GfNames.DifficultCap - 1e-6 ? " more" : "")} (p.11); {state.Projection.Models[id].Slot.Name} moved {check.Distance:0.0}\"");
        return null;
    }

    /// <summary>
    /// The least this model must move under a minimum (MinimumBreach). Where the table has room for the minimum in some direction, the minimum:
    /// p.13 "Aircraft can't move into the table edges to move less than 30"". Where it has room in none (p.6 allows "smaller areas", p.7 keeps
    /// every base on the table), owner ruling: it moves as far as it can in the direction taken, the room towards `toward` (Movement.RoomAlong);
    /// with no direction taken, as far as the table lets its whole base go (Movement.FarthestOnTable).
    /// </summary>
    public static double Minimum(GameState state, PlacedModel start, double minimum, Point2? toward = null)
    {
        var size = state.Board.Table.Size;
        var farthest = Movement.FarthestOnTable(start, size).Distance;
        if (farthest >= minimum) return minimum;
        return toward is { } t && t.DistanceTo(start.Placement.Position.Horizontal) > 1e-6 ? Movement.RoomAlong(start, t, size) : farthest;
    }

    /// <summary>The table has room for this model's minimum in some direction (Minimum).</summary>
    static bool HasRoomFor(GameState state, PlacedModel start, double minimum) => Movement.FarthestOnTable(start, state.Board.Table.Size).Distance >= minimum;

    /// <summary>
    /// A relocation of a listed model that the move steps do not read (GfAnswers.Unread): an Inside the Ask did not offer, a move out of a container
    /// the Ask is not exiting, a move farther than the table's diagonal, or one to the shelf. Refused under strict instead of applied as nothing.
    /// </summary>
    public static AnswerBreachFound? NotOfferedBreach(GameState state, MoveRegion region, Destinations answer) =>
        GfAnswers.Unread(state, region, answer) is { } m
            ? new(GfNames.MoveNotOfferedRule, $"the move Ask does not offer {state.Projection.Models[m.ModelId].Slot.Name} that relocation")
            : null;

    /// <summary>
    /// The geometry of each move on the table, against the Ask's own parameters, read as the steps apply it (GfAnswers.Moves), along the shortest
    /// path the base can take (Movement.Check; p.5 "always measure so that no part of its base moves further than the total distance"). In order:
    /// p.7 "models may also never move outside of the battlefield (no part of them, at any point)" (a model already standing off the table, which only a
    /// hand move can do, is not judged for it); p.11 "Units may not move through impassable terrain" (the volumes the Ask lists, refused when no path
    /// round them fits and the straight line meets one; one the model's base already touches where it stands, which again only a hand move can do,
    /// it may head out of but not move along or deeper into); p.7 "as long as no part of their bases
    /// move further than the total movement distance" (the Ask's Allowance); and p.11 "may not end their move partially overhanging gaps or terrain"
    /// with p.7 "physically impossible locations" (LandsInImpassable, against every impassable volume on the table, whether or not the Ask lists it:
    /// a Flying unit's Ask lists none, p.13). An Aircraft's move, its action's or another team's (the region's IgnoresTerrain), is exempt from the
    /// landing: p.13 "Aircraft ignore all units and terrain when moving and stopping". A model answered where it stands has not moved and is not judged. A disembark is
    /// judged by ExitPlacement instead: p.15 "must stay fully within 6" of it when exiting" and p.7's table edge. p.7's contact
    /// clauses are ContactBreach's; coherency is not judged.
    /// </summary>
    public static AnswerBreachFound? PathBreach(GameState state, MoveRegion region, Destinations answer)
    {
        var terrain = Battlefield.Terrain(state);
        var impassable = terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var landing = region.IgnoresTerrain ? [] : Declared(terrain, TerrainDeclarations.Impassable);
        var size = state.Board.Table.Size;
        foreach (var m in GfAnswers.Moves(state, region, answer))
        {
            if (m.To is not OnTable to) continue;
            var name = state.Projection.Models[m.ModelId].Slot.Name;
            if (m.From is Inside from)
            {
                if (ExitPlacement(state, m.ModelId, from.ContainerId, to.Placement, region.Containment?.ExitWithin ?? GfNames.ExitWithin) is null)
                    return new(GfNames.MoveExitRule, $"{name} must be placed fully within {region.Containment?.ExitWithin ?? GfNames.ExitWithin:0.#}\" of the transport, on the table and clear of impassable terrain (p.15, p.7, p.11)");
                continue;
            }
            // A placement from the shelf (GfReserves: "fully within 6" of it") is held to the region's anchor as a disembark is to its transport.
            if (m.From is OnShelf && region.Containment is { ExitingId: { } anchor, ExitWithin: { } reach })
            {
                if (ExitPlacement(state, m.ModelId, anchor, to.Placement, reach) is null)
                    return new(GfNames.MoveExitRule, $"{name} must be placed fully within {reach:0.#}\" of {state.Projection.Models[anchor].Slot.Name}, on the table and clear of impassable terrain (p.7, p.11)");
                continue;
            }
            if (Battlefield.Placed(state, m.ModelId) is not { } start) continue;
            var destination = to.Placement.Position.Horizontal;
            if (start.Placement.Position.Horizontal.DistanceTo(destination) <= 1e-6) continue;
            var check = Movement.Check(start, destination, region.Allowance, impassable, [], null, size);
            if (check.LeavesTable && Table.ContainsBase(size, start))
                return new(GfNames.MoveOffTableRule, $"{name} would leave the table: models may never move outside of the battlefield (p.7)");
            if (check.CrossesImpassable)
                return new(GfNames.MoveImpassableRule, $"{name} would move through impassable terrain (p.11)");
            if (LandsInImpassable(state, m.ModelId, to.Placement, landing))
                return new(GfNames.MoveImpassableRule, $"{name} would end its move in impassable terrain (p.11, p.7)");
            if (check.Distance > region.Allowance + 1e-6)
                return new(GfNames.MoveAllowanceRule, $"{name} moved {check.Distance:0.##}\", more than the {region.Allowance:0.#}\" allowed (p.7)");
        }
        return null;
    }

    /// <summary>
    /// p.13 Aircraft: "Aircraft can't move into the table edges to move less than 30"" — the move is mandatory. The region's MinDistance is that 30";
    /// every listed model on the table must move at least that far. Where the table has no room for it in any direction, the model must go as far
    /// as it can in the direction taken (Minimum): p.13 assumes 30" of room, which p.6's 6'x4' table always has, but p.6 allows "smaller areas"
    /// and p.7 keeps every base on the table. The answer is read as the move step applies it (GfAnswers.Moves): a model
    /// it leaves out, or whose relocation that reading drops, has not moved. Null when every model went far enough.
    /// </summary>
    public static AnswerBreachFound? MinimumBreach(GameState state, MoveRegion region, Destinations answer)
    {
        if (region.MinDistance is not { } minimum) return null;
        var moves = GfAnswers.Moves(state, region, answer);
        foreach (var id in region.ModelIds)
        {
            if (Battlefield.Placed(state, id) is not { } start) continue;
            var to = moves.FirstOrDefault(m => m.ModelId == id)?.To is OnTable on ? on.Placement.Position.Horizontal : start.Placement.Position.Horizontal;
            var required = Minimum(state, start, minimum, to);
            var moved = start.Placement.Position.Horizontal.DistanceTo(to);
            if (moved < required - 1e-6)
                return new(GfNames.AircraftMinimumRule, $"an Aircraft must move at least {required:0.#}\" (p.13), and {state.Projection.Models[id].Slot.Name} moved {moved:0.##}\"");
        }
        return null;
    }
}
