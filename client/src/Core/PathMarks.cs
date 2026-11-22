using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A boot print (every model but a vehicle) or a tread link (a vehicle).</summary>
public enum MarkKind { Foot, Tread }

/// <summary>One mark along a move path: where it stands, the way it faces (along the route), whether it is a left print or on the left track, whether
/// the base touches dangerous ground there (a SweptPath stretch), and its station, the walked length along the route.</summary>
public sealed record PathMark(MarkKind Kind, Point2 At, Yaw Facing, bool Left, bool Dangerous, double Station);

/// <summary>
/// The marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and lines. That way it shows movement.
/// Vehicles should show treads for their path"). A model's prints come from walkers spread across the base's swept width (the width across the leg's
/// direction at the start's facing, as SweptPath sweeps it), each walker alternating left and right a Stride apart; a vehicle (ModelSlot.Vehicle)
/// leaves two tracks of tread links a TreadPitch apart at the base's width. A mark on a stretch where any part of the base touches a dangerous volume
/// (SweptBase.Stretches) is dangerous. Inches throughout; the shapes are drawn about the mark, +Y along its facing.
/// </summary>
public static class PathMarks
{
    /// <summary>One walker's prints this far apart along the route; walkers' lanes at most Lane apart; a walker's left and right prints Gait either
    /// side of its lane; the outer lanes Inset inside the swept width.</summary>
    public const double Stride = 0.4, Lane = 0.45, Gait = 0.07, Inset = 0.14;
    /// <summary>A vehicle's tread links this far apart, each TrackWidth across.</summary>
    public const double TreadPitch = 0.22, TrackWidth = 0.32;

    /// <summary>A right boot print as two outlines, sole then heel, toe along +Y; a left print is it mirrored in X.</summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> Boot { get; } = [Oval(new(0.004, 0.05), 0.055, 0.1, 12), Oval(new(0, -0.1), 0.045, 0.05, 10)];

    /// <summary>One tread link: a bar across the track with a chevron's point along the facing.</summary>
    public static IReadOnlyList<Point2> TreadLink { get; } =
        [new(-TrackWidth / 2, -0.06), new(0, -0.02), new(TrackWidth / 2, -0.06), new(TrackWidth / 2, 0.02), new(0, 0.06), new(-TrackWidth / 2, 0.02)];

    static IReadOnlyList<Point2> Oval(Point2 c, double rx, double ry, int n) =>
        Enumerable.Range(0, n).Select(i => { var t = Math.Tau * i / n; return new Point2(c.X + rx * Math.Cos(t), c.Y + ry * Math.Sin(t)); }).ToList();

    /// <summary>The base's width across a direction of travel (a unit vector), the base at `yaw`: what its swept band covers.</summary>
    public static double Width(Base b, Yaw yaw, Point2 dir)
    {
        var n = new Point2(-dir.Y, dir.X);
        var x = new Point2(1, 0).Rotate(yaw); var y = new Point2(0, 1).Rotate(yaw);
        double u = Math.Abs(n.Dot(x)) * b.B / 2, v = Math.Abs(n.Dot(y)) * b.A / 2;
        return 2 * (b.Shape == BaseShape.Rect ? u + v : Math.Sqrt(u * u + v * v));
    }

    /// <summary>A path's marks (the model's base at its start's facing).</summary>
    public static IReadOnlyList<PathMark> Of(ModelPath path, bool vehicle) =>
        Of(path.Route, path.Start.Body.Base, path.Start.Placement.Yaw, path.Stretches, vehicle);

    public static IReadOnlyList<PathMark> Of(IReadOnlyList<Point2> route, Base b, Yaw yaw, IReadOnlyList<PathStretch> stretches, bool vehicle)
    {
        var marks = new List<PathMark>();
        double walked = 0;
        // Each lane's offset across the path (+ to the left) and its first station on the current leg, carried over corners.
        var next = new Dictionary<int, (double Station, int Count)>();
        var step = vehicle ? TreadPitch : Stride;
        Point2? before = null;
        for (var i = 1; i < route.Count; i++)
        {
            var leg = route[i] - route[i - 1];
            var length = leg.Length;
            if (length <= 1e-9) continue;
            var dir = leg * (1 / length);
            // Prints cover the swept width across this leg; a vehicle's tracks keep its gauge, the base's width, whichever way it goes.
            var lanes = Lanes(vehicle ? b.B : Width(b, yaw, dir), vehicle);
            for (var j = 0; j < lanes.Count; j++)
            {
                var (station, count) = next.TryGetValue(j, out var n) ? n
                    : (vehicle ? TreadPitch / 2 : Stride / 2 + (j % 2) * Stride / 2, 0);
                // At a corner a lane on the outside of the turn swings round it, so its marks run on without a gap.
                if (before is { } from)
                {
                    var turn = Math.Atan2(from.Cross(dir), from.Dot(dir));
                    var arcs = lanes[j] * turn < 0 ? (int)Math.Floor(Math.Abs(lanes[j] * turn) / step) : 0;
                    for (var k = 1; k <= arcs; k++, count++)
                        marks.Add(Mark(route[i - 1], Turned(from, turn * k / (arcs + 1)), lanes[j], j, count, walked));
                }
                for (; station <= walked + length + 1e-9; station += step, count++)
                    marks.Add(Mark(route[i - 1] + dir * (station - walked), dir, lanes[j], j, count, station));
                next[j] = (station, count);
            }
            before = dir;
            walked += length;
        }
        return marks;

        PathMark Mark(Point2 on, Point2 dir, double lane, int j, int count, double station)
        {
            var isLeft = vehicle ? lane > 0 : (count + j) % 2 == 0;
            var side = vehicle ? lane : lane + (isLeft ? Gait : -Gait);
            return new PathMark(vehicle ? MarkKind.Tread : MarkKind.Foot, on + new Point2(-dir.Y, dir.X) * side,
                new Yaw(Math.Atan2(-dir.X, dir.Y) * 180 / Math.PI), isLeft, Dangerous(station, stretches), station);
        }
    }

    /// <summary>The lanes' offsets across the path (+ to the left): a vehicle's two tracks, their links inside its width; a walker every Lane at most
    /// between the outer lanes Inset inside the width, one in the middle of a base too narrow for two.</summary>
    static IReadOnlyList<double> Lanes(double width, bool vehicle)
    {
        if (vehicle)
        {
            var track = width / 2 - TrackWidth / 2;
            return track > TrackWidth / 2 ? [-track, track] : [-width / 4, width / 4];
        }
        var usable = width - 2 * Inset;
        if (usable <= 1e-9) return [0];
        var count = 1 + (int)Math.Ceiling(usable / Lane - 1e-9);
        return Enumerable.Range(0, count).Select(j => -usable / 2 + j * usable / (count - 1)).ToList();
    }

    /// <summary>A direction turned counter-clockwise by `radians`.</summary>
    static Point2 Turned(Point2 d, double radians)
    {
        var (s, c) = Math.SinCos(radians);
        return new(d.X * c - d.Y * s, d.X * s + d.Y * c);
    }

    static bool Dangerous(double station, IReadOnlyList<PathStretch> stretches) =>
        stretches.Any(s => station >= s.From - 1e-9 && station <= s.To + 1e-9);
}
