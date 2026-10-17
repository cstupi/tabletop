using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

/// <summary>What a saved layout may hold. Problem returns null or the 422 detail.</summary>
public static class LayoutRules
{
    public const double MaxTableInches = 144;
    public const int MaxTerrain = 64, MaxMarkers = 32, MaxZones = 16, MaxVolumesPerPlacement = 16;

    public static string? Problem(TableState s, bool nameRequired)
    {
        if (Limits.Text(s.Name, "name", Limits.Name, required: nameRequired) is { } name) return name;
        if (!(s.Size.Width > 0 && s.Size.Width <= MaxTableInches && s.Size.Length > 0 && s.Size.Length <= MaxTableInches))
            return $"The table is more than 0 and at most {MaxTableInches}\" a side.";
        if (!s.Texture.IsValid || s.Terrain.Any(p => !p.Mesh.IsValid) || s.Markers.Any(m => !m.Visual.IsValid))
            return "Every texture, terrain mesh and marker visual needs a builtin or an assetId.";
        if (s.Terrain.Count > MaxTerrain) return $"A layout holds at most {MaxTerrain} terrain pieces.";
        if (s.Markers.Count > MaxMarkers) return $"A layout holds at most {MaxMarkers} markers.";
        if (s.Zones.Count > MaxZones) return $"A layout holds at most {MaxZones} deployment zones.";
        if (Builtin(s.Texture) is { } t) return t;
        foreach (var p in s.Terrain)
        {
            if (Builtin(p.Mesh) is { } b) return b;
            if (!Finite(p.Position) || !double.IsFinite(p.Yaw.Degrees) || !(p.Scale > 0 && p.Scale <= 100)) return "A terrain piece needs a finite position and yaw and a scale above 0 and at most 100.";
            if (p.Volumes.Count > MaxVolumesPerPlacement) return $"A terrain piece holds at most {MaxVolumesPerPlacement} volumes.";
            foreach (var v in p.Volumes) if (VolumeProblem(v.Volume) is { } vp) return vp;
        }
        foreach (var m in s.Markers)
        {
            if (Builtin(m.Visual) is { } b) return b;
            if (!Enum.IsDefined(m.Kind)) return "A marker is an objective or a token.";
            if (!Finite(m.Position) || !double.IsFinite(m.Yaw.Degrees) || !(m.Diameter > 0 && m.Diameter <= 24)) return "A marker needs a finite position and yaw and a diameter above 0 and at most 24\".";
            if (Limits.Text(m.Label, "A marker label", Limits.Name, required: false) is { } l) return l;
        }
        foreach (var z in s.Zones)
            if (Limits.Text(z.Shape, "A zone shape", Limits.Name) is { } zs) return zs;
        return null;
    }

    /// <summary>Shared by a layout's placement volumes.</summary>
    public static string? VolumeProblem(Volume v)
    {
        if (!Enum.IsDefined(v.Kind) || !Enum.IsDefined(v.Footprint.Kind)) return "A volume's kind or footprint is not one this server knows.";
        if (((int)v.Declarations & ~AllDeclarations) != 0) return "A volume declares a terrain property this server does not know.";
        double[] numbers = [v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top];
        if (numbers.Any(n => !double.IsFinite(n))) return "A volume's dimensions are finite numbers.";
        return Limits.Text(v.Notes, "Volume notes", Limits.Notes, required: false, multiline: true);
    }

    /// <summary>The asset references a layout makes, with the kinds each takes.</summary>
    public static IReadOnlyCollection<(Guid Id, AssetKind[] Kinds)> AssetRefs(TableState s)
    {
        var refs = new List<(Guid, AssetKind[])>();
        if (s.Texture.AssetId is { } t) refs.Add((t, AssetReferences.Texture));
        refs.AddRange(s.Terrain.Where(p => p.Mesh.AssetId is not null).Select(p => (p.Mesh.AssetId!.Value, AssetReferences.Terrain)));
        refs.AddRange(s.Markers.Where(m => m.Visual.AssetId is not null).Select(m => (m.Visual.AssetId!.Value, AssetReferences.Mesh)));
        return refs;
    }

    private static readonly int AllDeclarations = Enum.GetValues<TerrainDeclarations>().Aggregate(0, (a, d) => a | (int)d);
    private static bool Finite(Point3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    private static string? Builtin(VisualRef r) => r.Builtin is null ? null : Limits.Text(r.Builtin, "A builtin name", Limits.Name);
}
