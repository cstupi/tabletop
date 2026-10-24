using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Setup;

/// <summary>TableState × SetupEdit → TableState. Pure, so the setup phase is headless logic: edits in, draft out. Null with a reason when the edit names nothing.</summary>
public static class DraftEditor
{
    /// <summary>The edit applied, then the result held to the draft limits (DraftLimits; removals and swaps always apply, so a draft over a
    /// limit can be brought back under it).</summary>
    public static TableState? Apply(TableState d, SetupEdit edit, out string? missing, DraftLimits? limits = null)
    {
        if (edit is SetZoneStyle z && DraftLimits.ZoneStyle(z.Style, z.Shapes) is { } style) return Miss(out missing, style);
        var next = ApplyCore(d, edit, out missing);
        if (next is null || edit is RemoveTerrain or RemoveSetupMarker or SwapZones) return next;
        return (limits ?? DraftLimits.Default).Breach(next) is { } breach ? Miss(out missing, breach) : next;
    }

    static TableState? ApplyCore(TableState d, SetupEdit edit, out string? missing)
    {
        missing = null;
        // A JSON property left out never reaches the VisualRef converter, so the parent record receives default(VisualRef):
        // both fields null, IsValid false. The row's check constraint (builtin is null) <> (asset_id is null) would then
        // reject the write after the board was already broadcast, so this is caught here, before any of it happens.
        if (InvalidVisual(edit) is { } bad) return Miss(out missing, $"{bad} is not a valid visual");
        // Geometry throws at construction for an outline without area, and every later cover, sight or settle call would fault the game.
        if (Degenerate(d, edit) is { } flat) return Miss(out missing, $"{flat} is degenerate");
        switch (edit)
        {
            case SetTableSize s: return d with { Size = s.Size };
            case SetTexture t: return d with { Texture = t.Texture };
            case AddTerrain a: return d.Terrain.Any(t => t.Id == a.Placement.Id) ? Miss(out missing, $"terrain {a.Placement.Id} already placed") : Rest(d with { Terrain = [.. d.Terrain, a.Placement] }, a.Placement.Id);
            case MoveTerrain m: return Rest(Terrain(d, m.PlacementId, t => t with { Position = m.Position, Yaw = m.Yaw }, out missing), m.PlacementId);
            case ScaleTerrain s: return Rest(Terrain(d, s.PlacementId, t => t with { Scale = s.Scale }, out missing), s.PlacementId);
            case OverrideTerrainVolumes o: return Rest(Terrain(d, o.PlacementId, t => t with { Volumes = o.Volumes }, out missing), o.PlacementId);
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
            case ReplaceDraft r: return Adopt(r.Draft, d);
            default: return Miss(out missing, $"unknown edit {edit.GetType().Name}");
        }
    }

    /// <summary>A loaded layout becomes this game's draft: it keeps the draft row's own identity, and every child gets a fresh id.
    /// terrain_placement, terrain_volume, marker and deployment_zone ids are global primary keys and layouts share the table_state
    /// tree with drafts, so reusing the layout's would make the draft write a unique violation (management plan: on replace_draft
    /// from a loaded layout, regenerate every child id).</summary>
    static TableState Adopt(TableState layout, TableState draft) => layout with
    {
        Id = draft.Id,
        Name = draft.Name,
        Terrain = layout.Terrain.Select(t => t with { Id = Guid.CreateVersion7(), Volumes = t.Volumes.Select(v => v with { Id = Guid.CreateVersion7() }).ToList() }).ToList(),
        Markers = layout.Markers.Select(m => m with { Id = Guid.CreateVersion7() }).ToList(),
        Zones = layout.Zones.Select(z => z with { Id = Guid.CreateVersion7() }).ToList(),
    };

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

    static bool Positive(double v) => double.IsFinite(v) && v > 0;
    static bool Flat(TerrainPlacement t) => !Positive(t.Scale) || t.Volumes.Any(v => !v.Volume.Footprint.IsValid(t.Scale));
    static bool Flat(TableSize s) => !Table.IsValid(s);
    static TerrainPlacement? Placed(TableState d, Guid id) => d.Terrain.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Names the first outline an edit carries that geometry could not build, or null. The tests are geometry's own (Footprint.IsValid,
    /// Table.IsValid), on the placement as the edit would leave it: a new scale over the placement's volumes, new volumes under its scale.
    /// </summary>
    static string? Degenerate(TableState d, SetupEdit edit) => edit switch
    {
        SetTableSize s when Flat(s.Size) => "the table size",
        AddTerrain a when Flat(a.Placement) => $"terrain {a.Placement.Id}",
        ScaleTerrain s when !Positive(s.Scale) || (Placed(d, s.PlacementId) is { } t && Flat(t with { Scale = s.Scale })) => $"terrain {s.PlacementId}'s scale",
        OverrideTerrainVolumes o when Flat(new TerrainPlacement(o.PlacementId, default, default, default, Placed(d, o.PlacementId)?.Scale ?? 1, o.Volumes)) => $"terrain {o.PlacementId}'s volumes",
        ReplaceDraft r when Flat(r.Draft.Size) => "the layout's table size",
        ReplaceDraft r when r.Draft.Terrain.FirstOrDefault(Flat) is { } t => $"the layout terrain {t.Id}",
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

    /// <summary>Client UX spec → Physical collision; held pieces lift over spec: the edited piece rests on its open surface nearest the Z sent, over the
    /// Solid tops under its footprints, its own volumes ignored, so a piece set under a bridge with room stays there.</summary>
    static TableState? Rest(TableState? d, Guid id)
    {
        if (d?.Terrain.FirstOrDefault(t => t.Id == id) is not { } piece) return d;
        var z = Collision.RestNear(piece.WorldVolumes(), d.Terrain.Where(t => t.Id != id).SelectMany(t => t.WorldVolumes()).ToList(), piece.Position.Z);
        return d with { Terrain = d.Terrain.Select(t => t.Id == id ? t with { Position = t.Position with { Z = z } } : t).ToList() };
    }

    static TableState? Miss(out string? missing, string detail) { missing = detail; return null; }
}
