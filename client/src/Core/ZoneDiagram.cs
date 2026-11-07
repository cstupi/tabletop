using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>One team's zone in the diagram: its ordinal and its polygons in the diagram's pixels.</summary>
public sealed record DiagramZone(int Ordinal, IReadOnlyList<IReadOnlyList<Point2>> Polygons);

/// <summary>The setup Zones section's drawing: the table outline (Width × Height px), each zone, the style's words.</summary>
public sealed record ZoneDiagramView(double Width, double Height, IReadOnlyList<DiagramZone> Zones, string Style);

/// <summary>
/// Spec 2026-11-07-ux-redesign-1-client-shell.md → Setup → Zones: the table outline at the section's width and each team's zone, the same legs
/// the table draws (Session.Zones). The table's X runs right and its Y up the drawing, so the long edges lie across it as on the table.
/// </summary>
public static class ZoneDiagram
{
    /// <summary>The style of a draft's zones from their shape ids ("frontline/1" → "frontline"), null with none.</summary>
    public static string? StyleOf(IReadOnlyList<DeploymentZone> zones) => zones.Select(z => z.Shape.Split('/')[0]).FirstOrDefault();

    public static ZoneDiagramView Of(TableSize size, IReadOnlyList<DeploymentZone> zones, ZoneLegs legs, double width, double maxHeight)
    {
        var scale = Math.Min(width / size.Width, maxHeight / size.Length);
        double w = size.Width * scale, h = size.Length * scale;
        var resolved = new List<(int Ordinal, IReadOnlyList<Polygon> Polygons)>();
        foreach (var zone in zones.OrderBy(z => z.TeamOrdinal))
        {
            try { resolved.Add((zone.TeamOrdinal, ZoneStyles.Resolve(zone.Shape, size, legs))); } catch (ArgumentException) { }   // a shape this build does not know
        }
        Point2 At(Point2 v) => new(v.X * scale, h - v.Y * scale);   // as the setup camera shows the table: Y away from the viewer
        var drawn = resolved.Select(r => new DiagramZone(r.Ordinal, r.Polygons.Select(p => (IReadOnlyList<Point2>)p.Vertices.Select(At).ToList()).ToList())).ToList();
        var style = StyleOf(zones);
        return new ZoneDiagramView(w, h, drawn, style is null ? "No zones" : SetupWords.ZoneStyle(style));
    }
}
