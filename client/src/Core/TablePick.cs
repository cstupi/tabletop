using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>Model: a model. Marker: an objective or setup marker that may be dragged now, its disc under the pointer. Other: a terrain
/// piece, or a marker that may not be dragged.</summary>
public enum HitKind { Model, Marker, Other }

/// <summary>A piece against the pointer's ray: Along, how far along the ray it is entered; Miss, how far the ray passes beside it in screen
/// pixels (0: the ray enters it); Proposed, a ghost the open Ask moves (Proposing).</summary>
public readonly record struct TableHit(HitKind Kind, double Along, double Miss, bool Proposed = false);

/// <summary>Which piece the pointer picks (TableInput.Pick, the viewer's pickAt): a model the ray enters, the nearest; else a model the ray
/// passes within the slop of, the closest; else a marker that may be dragged now whose disc the ray meets, so an unlocked marker under a
/// terrain piece is grabbed and not the terrain; else the nearest terrain piece or locked marker the ray enters. Terrain is entered by its
/// volumes' prisms (EnterPrism), not their bounding box, so a tall or turned piece beside the pointer never takes the pick from the piece under it.</summary>
public static class TablePick
{
    /// <summary>The slop in screen pixels: a click this close to a model picks it over the terrain under the pointer.</summary>
    public const double SlopPixels = 12;

    public static int? Best(IReadOnlyList<TableHit> hits, double slop = SlopPixels)
    {
        int? best = null;
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            if (Rank(h, slop) is not { } rank) continue;
            if (best is not { } b || rank < Rank(hits[b], slop) || rank == Rank(hits[b], slop) && Key(h) < Key(hits[b])) best = i;
        }
        return best;
    }

    /// <summary>0 a ghost the open Ask moves entered (a passenger over its transport is grabbed, never the transport), 1 a model entered, 2 a
    /// model within the slop, 3 a draggable marker entered, 4 another piece entered; null for nothing picked.</summary>
    static int? Rank(TableHit h, double slop) => h.Kind switch
    {
        HitKind.Model => h.Miss <= 0 ? h.Proposed ? 0 : 1 : h.Miss <= slop ? 2 : null,
        HitKind.Marker => h.Miss <= 0 ? 3 : null,
        _ => h.Miss <= 0 ? 4 : null,
    };

    static double Key(TableHit h) => h.Kind == HitKind.Model && h.Miss > 0 ? h.Miss : h.Along;

    /// <summary>How far along the ray (from, in the direction dir, table frame) it meets the horizontal plane at centre.Z within radius of
    /// centre; null when it meets the plane outside the disc, or never.</summary>
    public static double? OnDisc(Point3 from, Point3 dir, Point3 centre, double radius)
    {
        if (Math.Abs(dir.Z) < 1e-12) return null;
        var t = (centre.Z - from.Z) / dir.Z;
        if (t < 0) return null;
        var dx = from.X + dir.X * t - centre.X; var dy = from.Y + dir.Y * t - centre.Y;
        return dx * dx + dy * dy <= radius * radius ? t * Length(dir) : null;
    }

    /// <summary>How far along the ray it enters a volume's prism (its footprint at the piece's yaw and scale, from its bottom to its top); 0
    /// when it starts inside, null when it misses.</summary>
    public static double? EnterPrism(Point3 from, Point3 dir, WorldVolume v)
    {
        double near = 0, far = double.MaxValue;
        if (!Slab(from.Z, dir.Z, v.WorldBottom(), v.WorldTop(), ref near, ref far)) return null;
        var c = v.WorldCentre(); var (s, k) = Math.SinCos(-v.WorldYaw().Degrees * Math.PI / 180);
        // Into the volume's own frame: centred, turned back by its yaw.
        double px = from.X - c.X, py = from.Y - c.Y;
        double lx = px * k - py * s, ly = px * s + py * k, ldx = dir.X * k - dir.Y * s, ldy = dir.X * s + dir.Y * k;
        var f = v.Volume.Footprint; var scale = v.Transform.Scale;
        if (f.Kind == FootprintKind.Circle)
        {
            var r = f.A * scale; var a = ldx * ldx + ldy * ldy; var b = lx * ldx + ly * ldy; var q = lx * lx + ly * ly - r * r;
            if (a < 1e-24) { if (q > 0) return null; }
            else
            {
                var disc = b * b - a * q;
                if (disc < 0) return null;
                var root = Math.Sqrt(disc);
                near = Math.Max(near, (-b - root) / a); far = Math.Min(far, (-b + root) / a);
            }
        }
        else
        {
            var (hw, hl) = (f.A * scale / 2, f.B * scale / 2);
            if (!Slab(lx, ldx, -hw, hw, ref near, ref far) || !Slab(ly, ldy, -hl, hl, ref near, ref far)) return null;
        }
        return near <= far ? near * Length(dir) : null;
    }

    static bool Slab(double origin, double d, double lo, double hi, ref double near, ref double far)
    {
        if (Math.Abs(d) < 1e-12) return origin >= lo && origin <= hi;
        var t1 = (lo - origin) / d; var t2 = (hi - origin) / d;
        if (t1 > t2) (t1, t2) = (t2, t1);
        near = Math.Max(near, t1); far = Math.Min(far, t2);
        return near <= far;
    }

    static double Length(Point3 d) => Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
}
