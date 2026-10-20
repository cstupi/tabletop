using System.Text.RegularExpressions;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>
/// The text limits the servers hold names and labels to, mirrored so a field stops at the limit and a dialog says why before the
/// server refuses: management's <c>Limits</c> (account, game and layout names) and the game server's <c>DraftLimits</c> (a marker
/// label, a volume's notes). A change there is a change here.
/// </summary>
public static partial class TextLimits
{
    /// <summary>Account, game and layout names (management <c>Limits.Name</c>).</summary>
    public const int Name = 64;
    /// <summary>A setup marker's label and a condition's name (<c>DraftLimits.Label</c>, <c>MenuLimits</c>).</summary>
    public const int Label = 64;
    /// <summary>A terrain volume's notes in a draft (<c>DraftLimits.Notes</c>).</summary>
    public const int Notes = 500;

    /// <summary>Null when management would take the name, else what it would say: required after trimming, at most 64 characters, no
    /// control or bidirectional formatting character.</summary>
    public static string? NameProblem(string? value, string field = "A name")
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return $"{field} is required.";
        if (name.Length > Name) return $"{field} is at most {Name} characters.";
        if (name.Any(ch => char.IsControl(ch) || IsBidiFormat(ch))) return $"{field} contains a control or bidirectional formatting character.";
        return null;
    }

    /// <summary>The name management gives a new account, <c>Player-</c> and four hex digits: Home asks the player to choose one.</summary>
    public static bool IsDefaultName(string name) => DefaultName().IsMatch(name);

    static bool IsBidiFormat(char ch) => ch is '‎' or '‏' or '؜' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    [GeneratedRegex("^Player-[0-9a-f]{4}$")] private static partial Regex DefaultName();
}

/// <summary>
/// What a setup draft may hold, from the game server's <c>DraftLimits</c> and the table's own bound (<c>Table.MaxEdge</c>): the toolbar
/// counts against them in its headings and turns an add that would pass one into a message instead of a refused edit.
/// </summary>
public static class SetupLimits
{
    public const int Terrain = 64, Markers = 32;
    /// <summary>The toolbar's smallest table edge; the largest is the table's own bound.</summary>
    public const double MinEdge = 12, MaxEdge = Tabletop.Geometry.Table.MaxEdge;

    public static string SizeHint => $"Width × length, {MinEdge}–{MaxEdge}\" a side";

    public static string TerrainHeading(TableState? d) => d is null ? "Terrain" : $"Terrain ({d.Terrain.Count}/{Terrain})";
    public static string MarkersHeading(TableState? d) => d is null ? "Markers" : $"Markers ({d.Markers.Count}/{Markers})";

    /// <summary>Null when <paramref name="adding"/> more pieces fit.</summary>
    public static string? CannotAddTerrain(TableState? d, int adding = 1) => d switch
    {
        null => "The table has not arrived yet.",
        _ when d.Terrain.Count + adding <= Terrain => null,
        _ when adding == 1 => $"The table holds at most {Terrain} terrain pieces: remove some first.",
        _ => $"These {adding} pieces would take the table past {Terrain} terrain pieces (it has {d.Terrain.Count}): remove some first.",
    };

    /// <summary>Null when the table has terrain for Clear terrain to remove.</summary>
    public static string? CannotClearTerrain(TableState? d) => d switch
    {
        null => "The table has not arrived yet.",
        { Terrain.Count: 0 } => "There is no terrain to clear.",
        _ => null,
    };

    /// <summary>Where a new setup marker goes: the table's centre, else the first spot on rings 6" apart about it (eight, then sixteen,
    /// … points a ring) whose marker would sit on the table and clear every marker already placed, base to base. The centre when none is.</summary>
    public static Point3 FreeSpot(TableSize size, IReadOnlyList<Marker> markers, double diameter)
    {
        var centre = new Point2(size.Width / 2, size.Length / 2);
        bool Free(Point2 p) => p.X - diameter / 2 >= 0 && p.X + diameter / 2 <= size.Width && p.Y - diameter / 2 >= 0 && p.Y + diameter / 2 <= size.Length
            && markers.All(m => m.Position.Horizontal.DistanceTo(p) >= (m.Diameter + diameter) / 2);
        if (Free(centre)) return new Point3(centre.X, centre.Y, 0);
        for (var ring = 1; ring * 6 <= Math.Max(size.Width, size.Length); ring++)
            for (var i = 0; i < 8 * ring; i++)
            {
                var angle = 2 * Math.PI * i / (8 * ring);
                var p = new Point2(Math.Round(centre.X + ring * 6 * Math.Cos(angle), 2), Math.Round(centre.Y + ring * 6 * Math.Sin(angle), 2));
                if (Free(p)) return new Point3(p.X, p.Y, 0);
            }
        return new Point3(centre.X, centre.Y, 0);
    }

    public static string? CannotAddMarker(TableState? d) => d switch
    {
        null => "The table has not arrived yet.",
        _ when d.Markers.Count < Markers => null,
        _ => $"The table holds at most {Markers} markers: remove some first.",
    };
}
