using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

public static class Palette
{
    static readonly Color[] Teams = [new(0.85f, 0.25f, 0.2f), new(0.2f, 0.45f, 0.9f), new(0.25f, 0.7f, 0.3f), new(0.9f, 0.75f, 0.2f), new(0.6f, 0.3f, 0.8f), new(0.2f, 0.75f, 0.75f)];
    public static Color Team(int ordinal) => Teams[(Math.Max(1, ordinal) - 1) % Teams.Length];
    public static readonly Color Proxy = new(0.3f, 0.9f, 0.4f, 0.45f);       // semi-transparent green (asset library)
    public static readonly Color Selected = new(1f, 1f, 1f);
    public static readonly Color Ghost = new(0.9f, 0.9f, 1f, 0.35f);
    public static readonly Color Unassigned = new(0.6f, 0.6f, 0.6f, 0.35f);

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
