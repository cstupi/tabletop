using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A boot print (every model but a vehicle) or a tread link (a vehicle).</summary>
public enum MarkKind { Foot, Tread }

/// <summary>One mark along a move path: where it stands, the way it faces (along the route), whether it is a left print or on the left track, whether
/// the base touches dangerous ground there (a SweptPath stretch), its station, the walked length along the route, and its size, how many times the
/// shape's own size it draws (a print scales with the base; a tread link is 1).</summary>
public sealed record PathMark(MarkKind Kind, Point2 At, Yaw Facing, bool Left, bool Dangerous, double Station, double Size = 1);

/// <summary>
/// The marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and lines. That way it shows movement.
/// Vehicles should show treads for their path"; "it should be based on the number of models. A single model shouldn't have a sea of footprints…
/// but instead a line of a pair of footprints"). A model leaves one trail: prints alternating left and right a stride apart, Gait either side of
/// the route, stride, gait and print size scaled with the base (SizeOf), following the measured route round whatever it bends round and turning
/// in place at each bend and where the model pivots at either end (owner: "if they had to pivot or go around something that has to be reflected
/// in their pathing"); a vehicle (ModelSlot.Vehicle) leaves two tracks of tread links a
/// TreadPitch apart at the base's width. A mark on a stretch where any part of the base touches a dangerous volume (SweptBase.Stretches) is
/// dangerous. Inches throughout; the shapes are drawn about the mark, +Y along its facing.
/// </summary>
public static class PathMarks
{
    /// <summary>An infantryman's (a 32 mm base, Reference across) prints this far apart along the route, Gait either side of it.</summary>
    public const double Stride = 0.4, Gait = 0.07, Reference = 32 / 25.4;
    /// <summary>A vehicle's tread links this far apart, each TrackWidth across.</summary>
    public const double TreadPitch = 0.22, TrackWidth = 0.32;

    /// <summary>A base's print size against an infantryman's: the square root of its mean width over Reference, held to 0.8–2, so a monster's
    /// prints are larger and further apart and never a carpet.</summary>
    public static double SizeOf(Base b) => Math.Clamp(Math.Sqrt(Math.Sqrt(b.A * b.B) / Reference), 0.8, 2);
    public static double StrideOf(Base b) => Stride * SizeOf(b);
    public static double GaitOf(Base b) => Gait * SizeOf(b);

    /// <summary>A right boot print as two outlines, sole then heel, toe along +Y; a left print is it mirrored in X.</summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> Boot { get; } = [Oval(new(0.004, 0.05), 0.055, 0.1, 12), Oval(new(0, -0.1), 0.045, 0.05, 10)];

    /// <summary>One tread link: a bar across the track with a chevron's point along the facing.</summary>
    public static IReadOnlyList<Point2> TreadLink { get; } =
        [new(-TrackWidth / 2, -0.06), new(0, -0.02), new(TrackWidth / 2, -0.06), new(TrackWidth / 2, 0.02), new(0, 0.06), new(-TrackWidth / 2, 0.02)];

    static IReadOnlyList<Point2> Oval(Point2 c, double rx, double ry, int n) =>
        Enumerable.Range(0, n).Select(i => { var t = Math.Tau * i / n; return new Point2(c.X + rx * Math.Cos(t), c.Y + ry * Math.Sin(t)); }).ToList();

    /// <summary>A pivot or bend of feet: a print every TurnStep radians of it at most; a turn under PivotMin radians shows none.</summary>
    public const double TurnStep = Math.PI / 4, PivotMin = Math.PI / 18;

    /// <summary>A path's marks: along its route, turning in place from the start's facing and to the end's.</summary>
    public static IReadOnlyList<PathMark> Of(ModelPath path, bool vehicle) =>
        Of(path.Route, path.Start.Body.Base, path.Stretches, vehicle, path.Start.Placement.Yaw, path.End);

