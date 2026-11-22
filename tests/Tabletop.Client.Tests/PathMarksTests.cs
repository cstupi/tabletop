using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>
/// The marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and lines… Vehicles should show treads
/// for their path"): alternating left and right boot prints a few per inch, facing along the route and spread across the base's swept width, or two
/// tread tracks at the base's width for a vehicle; a mark on a stretch where the base touches dangerous ground (SweptPath) in the dangerous colour.
/// </summary>
public class PathMarksTests
{
    static readonly Base Infantry = Base.Circle(1.26);
    static readonly Base Hull = Base.Rect(221 / 25.4, 119 / 25.4);
    static readonly Point2 O = new(10, 10);

    static Point2 Side(PathMark m, Point2 from, Point2 dir) { var d = m.At - from; return new(d.Dot(dir), d.Cross(dir) * -1); }

    [Fact]
    public void Each_walker_prints_a_stride_apart_alternating_left_and_right()
    {
        var marks = PathMarks.Of([O, new(13, 10)], Infantry, new Yaw(0), [], vehicle: false);
        Assert.All(marks, m => Assert.Equal(MarkKind.Foot, m.Kind));
        var walkers = marks.GroupBy(m => Math.Round(Side(m, O, new(1, 0)).Y - (m.Left ? PathMarks.Gait : -PathMarks.Gait), 6)).ToList();
        Assert.True(walkers.Count > 1);
        foreach (var w in walkers)
        {
            var along = w.OrderBy(m => m.At.X).ToList();
            Assert.True(along.Count >= 6, $"{along.Count} prints over 3\"");
            for (var i = 1; i < along.Count; i++)
            {
                Assert.Equal(PathMarks.Stride, along[i].At.X - along[i - 1].At.X, 6);
                Assert.NotEqual(along[i].Left, along[i - 1].Left);
            }
        }
    }

    [Fact]
    public void A_print_faces_along_the_leg_it_stands_on()
    {
        var marks = PathMarks.Of([O, new(13, 10), new(13, 14)], Infantry, new Yaw(0), [], vehicle: false);
        // Yaw 0 faces +Y and turns counter-clockwise: +X is -90, +Y is 0.
        Assert.Contains(marks, m => m.At.Y < 10.7 && Math.Abs(m.Facing.Degrees - -90) < 1e-6);
        Assert.Contains(marks, m => m.At.Y > 11 && Math.Abs(m.Facing.Degrees) < 1e-6);
        // Off the corner, every print faces its leg; round it, between the two.
        Assert.All(marks.Where(m => m.At.DistanceTo(new Point2(13, 10)) > 1), m => Assert.True(Math.Abs(m.Facing.Degrees - -90) < 1e-6 || Math.Abs(m.Facing.Degrees) < 1e-6));
        Assert.All(marks, m => Assert.InRange(m.Facing.Degrees, -90 - 1e-6, 1e-6));
    }

    [Fact]
    public void The_outer_track_swings_round_a_corner_without_a_gap()
    {
        // A left turn: the right track is outside it.
        var marks = PathMarks.Of([O, new(10, 16), new(4, 16)], Hull, new Yaw(0), [], vehicle: true);
        var outer = marks.Where(m => !m.Left).ToList();
        var gaps = outer.Zip(outer.Skip(1), (a, b) => a.At.DistanceTo(b.At)).ToList();
        Assert.True(gaps.Max() < 2 * PathMarks.TreadPitch, $"largest gap {gaps.Max()}");
        Assert.Contains(outer, m => Math.Abs(m.Facing.Degrees - 45) < 10);
        Assert.All(outer.Where(m => m.Facing.Degrees is > 1 and < 89), m => Assert.Equal(6, m.Station, 6));
    }

    [Fact]
    public void The_prints_spread_across_the_base_width_across_the_path()
    {
        var marks = PathMarks.Of([O, new(16, 10)], Infantry, new Yaw(0), [], vehicle: false);
        var sides = marks.Select(m => Side(m, O, new(1, 0)).Y).ToList();
        var half = Infantry.B / 2;
        Assert.All(sides, y => Assert.InRange(Math.Abs(y), 0, half));
        Assert.Equal(half - PathMarks.Inset + PathMarks.Gait, sides.Max(), 6);
        Assert.Equal(-(half - PathMarks.Inset + PathMarks.Gait), sides.Min(), 6);
    }

    [Fact]
    public void An_oval_moving_sideways_spreads_its_prints_over_its_length()
    {
        var oval = Base.Oval(2.362204724409449, 1.3779527559055118);
        Assert.Equal(oval.A, PathMarks.Width(oval, new Yaw(0), new Point2(1, 0)), 6);
        Assert.Equal(oval.B, PathMarks.Width(oval, new Yaw(0), new Point2(0, 1)), 6);
        Assert.Equal(Hull.B * Math.Cos(Math.PI / 4) + Hull.A * Math.Sin(Math.PI / 4), PathMarks.Width(Hull, new Yaw(45), new Point2(0, 1)), 6);
        var sideways = PathMarks.Of([O, new(16, 10)], oval, new Yaw(0), [], vehicle: false).Select(m => Math.Round(m.At.Y, 6)).Distinct().Count();
        var forward = PathMarks.Of([O, new(10, 16)], oval, new Yaw(0), [], vehicle: false).Select(m => Math.Round(m.At.X, 6)).Distinct().Count();
        Assert.True(sideways > forward, $"{sideways} lines of prints sideways, {forward} forward");
    }

