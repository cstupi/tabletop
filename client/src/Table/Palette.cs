using Godot;
using Tabletop.Client.Theming;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

public static class Palette
{
    public static Color Team(int ordinal) => Ui.Team(ordinal);
    public static readonly Color Proxy = new(0.3f, 0.9f, 0.4f, 0.45f);       // semi-transparent green (asset library)
    /// <summary>An unhighlighted model's proxy: Proxy blended halfway toward its team's colour, so the sides tell apart and a mesh keeps its look; Proxy with no team.</summary>
    public static Color Tinted(int? teamOrdinal) => teamOrdinal is { } o ? Proxy.Lerp(Team(o), 0.5f) with { A = Proxy.A } : Proxy;
    public static readonly Color Selected = new(1f, 1f, 1f);
    public static readonly Color Picked = new(1f, 0.85f, 0.2f, 0.7f);        // the selected unit's models
    public static readonly Color Ghost = new(0.9f, 0.9f, 1f, 0.35f);
    public static readonly Color Unassigned = new(0.6f, 0.6f, 0.6f, 0.35f);
    public static readonly Color WoundDot = new(0.9f, 0.15f, 0.15f);              // wounds left over a model
    public static readonly Color TokenDot = new(0.3f, 0.55f, 1f);                 // spell tokens over a model
    public static readonly Color ShakenRing = new(1f, 0.85f, 0.1f);

    public static Color ForDeclarations(TerrainDeclarations d) => d switch
    {
        _ when d.HasFlag(TerrainDeclarations.Impassable) => new(0.5f, 0.5f, 0.55f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Dangerous) => new(0.9f, 0.4f, 0.2f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.BlocksLineOfSight) => new(0.45f, 0.35f, 0.3f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Cover) => new(0.3f, 0.65f, 0.3f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.Difficult) => new(0.7f, 0.65f, 0.3f, 0.5f),
        _ => new(0.7f, 0.7f, 0.7f, 0.4f),
    };

    public static StandardMaterial3D Translucent(Color c) => new() { AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
}
