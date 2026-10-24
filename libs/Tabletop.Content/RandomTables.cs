using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A placed piece and the team ordinal it was dealt to; null for a neutral piece.</summary>
public sealed record LaidPiece(TerrainPlacement Placement, int? Team);

/// <summary>The table laid: the pieces, the rolled names withdrawn for want of room, and the names the catalog lacks.</summary>
public sealed record LaidTable(int Seed, IReadOnlyList<LaidPiece> Pieces, IReadOnlyList<string> Unplaced, IReadOnlyList<string> Unlisted);

/// <summary>A random table (plan → The placer): rolled per planet, dealt evenly to the teams by signature, placed clear of each other, mirrored for two teams.</summary>
public static partial class RandomTables
{
    /// <summary>The clearances tried between pieces, in inches, widest first.</summary>
    public static readonly double[] Gaps = [6, 3, 0.5];
    /// <summary>Candidate positions tried per rung of <see cref="Gaps"/>.</summary>
    public const int Candidates = 400;

    const int Sight = 1, Cover = 2, Difficult = 4;

    sealed record Piece(string Name, Volume Volume, double Area, int Signature);

    enum Kind { Pair, Round, Neutral }

    /// <summary>Pieces placed together: a mirrored pair, a round of one per team, or one neutral piece. Team is an index into the ordered team ordinals.</summary>
    sealed record Unit(Kind Kind, IReadOnlyList<(Piece Piece, int? Team)> Members, double Area);

    /// <summary>Rolls the planet's pieces with <paramref name="seed"/> and lays them on the table: <paramref name="zones"/> are the draft's rows (empty is Frontline's two).</summary>
    public static LaidTable Lay(Planet planet, TableSize size, IReadOnlyList<DeploymentZone> zones, int seed, IReadOnlyDictionary<string, BuiltinTerrain> terrain)
    {
        var rng = new Random(seed);
        var names = PlanetTerrain.Roll(planet, rng);
        var unlisted = names.Where(n => !terrain.ContainsKey(n)).ToList();
        var pieces = names.Where(terrain.ContainsKey).Select(n => MakePiece(terrain[n])).ToList();
        var anchors = TableSides.Anchors(size, zones);
        var teams = anchors.Keys.Order().ToList();
        var units = Allocate(pieces, teams.Count).OrderByDescending(u => u.Area).ToList();
        var (laid, unplaced) = Place(units, size, anchors, teams, rng);
        return new LaidTable(seed, laid, unplaced, unlisted);
    }

    static Piece MakePiece(BuiltinTerrain t)
    {
        var d = t.Volume.Declarations;
        var f = t.Volume.Footprint;
        var area = f.Kind == FootprintKind.Circle ? Math.PI * f.A * f.A : f.A * f.B;
        var signature = (d.HasFlag(TerrainDeclarations.BlocksLineOfSight) ? Sight : 0) | (d.HasFlag(TerrainDeclarations.Cover) ? Cover : 0)
            | (d.HasFlag(TerrainDeclarations.Difficult) ? Difficult : 0);
        return new Piece(t.Name, t.Volume, area, signature);
    }

    /// <summary>Groups by signature (bit field order), largest first inside a group; each team gets c / n of a group of c, the remainder is neutral (the smallest).
    /// The team dealt the first piece rotates from group to group and from round to round.</summary>
    static IEnumerable<Unit> Allocate(IReadOnlyList<Piece> pieces, int n)
    {
        var rotation = 0;
        foreach (var group in pieces.GroupBy(p => p.Signature).OrderBy(g => g.Key))
        {
            var sorted = group.OrderByDescending(p => p.Area).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
            var dealt = sorted.Count - sorted.Count % n;
            for (var r = 0; r < dealt / n; r++)
            {
                var round = Enumerable.Range(0, n).Select(j => (sorted[r * n + j], (int?)((j + rotation + r) % n))).ToList();
                if (n == 2) yield return new Unit(Kind.Pair, round, round.Max(m => m.Item1.Area));
                else yield return new Unit(Kind.Round, round, round.Max(m => m.Item1.Area));
            }
            for (var i = dealt; i < sorted.Count; i++) yield return new Unit(Kind.Neutral, [(sorted[i], null)], sorted[i].Area);
            if (dealt > 0) rotation++;
        }
    }
}
