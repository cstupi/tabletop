using Tabletop.Geometry;

namespace Tabletop.Content;

public static partial class RandomTables
{
    /// <summary>Draws a point on a team's side tries this often before the candidate is given up.</summary>
    const int SideDraws = 50;

    sealed record Spot(Piece Piece, Point2 At, double Yaw, int? Team);

    /// <summary>Largest unit first, down the gap ladder; a unit with no candidate on the last rung is withdrawn whole.</summary>
    static (List<LaidPiece> Laid, List<string> Unplaced) Place(IReadOnlyList<Unit> units, TableSize size, IReadOnlyDictionary<int, Point2> anchors, IReadOnlyList<int> teams, Random rng)
    {
        var laid = new List<LaidPiece>();
        var standing = new List<WorldVolume>();
        var unplaced = new List<string>();
        foreach (var unit in units)
        {
            var spots = Find(unit, size, anchors, teams, standing, rng);
            if (spots is null) { unplaced.AddRange(unit.Members.Select(m => m.Piece.Name)); continue; }
            foreach (var s in spots)
            {
                var volume = new TerrainVolume(NewId(rng), s.Piece.Volume);
                var placement = new TerrainPlacement(NewId(rng), VisualRef.OfBuiltin(s.Piece.Name), new Point3(s.At.X, s.At.Y, 0), new Yaw(s.Yaw), 1, [volume]);
                laid.Add(new LaidPiece(placement, s.Team));
                standing.AddRange(placement.WorldVolumes());
            }
        }
        return (laid, unplaced);
    }

    static List<Spot>? Find(Unit unit, TableSize size, IReadOnlyDictionary<int, Point2> anchors, IReadOnlyList<int> teams, List<WorldVolume> standing, Random rng)
    {
        foreach (var gap in Gaps)
            for (var c = 0; c < Candidates; c++)
                if (Candidate(unit, size, anchors, teams, standing, gap, rng) is { } spots) return spots;
        return null;
    }

    static List<Spot>? Candidate(Unit unit, TableSize size, IReadOnlyDictionary<int, Point2> anchors, IReadOnlyList<int> teams, List<WorldVolume> standing, double gap, Random rng)
    {
        var spots = new List<Spot>();
        var stand = new List<WorldVolume>();
        bool Fits(Piece piece, Point2 at, double yaw)
        {
            var volumes = new WorldVolume(Guid.Empty, piece.Volume, new TerrainTransform(new Point3(at.X, at.Y, 0), new Yaw(yaw), 1));
            if (!Table.ContainsVolume(size, volumes)) return false;
            if (standing.Any(s => TooClose(volumes, s, gap)) || stand.Any(s => TooClose(volumes, s, gap))) return false;
            stand.Add(volumes);
            return true;
        }

        switch (unit.Kind)
        {
            case Kind.Pair:
            {
                var ((a, ta), (b, tb)) = (unit.Members[0], unit.Members[1]);
                if (OnSide(size, anchors, teams[ta!.Value], rng) is not { } at) return null;
                var yaw = Step(rng);
                var mirror = new Point2(size.Width - at.X, size.Length - at.Y);
                if (TableSides.SideOf(anchors, mirror) != teams[tb!.Value]) return null;
                if (!Fits(a, at, yaw) || !Fits(b, mirror, (yaw + 180) % 360)) return null;
                spots.Add(new Spot(a, at, yaw, teams[ta.Value]));
                spots.Add(new Spot(b, mirror, (yaw + 180) % 360, teams[tb.Value]));
                return spots;
            }
            case Kind.Round:
                foreach (var (piece, team) in unit.Members)
                {
                    if (OnSide(size, anchors, teams[team!.Value], rng) is not { } at) return null;
                    var yaw = Step(rng);
                    if (!Fits(piece, at, yaw)) return null;
                    spots.Add(new Spot(piece, at, yaw, teams[team.Value]));
                }
                return spots;
            default:
            {
                var piece = unit.Members[0].Piece;
                var at = teams.Count == 2 ? OnBisector(size, anchors, teams, rng) : NearCentre(size, rng);
                var yaw = Step(rng);
                if (!Fits(piece, at, yaw)) return null;
                return [new Spot(piece, at, yaw, null)];
            }
        }
    }

    /// <summary>Closer than the gap; footprints whose bounding circles are the gap apart are not measured.</summary>
    static bool TooClose(WorldVolume a, WorldVolume b, double gap) =>
        a.WorldCentre().DistanceTo(b.WorldCentre()) - Reach(a) - Reach(b) < gap && Distance.Between(a, b) < gap;

    static double Reach(WorldVolume v)
    {
        var f = v.Volume.Footprint;
        return (f.Kind == FootprintKind.Circle ? f.A : Math.Sqrt(f.A * f.A + f.B * f.B) / 2) * v.Transform.Scale;
    }

    static double Step(Random rng) => rng.Next(0, 24) * 15;

    /// <summary>A uniform point on a team's side, by rejection over the table; null when <see cref="SideDraws"/> draws all miss.</summary>
    static Point2? OnSide(TableSize size, IReadOnlyDictionary<int, Point2> anchors, int team, Random rng)
    {
        for (var i = 0; i < SideDraws; i++)
        {
            var p = new Point2(rng.NextDouble() * size.Width, rng.NextDouble() * size.Length);
            if (TableSides.SideOf(anchors, p) == team) return p;
        }
        return null;
    }

    /// <summary>A uniform point on the chord of the line through the two anchors' midpoint, perpendicular to them, that lies inside the table.</summary>
    static Point2 OnBisector(TableSize size, IReadOnlyDictionary<int, Point2> anchors, IReadOnlyList<int> teams, Random rng)
    {
        var (a, b) = (anchors[teams[0]], anchors[teams[1]]);
        var mid = (a + b) * 0.5;
        var d = new Point2(-(b - a).Y, (b - a).X);
        d *= 1 / d.Length;
        double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        void Clip(double origin, double dir, double max)
        {
            if (Math.Abs(dir) < 1e-12) return;
            var (t0, t1) = ((0 - origin) / dir, (max - origin) / dir);
            lo = Math.Max(lo, Math.Min(t0, t1));
            hi = Math.Min(hi, Math.Max(t0, t1));
        }
        Clip(mid.X, d.X, size.Width);
        Clip(mid.Y, d.Y, size.Length);
        return mid + d * (lo + rng.NextDouble() * (hi - lo));
    }

    /// <summary>A uniform point within a quarter of the shorter edge of the table's centre.</summary>
    static Point2 NearCentre(TableSize size, Random rng)
    {
        var r = Math.Min(size.Width, size.Length) / 4;
        var centre = new Point2(size.Width / 2, size.Length / 2);
        while (true)
        {
            var p = new Point2((rng.NextDouble() * 2 - 1) * r, (rng.NextDouble() * 2 - 1) * r);
            if (p.Length <= r) return centre + p;
        }
    }

    /// <summary>A version 4 id drawn from the seeded generator, so a seed pins every id.</summary>
    static Guid NewId(Random rng)
    {
        Span<byte> b = stackalloc byte[16];
        rng.NextBytes(b);
        b[6] = (byte)((b[6] & 0x0F) | 0x40);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }
}
