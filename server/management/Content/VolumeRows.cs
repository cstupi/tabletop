using Tabletop.Geometry;

namespace Tabletop.Management.Content;

/// <summary>asset_volume and terrain_volume share these columns. real columns are cast to float8 in SQL so they map to double.</summary>
public sealed record VolumeRow(Guid Id, string Kind, string Footprint, double DimA, double DimB, double OffsetX, double OffsetY, double Yaw, double Bottom, double Top,
    bool Impassable, bool BlocksLos, bool SeeInOut, bool Cover, bool Difficult, bool Dangerous, bool DangerousRushCharge, bool Steep, string? Notes)
{
    public const string Columns = "id as Id, kind as Kind, footprint as Footprint, dim_a::float8 as DimA, dim_b::float8 as DimB, offset_x::float8 as OffsetX, offset_y::float8 as OffsetY, yaw::float8 as Yaw, bottom::float8 as Bottom, top::float8 as Top, impassable as Impassable, blocks_los as BlocksLos, see_in_out as SeeInOut, cover as Cover, difficult as Difficult, dangerous as Dangerous, dangerous_rush_charge as DangerousRushCharge, steep as Steep, notes as Notes";
    public const string InsertColumns = "id, kind, footprint, dim_a, dim_b, offset_x, offset_y, yaw, bottom, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, dangerous_rush_charge, steep, notes";
    public const string InsertValues = "@Id, @Kind, @Footprint, @DimA, @DimB, @OffsetX, @OffsetY, @Yaw, @Bottom, @Top, @Impassable, @BlocksLos, @SeeInOut, @Cover, @Difficult, @Dangerous, @DangerousRushCharge, @Steep, @Notes";

    public Volume ToVolume()
    {
        var d = TerrainDeclarations.None;
        if (Impassable) d |= TerrainDeclarations.Impassable; if (BlocksLos) d |= TerrainDeclarations.BlocksLineOfSight; if (SeeInOut) d |= TerrainDeclarations.SeeInAndOut;
        if (Cover) d |= TerrainDeclarations.Cover; if (Difficult) d |= TerrainDeclarations.Difficult; if (Dangerous) d |= TerrainDeclarations.Dangerous;
        if (DangerousRushCharge) d |= TerrainDeclarations.DangerousOnRushCharge; if (Steep) d |= TerrainDeclarations.Steep;
        return new Volume(Kind == "solid" ? VolumeKind.Solid : VolumeKind.Area, new Footprint(Footprint == "circle" ? FootprintKind.Circle : FootprintKind.Rect, DimA, DimB),
            new Point2(OffsetX, OffsetY), new Yaw(Yaw), Bottom, Top, d, Notes);
    }

    public static VolumeRow From(Guid id, Volume v) => new(id, v.Kind == VolumeKind.Solid ? "solid" : "area", v.Footprint.Kind == FootprintKind.Circle ? "circle" : "rect",
        v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top,
        v.Declarations.HasFlag(TerrainDeclarations.Impassable), v.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight), v.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut),
        v.Declarations.HasFlag(TerrainDeclarations.Cover), v.Declarations.HasFlag(TerrainDeclarations.Difficult), v.Declarations.HasFlag(TerrainDeclarations.Dangerous),
        v.Declarations.HasFlag(TerrainDeclarations.DangerousOnRushCharge), v.Declarations.HasFlag(TerrainDeclarations.Steep), v.Notes);
}
