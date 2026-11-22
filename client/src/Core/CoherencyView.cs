using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One ghost's coherency reach: the ring Chain out from its base edge, at its base's shape and facing (RangeOutline.Of), at its height.</summary>
public sealed record CoherencyRing(Guid ModelId, IReadOnlyList<(Point2 A, Point2 B)> Segments, double Z);

/// <summary>A model out of coherency and the model it is measured to (Coherency.Gaps: the nearest of the chain, or the farthest past the spread),
/// between the two bases' closest points, with its words over the line (`1.4" apart, needs 1"`).</summary>
public sealed record CoherencyLine(Guid ModelId, Guid ToId, CoherencyBreak Break, Point3 From, Point3 To, string Text);

/// <summary>
/// What the table draws while a unit's models are placed (a move's or a deployment's ghosts): each ghost's ring, a line per model out of coherency,
/// and each such model's words for the decision box (`Flayer Cavalry 3 out of coherency: move within 1" of another model`, MarkWords' `Out of
/// coherency`). Blocks: the server refuses this placement (GfMoves.CoherencyBreach: a move of a unit in coherency before it, or with none on the table),
/// so the words are marks that hold the commit under strict; otherwise they are warnings.
/// </summary>
public sealed record CoherencyPicture(IReadOnlyList<CoherencyRing> Rings, IReadOnlyList<CoherencyLine> Lines, IReadOnlyDictionary<Guid, string> Words, bool Blocks)
{
    public static readonly CoherencyPicture None = new([], [], new Dictionary<Guid, string>(), false);
}

/// <summary>
/// The unit coherency of the units being placed, measured as the server measures it (Coherency.Check and Gaps over the rule set's Welcome.Coherency):
/// every model of each unit on the table, the placed ones where their ghosts stand at their facings, the rest where they stand.
/// </summary>
public static class CoherencyView
{
    /// <summary>`judged`: the server judges this placement's coherency (a move answer); a deployment's ghosts only warn.</summary>
    public static CoherencyPicture Of(Projection p, IReadOnlyDictionary<Guid, Placement> placed, UnitCoherency? coherency, Func<Guid, string> name, bool judged)
    {
        if (coherency is not { } c || placed.Count == 0) return CoherencyPicture.None;
        var rings = new List<CoherencyRing>(); var lines = new List<CoherencyLine>(); var words = new Dictionary<Guid, string>();
        var blocks = false;
        foreach (var unit in placed.Keys.Where(p.Models.ContainsKey).Select(id => p.Models[id].UnitId).Distinct())
        {
            var members = p.Models.Values.Where(m => m.UnitId == unit).ToList();
            var now = members.Select(m => (m.Id, At: placed.TryGetValue(m.Id, out var at) ? at : m.Location is OnTable t ? t.Placement : (Placement?)null))
                .Where(x => x.At is not null).Select(x => (x.Id, Body: Body(p, x.Id, x.At!.Value))).ToList();
            foreach (var (id, body) in now.Where(x => placed.ContainsKey(x.Id)))
                rings.Add(new CoherencyRing(id, RangeOutline.Of([body], c.Chain), body.Placement.Position.Z));
            var bodies = now.Select(x => x.Body).ToList();
            var gaps = Coherency.Gaps(bodies, c.Chain, c.Elevated, c.Spread);
            if (gaps.Count == 0) continue;
            var before = members.Where(m => m.Location is OnTable).Select(m => Body(p, m.Id, ((OnTable)m.Location).Placement)).ToList();
            blocks |= judged && Coherency.Check(before, c.Chain, c.Elevated, c.Spread).Coherent;
            foreach (var g in gaps)
            {
                var (id, to) = (now[g.Model].Id, now[g.To].Id);
                double zFrom = bodies[g.Model].Placement.Position.Z, zTo = bodies[g.To].Placement.Position.Z;
                lines.Add(new CoherencyLine(id, to, g.Break, new Point3(g.OnModel.X, g.OnModel.Y, zFrom), new Point3(g.OnTo.X, g.OnTo.Y, zTo), Text(g)));
                words[id] = g.Break == CoherencyBreak.Chain
                    ? $"{name(id)} out of coherency: move within {Inches(g.Needed)}\" of another model"
                    : $"{name(id)} out of coherency: move within {Inches(g.Needed)}\" of {name(to)}";
            }
        }
        return new CoherencyPicture(rings, lines, words, blocks);
    }

    /// <summary>`1.4" apart, needs 1"`.</summary>
    public static string Text(CoherencyGap g) => $"{g.Distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\" apart, needs {Inches(g.Needed)}\"";

    /// <summary>A model's marks: its contact words first, else its coherency words when the picture blocks.</summary>
    public static IReadOnlyDictionary<Guid, string> With(IReadOnlyDictionary<Guid, string> marks, CoherencyPicture picture)
    {
        if (!picture.Blocks || picture.Words.Count == 0) return marks;
        var all = new Dictionary<Guid, string>(marks);
        foreach (var (id, w) in picture.Words) all.TryAdd(id, w);
        return all;
    }

    /// <summary>A model's warnings: its own first (a dangerous terrain test), else its coherency words when the picture does not block.</summary>
    public static IReadOnlyDictionary<Guid, string> Warnings(IReadOnlyDictionary<Guid, string> warnings, CoherencyPicture picture)
    {
        if (picture.Blocks || picture.Words.Count == 0) return warnings;
        var all = new Dictionary<Guid, string>(warnings);
        foreach (var (id, w) in picture.Words) all.TryAdd(id, w);
        return all;
    }

    static PlacedModel Body(Projection p, Guid id, Placement at) => new(new ModelBody(p.Models[id].Slot.Base, p.Models[id].Slot.Height), at);

    static string Inches(double d) => d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
