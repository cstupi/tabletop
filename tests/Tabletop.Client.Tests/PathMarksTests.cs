using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>
/// The marks a move path is drawn with (owner, 2026-11-22: "little footprints instead of traces of the base and lines… Vehicles should show treads
/// for their path"): one trail per model of alternating left and right boot prints, a stride and size scaled with the base, facing along the route, or two
/// tread tracks at the base's width for a vehicle; a mark on a stretch where the base touches dangerous ground (SweptPath) in the dangerous colour.
/// </summary>
public class PathMarksTests
{
    static readonly Base Infantry = Base.Circle(1.26);
    static readonly Base Hull = Base.Rect(221 / 25.4, 119 / 25.4);
    static readonly Point2 O = new(10, 10);

    static Point2 Side(PathMark m, Point2 from, Point2 dir) { var d = m.At - from; return new(d.Dot(dir), d.Cross(dir) * -1); }

    [Fact]
    public void A_model_leaves_one_trail_of_alternating_prints_a_stride_apart()
    {
        var marks = PathMarks.Of([O, new(16, 10)], Infantry, [], vehicle: false);
        Assert.All(marks, m => Assert.Equal(MarkKind.Foot, m.Kind));
        var stride = PathMarks.StrideOf(Infantry);
        // One pair of feet: every print a stride on from the last, left and right in turn, Gait either side of the centre line.
        Assert.InRange(marks.Count, (int)(6 / stride) - 1, (int)(6 / stride) + 1);
        var along = marks.OrderBy(m => m.Station).ToList();
        for (var i = 1; i < along.Count; i++)
        {
            Assert.Equal(stride, along[i].At.X - along[i - 1].At.X, 6);
            Assert.NotEqual(along[i].Left, along[i - 1].Left);
        }
        var gait = PathMarks.GaitOf(Infantry);
        Assert.All(marks, m => Assert.Equal(m.Left ? gait : -gait, Side(m, O, new(1, 0)).Y, 6));
    }

    [Fact]
    public void A_large_base_leaves_one_trail_of_larger_prints_further_apart()
    {
        var monster = Base.Circle(100 / 25.4);
        var big = PathMarks.Of([O, new(16, 10)], monster, [], vehicle: false);
        var small = PathMarks.Of([O, new(16, 10)], Infantry, [], vehicle: false);
        // Still one line of a pair of feet: two offsets across the path, never a carpet.
        Assert.Equal(2, big.Select(m => Math.Round(m.At.Y, 6)).Distinct().Count());
        Assert.True(big.Count < small.Count, $"{big.Count} prints for the monster, {small.Count} for the infantryman");
        Assert.True(big[0].Size > small[0].Size);
        Assert.True(PathMarks.StrideOf(monster) > PathMarks.StrideOf(Infantry));
        Assert.InRange(big[0].Size, 1.2, 2.5);
        Assert.InRange(small[0].Size, 0.9, 1.1);
        // An oval moving sideways still leaves one trail.
        var oval = Base.Oval(2.362204724409449, 1.3779527559055118);
        Assert.Equal(2, PathMarks.Of([O, new(16, 10)], oval, [], vehicle: false).Select(m => Math.Round(m.At.Y, 6)).Distinct().Count());
    }

    [Fact]
    public void A_print_faces_along_the_leg_it_stands_on()
    {
        var marks = PathMarks.Of([O, new(13, 10), new(13, 14)], Infantry, [], vehicle: false);
        // Yaw 0 faces +Y and turns counter-clockwise: +X is -90, +Y is 0.
        Assert.Contains(marks, m => m.At.Y < 10.7 && Math.Abs(m.Facing.Degrees - -90) < 1e-6);
        Assert.Contains(marks, m => m.At.Y > 11 && Math.Abs(m.Facing.Degrees) < 1e-6);
        // At the corner the feet turn in place: a print between the two legs' facings, standing at the corner.
        Assert.Contains(marks, m => Math.Abs(m.Facing.Degrees - -45) < 1e-6 && m.At.DistanceTo(new Point2(13, 10)) < 0.2);
        Assert.All(marks.Where(m => m.At.DistanceTo(new Point2(13, 10)) > 0.5),
            m => Assert.True(Math.Abs(m.Facing.Degrees - -90) < 1e-6 || Math.Abs(m.Facing.Degrees) < 1e-6));
        // The stride runs on round the corner: left and right keep alternating, in the order walked.
        for (var i = 1; i < marks.Count; i++) Assert.NotEqual(marks[i].Left, marks[i - 1].Left);
        Assert.Equal(marks.OrderBy(m => m.Station).Select(m => m.Station), marks.Select(m => m.Station));
    }

    static double ToRoute(Point2 p, IReadOnlyList<Point2> route) => route.Zip(route.Skip(1), (a, b) =>
    {
        var ab = b - a; var t = Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
        return p.DistanceTo(a + ab * t);
    }).Min();

