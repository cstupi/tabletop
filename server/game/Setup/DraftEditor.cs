using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Setup;

/// <summary>TableState × SetupEdit → TableState. Pure, so the setup phase is headless logic: edits in, draft out. Null with a reason when the edit names nothing.</summary>
public static class DraftEditor
{
    public static TableState? Apply(TableState d, SetupEdit edit, out string? missing)
    {
        missing = null;
        // A JSON property left out never reaches the VisualRef converter, so the parent record receives default(VisualRef):
        // both fields null, IsValid false. The row's check constraint (builtin is null) <> (asset_id is null) would then
        // reject the write after the board was already broadcast, so this is caught here, before any of it happens.
        if (InvalidVisual(edit) is { } bad) return Miss(out missing, $"{bad} is not a valid visual");
        switch (edit)
        {
            case SetTableSize s: return d with { Size = s.Size };
            case SetTexture t: return d with { Texture = t.Texture };
            case AddTerrain a: return d.Terrain.Any(t => t.Id == a.Placement.Id) ? Miss(out missing, $"terrain {a.Placement.Id} already placed") : d with { Terrain = [.. d.Terrain, a.Placement] };
            case MoveTerrain m: return Terrain(d, m.PlacementId, t => t with { Position = m.Position, Yaw = m.Yaw }, out missing);
            case ScaleTerrain s: return Terrain(d, s.PlacementId, t => t with { Scale = s.Scale }, out missing);
            case OverrideTerrainVolumes o: return Terrain(d, o.PlacementId, t => t with { Volumes = o.Volumes }, out missing);
            case SwapTerrainMesh s: return Terrain(d, s.PlacementId, t => t with { Mesh = s.Mesh }, out missing);
            case RemoveTerrain r: return d.Terrain.Any(t => t.Id == r.PlacementId) ? d with { Terrain = d.Terrain.Where(t => t.Id != r.PlacementId).ToList() } : Miss(out missing, $"no terrain {r.PlacementId}");
            case AddSetupMarker a: return d.Markers.Any(m => m.Id == a.Marker.Id) ? Miss(out missing, $"marker {a.Marker.Id} already placed") : d with { Markers = [.. d.Markers, a.Marker] };
            case MoveSetupMarker m: return d.Markers.Any(x => x.Id == m.MarkerId) ? d with { Markers = d.Markers.Select(x => x.Id == m.MarkerId ? x with { Position = m.Position, Yaw = m.Yaw } : x).ToList() } : Miss(out missing, $"no marker {m.MarkerId}");
            case RemoveSetupMarker r: return d.Markers.Any(x => x.Id == r.MarkerId) ? d with { Markers = d.Markers.Where(x => x.Id != r.MarkerId).ToList() } : Miss(out missing, $"no marker {r.MarkerId}");
            case SetZoneStyle z: return d with { Zones = z.Shapes.Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList() };
            case SwapZones s:
                var zoneA = d.Zones.FirstOrDefault(z => z.TeamOrdinal == s.TeamOrdinalA); var zoneB = d.Zones.FirstOrDefault(z => z.TeamOrdinal == s.TeamOrdinalB);
                if (zoneA is null || zoneB is null) return Miss(out missing, "no zone for one of the ordinals");
                return d with { Zones = d.Zones.Select(z => z.TeamOrdinal == zoneA.TeamOrdinal ? z with { Shape = zoneB.Shape } : z.TeamOrdinal == zoneB.TeamOrdinal ? z with { Shape = zoneA.Shape } : z).ToList() };
            case ReplaceDraft r: return r.Draft with { Id = d.Id, Name = d.Name };
            default: return Miss(out missing, $"unknown edit {edit.GetType().Name}");
        }
    }

    /// <summary>Names the first invalid VisualRef an edit references, or null when every one it carries is valid.</summary>
    static string? InvalidVisual(SetupEdit edit) => edit switch
    {
        SetTexture t when !t.Texture.IsValid => "the texture",
        AddTerrain a when !a.Placement.Mesh.IsValid => $"terrain {a.Placement.Id}'s mesh",
        SwapTerrainMesh s when !s.Mesh.IsValid => $"terrain {s.PlacementId}'s new mesh",
        AddSetupMarker a when !a.Marker.Visual.IsValid => $"marker {a.Marker.Id}'s visual",
        ReplaceDraft r when !r.Draft.Texture.IsValid => "the layout's texture",
        ReplaceDraft r when r.Draft.Terrain.FirstOrDefault(t => !t.Mesh.IsValid) is { } badTerrain => $"the layout terrain {badTerrain.Id}'s mesh",
        ReplaceDraft r when r.Draft.Markers.FirstOrDefault(m => !m.Visual.IsValid) is { } badMarker => $"the layout marker {badMarker.Id}'s visual",
        _ => null,
    };

    /// <summary>The corner is the origin: a position is on the slab when 0 ≤ x ≤ width and 0 ≤ y ≤ length.</summary>
    public static bool OnSlab(TableState d, Point3 p) => p.X >= 0 && p.X <= d.Size.Width && p.Y >= 0 && p.Y <= d.Size.Length;

    /// <summary>End-setup's rule: a generated piece never dragged onto the slab is dropped (setup phase → Terrain generators; rulebook p.17, the Random style: "if it's impossible to place the terrain piece, simply remove it").</summary>
    public static TableState DropOffSlab(TableState d) => d with { Terrain = d.Terrain.Where(t => OnSlab(d, t.Position)).ToList() };

    static TableState? Terrain(TableState d, Guid id, Func<TerrainPlacement, TerrainPlacement> change, out string? missing)
    {
        missing = null;
        return d.Terrain.Any(t => t.Id == id) ? d with { Terrain = d.Terrain.Select(t => t.Id == id ? change(t) : t).ToList() } : Miss(out missing, $"no terrain {id}");
    }

    static TableState? Miss(out string? missing, string detail) { missing = detail; return null; }
}
