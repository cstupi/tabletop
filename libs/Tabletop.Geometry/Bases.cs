namespace Tabletop.Geometry;

public enum BaseShape { Circle, Oval, Rect }

/// <summary>A base shape and its dimensions in inches. Circle: A = B = diameter. Oval and Rect: A = length, B = width, length along the model's facing. A square is Rect(s, s).</summary>
public readonly record struct Base(BaseShape Shape, double A, double B)
{
    public static Base Circle(double diameter) => new(BaseShape.Circle, diameter, diameter);
    public static Base Oval(double length, double width) => new(BaseShape.Oval, length, width);
    public static Base Rect(double length, double width) => new(BaseShape.Rect, length, width);
}

/// <summary>What line of sight and distance run against: the base, extruded to the height.</summary>
public readonly record struct ModelBody(Base Base, double Height);
