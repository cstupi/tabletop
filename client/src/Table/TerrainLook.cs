using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>
/// The surface grain on built-in terrain (spec 2026-10-19-tabletop-look-design.md → Terrain): each surface of a built-in mesh takes
/// its <see cref="SurfaceFamily"/>'s detail print, world-space triplanar, multiplied into its vertex paint, with that print's normal
/// map and a matte roughness. Liquid and plain surfaces keep the glTF material as it is. A material is changed once; the meshes'
/// duplicates share it.
/// </summary>
public static class TerrainLook
{
    /// <summary>Per family: the print, inches per repeat, the print's relief in inches, the roughness (null keeps the glTF's) and the
    /// normal depth.</summary>
    static readonly Dictionary<SurfaceFamily, (string File, float Inches, float Relief, float? Roughness, float Depth)> Grains = new()
    {
        [SurfaceFamily.Stone] = ("res://content/table/detail-stone.png", 2.5f, 0.02f, 0.9f, 1f),
        [SurfaceFamily.Ground] = ("res://content/table/detail-ground.png", 1.2f, 0.012f, 0.95f, 1f),
        [SurfaceFamily.Foliage] = ("res://content/table/detail-foliage.png", 0.8f, 0.006f, 0.8f, 0.6f),
        [SurfaceFamily.Wood] = ("res://content/table/detail-wood.png", 1.5f, 0.01f, 0.88f, 1f),
        [SurfaceFamily.Metal] = ("res://content/table/detail-metal.png", 2f, 0.006f, null, 0.8f),
    };

    /// <summary>The grain prints sit around 0.88; the albedo factor lifts them back to the paint's own value.</summary>
    const float Lift = 1.12f;
    /// <summary>Foliage is lit through and around its many small cards, which the bake's wash and the shadow pass both darken: it is
    /// lifted further, toward the bright clump foliage of hobby trees.</summary>
    const float FoliageLift = 1.4f;
    const string Done = "tabletop_terrain_look";

    public static void Apply(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh })
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
                if (mesh.SurfaceGetMaterial(i) is StandardMaterial3D material) Dress(material);
        foreach (var child in node.GetChildren()) Apply(child);
    }

    static void Dress(StandardMaterial3D m)
    {
        if (m.HasMeta(Done)) return;
        m.SetMeta(Done, true);
        var blended = m.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
        var family = SurfaceFamilies.Of(m.ResourceName ?? "", m.Metallic, blended);
        if (!Grains.TryGetValue(family, out var grain) || PrintTextures.Load(grain.File, grain.Relief / (grain.Inches / 512)) is not { } print) return;
        m.AlbedoTexture = print.Albedo;
        var lift = family == SurfaceFamily.Foliage ? FoliageLift : Lift;
        m.AlbedoColor = new Color(m.AlbedoColor.R * lift, m.AlbedoColor.G * lift, m.AlbedoColor.B * lift, m.AlbedoColor.A);
        m.NormalEnabled = true; m.NormalTexture = print.Normal; m.NormalScale = grain.Depth;
        m.Uv1Triplanar = true; m.Uv1WorldTriplanar = true; m.Uv1TriplanarSharpness = 4f;
        m.Uv1Scale = Vector3.One / grain.Inches;
        m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        if (grain.Roughness is { } r) m.Roughness = r;
        m.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        if (family != SurfaceFamily.Foliage) return;
        // Leaves face every way: wrapped diffuse and light through from behind keep a canopy from going black on its shadowed side.
        m.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.LambertWrap;
        m.BacklightEnabled = true; m.Backlight = new Color(0.45f, 0.5f, 0.3f);
    }
}
