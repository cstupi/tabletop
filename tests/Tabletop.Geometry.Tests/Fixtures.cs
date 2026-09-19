namespace Tabletop.Geometry.Tests;

internal static class Fx
{
    public const TerrainDeclarations Blocks = TerrainDeclarations.BlocksLineOfSight;
    public const TerrainDeclarations ForestSight = TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut;

    public static PlacedModel Model(double x, double y, double z = 0, double diameter = 1.26, double height = 1.5, double yaw = 0) =>
        new(new ModelBody(Base.Circle(diameter), height), new Placement(new Point3(x, y, z), new Yaw(yaw)));

    public static PlacedModel Model(Base b, double x, double y, double z = 0, double height = 1.5, double yaw = 0) =>
        new(new ModelBody(b, height), new Placement(new Point3(x, y, z), new Yaw(yaw)));

    public static WorldVolume Box(double x, double y, double width, double length, double bottom, double top,
        VolumeKind kind = VolumeKind.Solid, TerrainDeclarations d = TerrainDeclarations.None, double yaw = 0, double scale = 1, double z = 0) =>
        new(Guid.CreateVersion7(), new Volume(kind, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), bottom, top, d, null),
            new TerrainTransform(new Point3(x, y, z), new Yaw(yaw), scale));

    public static WorldVolume Cylinder(double x, double y, double radius, double bottom, double top,
        VolumeKind kind = VolumeKind.Solid, TerrainDeclarations d = TerrainDeclarations.None, double scale = 1, double z = 0) =>
        new(Guid.CreateVersion7(), new Volume(kind, Footprint.Circle(radius), new Point2(0, 0), new Yaw(0), bottom, top, d, null),
            new TerrainTransform(new Point3(x, y, z), new Yaw(0), scale));
}