    [Fact]
    public void A_trail_follows_the_route_bent_round_a_wall()
    {
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 6), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 10, 0), new Yaw(0), 1));
        var start = new PlacedModel(new ModelBody(Infantry, 1.5), new Placement(new Point3(10, 10, 0), new Yaw(-90)));
        var route = Movement.Measured(start, new Point2(18, 10), 12, [wall], [], null, new TableSize(72, 48), null, null, [], [], 0).Path;
        Assert.True(route.Count > 2, "the route bends round the wall");
        var marks = PathMarks.Of(route, Infantry, [], vehicle: false);
        // Every print stands beside the bent route, none on the straight line through the wall; the facings turn with it.
        Assert.All(marks, m => Assert.InRange(ToRoute(m.At, route), 0, PathMarks.GaitOf(Infantry) + 1e-6));
        Assert.Contains(marks, m => Math.Abs(m.At.Y - 10) > 2);
        Assert.DoesNotContain(marks, m => Math.Abs(m.At.X - 14) < 0.5 && Math.Abs(m.At.Y - 10) < 3);
        Assert.True(marks.Select(m => Math.Round(m.Facing.Degrees)).Distinct().Count() >= 3);
    }

    [Fact]
    public void A_pivot_at_the_start_or_end_turns_the_feet_in_place()
    {
        // Facing +Y (yaw 0), walking +X (-90), ending facing -Y (180).
        var marks = PathMarks.Of([O, new(16, 10)], Infantry, [], vehicle: false, start: new Yaw(0), end: new Yaw(180));
        var first = marks.TakeWhile(m => m.At.DistanceTo(O) < 0.2).ToList();
        Assert.True(first.Count >= 2, $"{first.Count} prints turning at the start");
        Assert.Equal(0, first[0].Facing.Degrees, 6);
        Assert.Contains(first, m => Math.Abs(m.Facing.Degrees - -45) < 1e-6);
        var last = marks.AsEnumerable().Reverse().TakeWhile(m => m.At.DistanceTo(new Point2(16, 10)) < 0.2).ToList();
        Assert.True(last.Count >= 2, $"{last.Count} prints turning at the end");
        Assert.Equal(180, Math.Abs(last[0].Facing.Degrees), 6);
        Assert.All(first.Concat(last), m => Assert.Equal(first.Contains(m) ? 0 : 6, m.Station, 6));
        for (var i = 1; i < marks.Count; i++) Assert.NotEqual(marks[i].Left, marks[i - 1].Left);
        // Already facing the way it walks, it turns nowhere.
        Assert.DoesNotContain(PathMarks.Of([O, new(16, 10)], Infantry, [], vehicle: false, start: new Yaw(-90), end: new Yaw(-90)),
            m => Math.Abs(m.Facing.Degrees - -90) > 1e-6);
        // A vehicle's tracks swing round its centre as it pivots.
        var treads = PathMarks.Of([O, new(16, 10)], Hull, [], vehicle: true, start: new Yaw(0));
        var pivot = treads.Where(m => m.Station == 0 && Math.Abs(m.Facing.Degrees - -90) > 1e-6).ToList();
        Assert.Contains(pivot, m => m.Facing.Degrees is < -10 and > -80);
        Assert.All(pivot, m => Assert.Equal(Hull.B / 2 - PathMarks.TrackWidth / 2, m.At.DistanceTo(O), 6));
    }

    [Fact]
    public void The_outer_track_swings_round_a_corner_without_a_gap()
    {
        // A left turn: the right track is outside it.
        var marks = PathMarks.Of([O, new(10, 16), new(4, 16)], Hull, [], vehicle: true);
        var outer = marks.Where(m => !m.Left).ToList();
        var gaps = outer.Zip(outer.Skip(1), (a, b) => a.At.DistanceTo(b.At)).ToList();
        Assert.True(gaps.Max() < 2 * PathMarks.TreadPitch, $"largest gap {gaps.Max()}");
        Assert.Contains(outer, m => Math.Abs(m.Facing.Degrees - 45) < 10);
        Assert.All(outer.Where(m => m.Facing.Degrees is > 1 and < 89), m => Assert.Equal(6, m.Station, 6));
    }

    [Fact]
    public void A_vehicle_leaves_two_tread_tracks_at_its_width()
    {
        var marks = PathMarks.Of([O, new(10, 16)], Hull, [], vehicle: true);
        Assert.All(marks, m => Assert.Equal(MarkKind.Tread, m.Kind));
        Assert.All(marks, m => Assert.Equal(1, m.Size));
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
        var marks = PathMarks.Of([O, new(16, 16)], Hull, [], vehicle: true);
        var dir = new Point2(1, 1) * (1 / Math.Sqrt(2));
        var offsets = marks.Select(m => Math.Round(Side(m, O, dir).Y, 6)).Distinct().Order().ToArray();
        var offset = Hull.B / 2 - PathMarks.TrackWidth / 2;
        Assert.Equal([-offset, offset], offsets, new Tol());
    }

    [Fact]
    public void A_mark_on_a_stretch_touching_dangerous_ground_is_dangerous()
    {
        var marks = PathMarks.Of([O, new(16, 10)], Infantry, [new PathStretch(2, 3.5)], vehicle: false);
        Assert.Contains(marks, m => m.Dangerous);
        Assert.All(marks, m => Assert.Equal(m.Station >= 2 && m.Station <= 3.5, m.Dangerous));
        Assert.All(marks, m => Assert.Equal(m.At.X - 10, m.Station, 6));
        Assert.Contains(marks, m => !m.Dangerous);
        var treads = PathMarks.Of([O, new(16, 10)], Hull, [new PathStretch(2, 3.5)], vehicle: true);
        Assert.All(treads, m => Assert.Equal(m.Station >= 2 && m.Station <= 3.5, m.Dangerous));
        Assert.Contains(treads, m => m.Dangerous);
    }

    [Fact]
    public void A_route_with_no_length_has_no_marks()
    {
        Assert.Empty(PathMarks.Of([O], Infantry, [], vehicle: false));
        Assert.Empty(PathMarks.Of([O, O], Hull, [], vehicle: true));
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