    [Fact]
    public void A_vehicle_leaves_two_tread_tracks_at_its_width()
    {
        var marks = PathMarks.Of([O, new(10, 16)], Hull, new Yaw(0), [], vehicle: true);
        Assert.All(marks, m => Assert.Equal(MarkKind.Tread, m.Kind));
        var offset = Hull.B / 2 - PathMarks.TrackWidth / 2;
        Assert.Equal([10 - offset, 10 + offset], marks.Select(m => Math.Round(m.At.X, 6)).Distinct().Order().ToArray(), new Tol());
        Assert.All(marks, m => Assert.Equal(m.At.X < 10, m.Left));
        foreach (var track in marks.GroupBy(m => m.Left))
        {
            var along = track.OrderBy(m => m.At.Y).ToList();
            Assert.Equal(10 + PathMarks.TreadPitch / 2, along[0].At.Y, 6);
            for (var i = 1; i < along.Count; i++) Assert.Equal(PathMarks.TreadPitch, along[i].At.Y - along[i - 1].At.Y, 6);
            Assert.InRange(along[^1].At.Y, 16 - PathMarks.TreadPitch, 16);
        }
    }

    [Fact]
    public void A_vehicle_keeps_its_track_gauge_on_a_leg_across_its_facing()
    {
        var marks = PathMarks.Of([O, new(16, 16)], Hull, new Yaw(0), [], vehicle: true);
        var dir = new Point2(1, 1) * (1 / Math.Sqrt(2));
        var offsets = marks.Select(m => Math.Round(Side(m, O, dir).Y, 6)).Distinct().Order().ToArray();
        var offset = Hull.B / 2 - PathMarks.TrackWidth / 2;
        Assert.Equal([-offset, offset], offsets, new Tol());
    }

    [Fact]
    public void A_mark_on_a_stretch_touching_dangerous_ground_is_dangerous()
    {
        var marks = PathMarks.Of([O, new(16, 10)], Infantry, new Yaw(0), [new PathStretch(2, 3.5)], vehicle: false);
        Assert.Contains(marks, m => m.Dangerous);
        Assert.All(marks, m => Assert.Equal(m.Station >= 2 && m.Station <= 3.5, m.Dangerous));
        Assert.All(marks, m => Assert.Equal(m.At.X - 10, m.Station, 6));
        var treads = PathMarks.Of([O, new(16, 10)], Hull, new Yaw(0), [new PathStretch(2, 3.5)], vehicle: true);
        Assert.All(treads, m => Assert.Equal(m.Station >= 2 && m.Station <= 3.5, m.Dangerous));
    }

    [Fact]
    public void A_route_with_no_length_has_no_marks()
    {
        Assert.Empty(PathMarks.Of([O], Infantry, new Yaw(0), [], vehicle: false));
        Assert.Empty(PathMarks.Of([O, O], Hull, new Yaw(0), [], vehicle: true));
    }

    [Fact]
    public void A_dangerous_mark_takes_the_dangerous_colour_and_the_kept_path_is_fainter()
    {
        var team = new Rgba(0.2f, 0.4f, 0.9f);
        var plain = new PathMark(MarkKind.Foot, O, new Yaw(0), true, false, 0);
        var danger = plain with { Dangerous = true };
        foreach (var ink in new[] { false, true })
        {
            Assert.Equal(TerrainEffects.Of(TerrainDeclarations.Dangerous).Colour with { A = MovePaths.DangerousAlpha },
                MovePaths.MarkColour(danger, team, ink, kept: false));
            Assert.True(MovePaths.MarkColour(plain, team, ink, kept: true).A < MovePaths.MarkColour(plain, team, ink, kept: false).A);
            Assert.True(MovePaths.MarkColour(danger, team, ink, kept: true).A < MovePaths.MarkColour(danger, team, ink, kept: false).A);
        }
        // Fantasy stamps in a dark ink of the team colour, Sci-fi traces it lifted toward white.
        var stamp = MovePaths.MarkColour(plain, team, ink: true, kept: false);
        var trace = MovePaths.MarkColour(plain, team, ink: false, kept: false);
        Assert.True(stamp.R + stamp.G + stamp.B < team.R + team.G + team.B);
        Assert.True(trace.R + trace.G + trace.B > team.R + team.G + team.B);
        // A dark rim under each mark, fainter on the kept path, a little larger than the mark.
        Assert.Equal(new Rgba(0, 0, 0, MovePaths.RimAlpha), MovePaths.RimColour(kept: false));
        Assert.True(MovePaths.RimColour(kept: true).A < MovePaths.RimAlpha);
        Assert.InRange(MovePaths.RimScale, 1.1f, 2f);
    }

    [Fact]
    public void The_shapes_are_small_closed_outlines_centred_on_the_mark()
    {
        foreach (var part in PathMarks.Boot.Append(PathMarks.TreadLink))
        {
            Assert.True(part.Count >= 4);
            Assert.All(part, p => Assert.InRange(p.Length, 0, 0.25));
        }
        var all = PathMarks.Boot.SelectMany(p => p).ToList();
        Assert.True(all.Max(p => p.Y) > 0 && all.Min(p => p.Y) < 0);
        Assert.Equal(PathMarks.TrackWidth, PathMarks.TreadLink.Max(p => p.X) - PathMarks.TreadLink.Min(p => p.X), 6);
    }

    sealed class Tol : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-6;
        public int GetHashCode(double d) => 0;
    }
}
