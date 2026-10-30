using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One volume on the terrain card: its kind, footprint and height range on the table (scale and elevation applied), its
/// declarations in TerrainLabel's order (<c>Open</c> with none) and its notes.</summary>
public sealed record TerrainCardVolume(Guid Id, string Kind, string Footprint, string Heights, IReadOnlyList<string> Declarations, string? Notes)
{
    public bool Equals(TerrainCardVolume? o) => o is not null && Id == o.Id && Kind == o.Kind && Footprint == o.Footprint && Heights == o.Heights
        && Notes == o.Notes && Declarations.SequenceEqual(o.Declarations);
    public override int GetHashCode() => HashCode.Combine(Id, Kind, Footprint, Heights, Notes, Declarations.Count);
}

/// <summary>
/// What the terrain card draws for the selected terrain piece, in the unit card's slot (the card replaces the unit card while
/// terrain is selected): the name (TerrainLabel), where the mesh comes from, facing 0–359°, scale, the piece's height range and each
/// volume. Editable in setup, where the card carries the piece's edits; read-only in play. Equal by value, lists element by element,
/// so the card rebuilds only when what it shows changed; a drag across the table changes nothing it shows.
/// </summary>
public sealed record TerrainCardContent(Guid PieceId, string Name, string Source, int Facing, double Scale, string Height, IReadOnlyList<TerrainCardVolume> Volumes, bool Editable)
{
    /// <summary>The table the card reads: the draft in setup, the board in play, none in the lobby.</summary>
    public static TableState? TableOf(GameStatus? status, TableState? draft, Projection? p) => status switch
    {
        GameStatus.Setup => draft,
        GameStatus.Playing or GameStatus.Ended => p?.Board.Table,
        _ => null,
    };

    /// <summary>The selected piece's card when it is a terrain piece on the table, in setup or play.</summary>
    public static TerrainCardContent? Of(TableState? table, GameStatus? status, Guid? selected, IReadOnlyDictionary<string, string>? words = null)
    {
        if (status is not (GameStatus.Setup or GameStatus.Playing or GameStatus.Ended) || selected is not { } id
            || table?.Terrain.FirstOrDefault(t => t.Id == id) is not { } t) return null;
        var source = t.Mesh.Builtin is { } b ? $"Built-in {b}" : $"Library asset {Names.Short(t.Mesh.AssetId!.Value)}";
        var height = t.Volumes.Count == 0 ? "" : $"{TerrainLabel.Inches(t.Position.Z + t.Volumes.Min(v => v.Volume.Bottom) * t.Scale)}–{TerrainLabel.Inches(t.Position.Z + t.Volumes.Max(v => v.Volume.Top) * t.Scale)}\"";
        var volumes = t.Volumes.Select(tv =>
        {
            var v = tv.Volume; var flags = TerrainLabel.FlagsOf(v, words);
            return new TerrainCardVolume(tv.Id, v.Kind.ToString(), FootprintOf(v.Footprint, t.Scale), TerrainLabel.HeightsOf(t, v), flags.Count == 0 ? ["Open"] : flags, v.Notes);
        }).ToList();
        return new TerrainCardContent(t.Id, TerrainLabel.NameOf(t), source, FacingOf(t.Yaw), t.Scale, height, volumes, status == GameStatus.Setup);
    }

    /// <summary>Degrees counter-clockwise from above, whole, 0–359.</summary>
    public static int FacingOf(Yaw yaw) => (int)(((Math.Round(yaw.Degrees) % 360) + 360) % 360);

    /// <summary>A rect's width × length, a circle's diameter, at the piece's scale.</summary>
    public static string FootprintOf(Footprint f, double scale) => f.Kind == FootprintKind.Circle
        ? $"{TerrainLabel.Inches(2 * f.A * scale)}\" across"
        : $"{TerrainLabel.Inches(f.A * scale)} × {TerrainLabel.Inches(f.B * scale)}\"";

    public bool Equals(TerrainCardContent? o) => o is not null && PieceId == o.PieceId && Name == o.Name && Source == o.Source && Facing == o.Facing
        && Scale == o.Scale && Height == o.Height && Editable == o.Editable && Volumes.SequenceEqual(o.Volumes);
    public override int GetHashCode() => HashCode.Combine(PieceId, Name, Source, Facing, Scale, Height, Editable, Volumes.Count);
}

/// <summary>A selected setup marker on the same card: its kind, diameter and label, with Remove. Setup only: in play a marker is
/// the marker menu's.</summary>
public sealed record MarkerCardContent(Guid PieceId, string Name, string Diameter, string? Label)
{
    public static MarkerCardContent? Of(TableState? table, GameStatus? status, Guid? selected) =>
        status == GameStatus.Setup && selected is { } id && table?.Markers.FirstOrDefault(m => m.Id == id) is { } m
            ? new MarkerCardContent(m.Id, m.Kind.ToString(), $"{TerrainLabel.Inches(m.Diameter)}\" across", m.Label)
            : null;
}
