using System.Text.RegularExpressions;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Setup;

/// <summary>
/// What one draft may hold (security review M4, M6; content-path review). Grimdark Future p.6 recommends 15 or more pieces; the client's
/// generators place 13 to 21 (p.18–19). The caps are several times that, and each piece's volumes, text and sizes are bounded so that
/// one draft's row write, broadcast and every later sight or cover query stay small.
/// </summary>
public sealed partial record DraftLimits(int Terrain = 64, int VolumesPerPiece = 8, int Markers = 32, int Label = 64, int Notes = 500)
{
    public static DraftLimits Default { get; } = new();
    public const double MinMarker = 0.5, MaxMarker = 6, MaxVolumeHeight = 24, MaxScale = 10;
    /// <summary>A built-in visual names a file in the client (setup phase: the server never reads the built-in catalog), so the server
    /// bounds its form: a short lowercase slug.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")] private static partial Regex BuiltinName();

    /// <summary>The first thing in the draft over a limit, or null. Positions may lie off the slab (generated pieces wait beside it) but not
    /// further than the longest table edge from its centre.</summary>
    public string? Breach(TableState d)
    {
        if (!Table.IsValid(d.Size)) return "the table size is out of range";
        if (Visual(d.Texture) is { } t) return $"the texture {t}";
        if (d.Terrain.Count > Terrain) return $"more than {Terrain} terrain pieces";
        if (d.Markers.Count > Markers) return $"more than {Markers} markers";
        foreach (var p in d.Terrain)
        {
            if (p.Volumes.Count > VolumesPerPiece) return $"terrain {p.Id} has more than {VolumesPerPiece} volumes";
            if (Visual(p.Mesh) is { } m) return $"terrain {p.Id}'s mesh {m}";
            if (!Near(d.Size, p.Position) || !double.IsFinite(p.Yaw.Degrees)) return $"terrain {p.Id} is not placed at a finite point near the table";
            if (!(double.IsFinite(p.Scale) && p.Scale > 0 && p.Scale <= MaxScale)) return $"terrain {p.Id}'s scale is out of range";
            foreach (var v in p.Volumes.Select(x => x.Volume))
            {
                if (!Enum.IsDefined(v.Kind) || ((int)v.Declarations & ~63) != 0) return $"terrain {p.Id} has an unknown volume kind or declaration";
                if (!(v.Bottom >= 0 && v.Bottom < v.Top && v.Top <= MaxVolumeHeight)) return $"terrain {p.Id} has a volume outside 0–{MaxVolumeHeight}\" or upside down";
                if (!(Finite(v.Offset.X) && Finite(v.Offset.Y) && Finite(v.Yaw.Degrees)) || v.Footprint.A * p.Scale > Table.MaxEdge || v.Footprint.B * p.Scale > Table.MaxEdge)
                    return $"terrain {p.Id} has a volume out of range";
                if (v.Notes is { Length: var n } && n > Notes) return $"terrain {p.Id} has notes over {Notes} characters";
            }
        }
        foreach (var m in d.Markers)
        {
            if (Visual(m.Visual) is { } v) return $"marker {m.Id}'s visual {v}";
            if (!Near(d.Size, m.Position) || !double.IsFinite(m.Yaw.Degrees)) return $"marker {m.Id} is not placed at a finite point near the table";
            if (!(m.Diameter >= MinMarker && m.Diameter <= MaxMarker)) return $"marker {m.Id}'s diameter is outside {MinMarker}–{MaxMarker}\"";
            if (m.Label is { Length: var n } && n > Label) return $"marker {m.Id}'s label is over {Label} characters";
            if (!Enum.IsDefined(m.Kind)) return $"marker {m.Id} has an unknown kind";
        }
        if (Zones(d.Zones) is { } z) return z;
        return null;
    }

    static readonly HashSet<string> Shapes = ZoneStyles.All.SelectMany(ZoneStyles.ShapesOf).ToHashSet();

    /// <summary>Each zone a built-in shape, once, with ordinals 1..n.</summary>
    static string? Zones(IReadOnlyList<DeploymentZone> zones)
    {
        if (zones.FirstOrDefault(z => !Shapes.Contains(z.Shape)) is { } bad) return "an unknown zone shape";
        if (zones.Select(z => z.Shape).Distinct().Count() != zones.Count) return "a zone shape twice";
        if (!zones.Select(z => z.TeamOrdinal).Order().SequenceEqual(Enumerable.Range(1, zones.Count))) return "zone ordinals are not 1..n";
        return null;
    }

    /// <summary>SetZoneStyle: the style is built in and the shapes are its own (SwapZones reorders them).</summary>
    public static string? ZoneStyle(string style, IReadOnlyList<string> shapes) =>
        !ZoneStyles.All.Contains(style) ? "an unknown zone style"
        : shapes.Any(s => !ZoneStyles.ShapesOf(style).Contains(s)) ? "a shape that is not the style's own" : null;

    string? Visual(VisualRef v) => v.Builtin is { } b && !BuiltinName().IsMatch(b) ? "is not a built-in name" : null;
    static bool Finite(double v) => double.IsFinite(v);
    static bool Near(TableSize size, Point3 p) =>
        Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Math.Abs(p.X - size.Width / 2) <= Table.MaxEdge && Math.Abs(p.Y - size.Length / 2) <= Table.MaxEdge
        && Math.Abs(p.Z) <= MaxVolumeHeight * 2;
}
