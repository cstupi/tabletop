using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The setup edits the toolbar and the terrain card send, one function per control. The browser's setup/edits.ts and catalog.ts build the same.</summary>
public static class SetupEdits
{
    /// <summary>Beside the table: x = W + 4 + 8·⌊3r⌋, y = 4 + 8·⌊4r'⌋, z 0, from two draws in [0, 1), x first.</summary>
    public static Point3 Beside(TableSize size, Func<double> random)
    {
        var x = size.Width + 4 + Math.Floor(random() * 3) * 8;
        var y = 4 + Math.Floor(random() * 4) * 8;
        return new Point3(x, y, 0);
    }

    public static SetTableSize TableSize(double width, double length) => new(new TableSize(width, length));
    public static SetTexture Texture(string builtin) => new(VisualRef.OfBuiltin(builtin));

    public static AddTerrain AddBuiltin(string name, TableSize size, Func<double> random) => new(BuiltinContent.Place(name, Beside(size, random), new Yaw(0)));

    public static AddTerrain AddAsset(Guid assetId, IReadOnlyList<Volume> volumes, TableSize size, Func<double> random) =>
        new(new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(assetId), Beside(size, random), new Yaw(0), 1, Fresh(volumes)));

    public static AddSetupMarker Objective(TableState draft) =>
        new(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, SetupLimits.FreeSpot(draft.Size, draft.Markers, 3), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring")));

    public static SetZoneStyle Zones(string style) => new(style, ZoneStyles.ShapesOf(style));
    public static SwapZones SwapZones(int teamOrdinalA, int teamOrdinalB) => new(teamOrdinalA, teamOrdinalB);

    public static ReplaceDraft ClearTerrain(TableState draft) => new(draft with { Terrain = [] });
    public static ReplaceDraft Random(TableState draft, RandomTable laid) => new(LayoutChoices.WithTerrain(draft, laid));
    public static ReplaceDraft Load(TableState layout) => new(layout);

    public static ScaleTerrain Scale(Guid placementId, double scale) => new(placementId, scale);
    public static SwapTerrainMesh SwapBuiltin(Guid placementId, string builtin) => new(placementId, VisualRef.OfBuiltin(builtin));
    public static SwapTerrainMesh SwapAsset(Guid placementId, Guid assetId) => new(placementId, VisualRef.OfAsset(assetId));
    public static RemoveTerrain RemoveTerrain(Guid placementId) => new(placementId);
    public static RemoveSetupMarker RemoveMarker(Guid markerId) => new(markerId);

    /// <summary>Reset volumes: the given volumes under new ids.</summary>
    public static OverrideTerrainVolumes ResetVolumes(Guid placementId, IReadOnlyList<Volume> volumes) => new(placementId, Fresh(volumes));

    /// <summary>The placement's whole volume list with one volume changed.</summary>
    public static OverrideTerrainVolumes ChangeVolume(TerrainPlacement piece, Guid volumeId, Func<Volume, Volume> change) =>
        new(piece.Id, piece.Volumes.Select(v => v.Id != volumeId ? v : v with { Volume = change(v.Volume) }).ToList());

    public static Func<Volume, Volume> WithKind(VolumeKind kind) => v => v with { Kind = kind };
    /// <summary>A circle's radius sets A and B; a rect's A is its width.</summary>
    public static Func<Volume, Volume> WithFootprintA(double value) =>
        v => v with { Footprint = v.Footprint.Kind == FootprintKind.Circle ? new Footprint(FootprintKind.Circle, value, value) : v.Footprint with { A = value } };
    public static Func<Volume, Volume> WithFootprintB(double value) => v => v with { Footprint = v.Footprint with { B = value } };
    public static Func<Volume, Volume> WithBottom(double bottom) => v => v with { Bottom = bottom };
    public static Func<Volume, Volume> WithTop(double top) => v => v with { Top = top };
    public static Func<Volume, Volume> WithDeclaration(TerrainDeclarations flag, bool on) =>
        v => v with { Declarations = on ? v.Declarations | flag : v.Declarations & ~flag };
    /// <summary>Clipped to TextLimits.Notes; "" is null.</summary>
    public static Func<Volume, Volume> WithNotes(string text) =>
        v => v with { Notes = text.Length == 0 ? null : text.Length > TextLimits.Notes ? text[..TextLimits.Notes] : text };

    static List<TerrainVolume> Fresh(IReadOnlyList<Volume> volumes) => volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v)).ToList();
}