    /// <summary>A route's marks. Each leg's prints face along it; at a bend the feet turn in place at the corner and a vehicle's outer track swings
    /// round it; given the model's `start` and `end` facings, it pivots in place at either end where they differ from the route's.</summary>
    public static IReadOnlyList<PathMark> Of(IReadOnlyList<Point2> route, Base b, IReadOnlyList<PathStretch> stretches, bool vehicle,
        Yaw? start = null, Yaw? end = null)
    {
        var marks = new List<PathMark>();
        double walked = 0;
        // A vehicle's two tracks keep its gauge, the base's width, whichever way it goes; a model walks the route itself.
        IReadOnlyList<double> lanes = vehicle ? Tracks(b.B) : [0];
        var step = vehicle ? TreadPitch : StrideOf(b);
        var size = vehicle ? 1 : SizeOf(b);
        var gait = GaitOf(b);
        // Each lane's next station and how many marks it has laid, carried over corners.
        var next = lanes.Select(_ => (Station: step / 2, Count: 0)).ToArray();
        Point2? before = null;
        for (var i = 1; i < route.Count; i++)
        {
            var leg = route[i] - route[i - 1];
            var length = leg.Length;
            if (length <= 1e-9) continue;
            var dir = leg * (1 / length);
            if (before is null && start is { } s) Pivot(route[i - 1], Facing(s), dir, walked, first: true);
            if (before is { } bend && !vehicle) Pivot(route[i - 1], bend, dir, walked, first: false, last: false);
            for (var j = 0; j < lanes.Count; j++)
            {
                var (station, count) = next[j];
                // At a corner a track on the outside of the turn swings round it, so its links run on without a gap.
                if (before is { } from && vehicle)
                {
                    var turn = Math.Atan2(from.Cross(dir), from.Dot(dir));
                    var arcs = lanes[j] * turn < 0 ? (int)Math.Floor(Math.Abs(lanes[j] * turn) / step) : 0;
                    for (var k = 1; k <= arcs; k++, count++)
                        marks.Add(Mark(route[i - 1], Turned(from, turn * k / (arcs + 1)), lanes[j], count, walked));
                }
                for (; station <= walked + length + 1e-9; station += step, count++)
                    marks.Add(Mark(route[i - 1] + dir * (station - walked), dir, lanes[j], count, station));
                next[j] = (station, count);
            }
            before = dir;
            walked += length;
        }
        if (before is { } lastDir && end is { } e) Pivot(route[^1], lastDir, Facing(e), walked, first: false);
        return marks;

        // Turning in place at `at` from one direction to another: the start's pivot lays its first facing and stops short of the route's, a bend
        // lays only the facings between, the end's pivot runs on to the end's facing. Feet a print every TurnStep; a vehicle's tracks a link every
        // TreadPitch of the arc each sweeps round the centre.
        void Pivot(Point2 at, Point2 from, Point2 to, double station, bool first, bool last = true)
        {
            var turn = Math.Atan2(from.Cross(to), from.Dot(to));
            if (Math.Abs(turn) < PivotMin) return;
            for (var j = 0; j < lanes.Count; j++)
            {
                var n = vehicle ? Math.Max(1, (int)Math.Ceiling(Math.Abs(turn * lanes[j]) / TreadPitch)) : (int)Math.Ceiling(Math.Abs(turn) / TurnStep);
                var (lo, hi) = first ? (0, n - 1) : last ? (1, n) : (1, n - 1);
                for (var k = lo; k <= hi; k++)
                    marks.Add(Mark(at, Turned(from, turn * k / n), lanes[j], next[j].Count++, station));
            }
        }

        PathMark Mark(Point2 on, Point2 dir, double lane, int count, double station)
        {
            var isLeft = vehicle ? lane > 0 : count % 2 == 0;
            var side = vehicle ? lane : isLeft ? gait : -gait;
            return new PathMark(vehicle ? MarkKind.Tread : MarkKind.Foot, on + new Point2(-dir.Y, dir.X) * side,
                new Yaw(Math.Atan2(-dir.X, dir.Y) * 180 / Math.PI), isLeft, Dangerous(station, stretches), station, size);
        }
    }

    /// <summary>The direction a facing points: yaw 0 along +Y, counter-clockwise.</summary>
    static Point2 Facing(Yaw yaw)
    {
        var (s, c) = Math.SinCos(yaw.Degrees * Math.PI / 180);
        return new(-s, c);
    }

    /// <summary>A vehicle's two tracks' offsets across the path (+ to the left), their links inside its width.</summary>
    static IReadOnlyList<double> Tracks(double width)
    {
        var track = width / 2 - TrackWidth / 2;
        return track > TrackWidth / 2 ? [-track, track] : [-width / 4, width / 4];
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
