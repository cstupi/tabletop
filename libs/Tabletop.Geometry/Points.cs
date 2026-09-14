namespace Tabletop.Geometry;

/// <summary>A point in world units (inches). Z is up.</summary>
public readonly record struct Point3(double X, double Y, double Z);
