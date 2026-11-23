using System.Globalization; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>
/// Where a deploy pick's unit may stand, from the reach its Ask carries (PieceSelection.ZoneReach: a Scout's 12" or 3.6's 9", 0 inside the zone): the
/// placing team's deployment zone grown by the reach (ZoneReach.Outline), cut to the table, as one ring the table draws in the team's colour, labelled
/// `Scout 9"`; and each ghost whose base is not fully within it (ZoneStyles.BaseFullyWithin, the server's ZoneBreach), as the decision box's words.
/// Every number is the server's; the zone's shape comes from the board and Welcome's legs.
/// </summary>
public static class ZoneReachArea
{
    /// <summary>The placing unit's team zone (shape and team ordinal) on the table, or null when the table has none for it.</summary>
    static (string Shape, int Ordinal, TableSize Size)? ZoneOf(Projection p, GameInfo game, Guid unit) =>
        SheetFocus.TeamOf(p, game, unit) is { } team && p.Board.Table.Zones.FirstOrDefault(z => z.TeamOrdinal == team.Ordinal) is { } zone
            ? (zone.Shape, team.Ordinal, p.Board.Table.Size) : null;

    /// <summary>`Scout 9"`; a reach of 0 is the zone itself.</summary>
    public static string Label(double reach) => reach > 0 ? $"Scout {Inches(reach)}" : "Your zone";

    /// <summary>The ring, or null when the pick carries no reach for its unit or the table has no zone for the team.</summary>
    public static RangeRing? Of(Projection p, GameInfo game, MidDeploy mid, ZoneLegs legs)
    {
        if (mid.ZoneReach is not { } reach || ZoneOf(p, game, mid.UnitId) is not { } zone) return null;
        IReadOnlyList<Polygon> polygons;
        try { polygons = ZoneStyles.Resolve(zone.Shape, zone.Size, legs); } catch (ArgumentException) { return null; }
        return new RangeRing(reach, Label(reach), ZoneReach.Outline(polygons, reach, zone.Size), RingKind.Reach);
    }

    /// <summary>The team ordinal whose colour the ring takes, or null.</summary>
    public static int? OrdinalOf(Projection p, GameInfo game, MidDeploy mid) => ZoneOf(p, game, mid.UnitId)?.Ordinal;

    /// <summary>Each proposed model whose base is not fully within the reach: "placed beyond 9\" of its deployment zone", or "placed outside its deployment
    /// zone" at 0.</summary>
    public static IReadOnlyDictionary<Guid, string> Breaches(Projection p, GameInfo game, MidDeploy mid, ZoneLegs legs, IReadOnlyDictionary<Guid, Placement> proposed)
    {
        var found = new Dictionary<Guid, string>();
        if (mid.ZoneReach is not { } reach || ZoneOf(p, game, mid.UnitId) is not { } zone) return found;
        foreach (var (id, at) in proposed)
        {
            if (p.Models.GetValueOrDefault(id) is not { } m) continue;
            var placed = new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), at);
            bool inside;
            try { inside = reach > 0 ? ZoneStyles.BaseFullyWithin(zone.Shape, zone.Size, placed, reach, legs) : ZoneStyles.BaseFullyWithin(zone.Shape, zone.Size, placed, legs); }
            catch (ArgumentException) { return found; }
            if (!inside) found[id] = reach > 0 ? $"placed beyond {Inches(reach)} of its deployment zone" : "placed outside its deployment zone";
        }
        return found;
    }

    /// <summary>A model's earlier words first (contacts, keep-away), else its reach words; a pick with no reach adds none.</summary>
    public static IReadOnlyDictionary<Guid, string> With(IReadOnlyDictionary<Guid, string> earlier, Projection p, GameInfo game, MidDeploy? mid, ZoneLegs legs,
        IReadOnlyDictionary<Guid, Placement> proposed)
    {
        if (mid?.ZoneReach is null) return earlier;
        var all = new Dictionary<Guid, string>(earlier);
        foreach (var (id, words) in Breaches(p, game, mid, legs, proposed)) all.TryAdd(id, words);
        return all;
    }

    static string Inches(double d) => d.ToString("0.#", CultureInfo.InvariantCulture) + "\"";
}
