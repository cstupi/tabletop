namespace Tabletop.Client.Core;

public enum HitKind { Model, Other }

/// <summary>A piece against the pointer's ray: Along, how far along the ray it is entered; Miss, how far the ray passes beside it in screen
/// pixels (0: the ray enters it).</summary>
public readonly record struct TableHit(HitKind Kind, double Along, double Miss);

/// <summary>Which piece the pointer picks (TableInput.Pick, the viewer's pickAt): a model the ray enters, the nearest; else a model the ray
/// passes within the slop of, the closest; else the nearest terrain piece or marker the ray enters. A terrain box encloses its whole area
/// volume, so a model standing in or beside it is never lost to it.</summary>
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

    /// <summary>0 a model entered, 1 a model within the slop, 2 another piece entered; null for nothing picked.</summary>
    static int? Rank(TableHit h, double slop) => h.Kind == HitKind.Model
        ? h.Miss <= 0 ? 0 : h.Miss <= slop ? 1 : null
        : h.Miss <= 0 ? 2 : null;

    static double Key(TableHit h) => h.Kind == HitKind.Model && h.Miss > 0 ? h.Miss : h.Along;
}
