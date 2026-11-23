using System.Globalization; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>
/// The area a placement may not reach, from the KeepAway its Ask carries (an Ambusher, a 3.6 Scout or Reinforcement copy, a 3.6 Fanatic or Vanguard):
/// each listed unit's models on the table grown by its distance, less the area an IgnoredNear model lifts (Clearance.Outline), as one ring the table
/// draws; and each ghost whose base would break it (Clearance.Breaks), as the decision box's words. Every number is the server's.
/// </summary>
public static class KeepAwayArea
{
    /// <summary>The fill and rim: a dark shade, apart from every team colour, the move area's bands and the activated green.</summary>
    public static readonly Rgba Shade = new(0.05f, 0.05f, 0.07f);
    /// <summary>The outline: light over the shade, so the edge reads on any board.</summary>
    public static readonly Rgba Rim = new(0.9f, 0.91f, 0.93f);

    /// <summary>The keep-away the placement under way is held to: the deploy pick's for its unit, else the open move Ask's.</summary>
    public static KeepAway? For(MidDeploy? deploy, Ask? move) => deploy?.KeepAway ?? (move?.Legal as MoveRegion)?.KeepAway;

    /// <summary>What the ring is built from: each listed unit's models on the table and each lifting model's. Equal stamps give equal rings.</summary>
    public static IReadOnlyList<RingStamp> Stamps(Projection p, KeepAway keep) =>
        [.. keep.Units.Select(u => RingStamp.Of(p, u.UnitId, null)),
         .. (keep.IgnoredNear ?? []).Select(id => p.Models.GetValueOrDefault(id)?.UnitId).OfType<Guid>().Distinct().Select(u => RingStamp.Of(p, u, null))];

    /// <summary>The ring in two halves: the models read now, on the caller's thread; the outline built when the returned function runs, on any thread. Null
    /// when no listed model is on the table.</summary>
    public static Func<RangeRing?> Builder(Projection p, KeepAway keep)
    {
        var (from, over, lifted) = Read(p, keep, null);
        var within = keep.IgnoredWithin ?? 0;
        if (from.Count == 0) return () => null;
        var label = Label(over);
        return () => new RangeRing(over.Max(), label, Clearance.Outline(from, over, lifted, within), RingKind.KeepAway);
    }

    public static RangeRing? Of(Projection p, KeepAway keep) => Builder(p, keep)();

    /// <summary>"Too close 9\"": the placing rule's own distance, the shortest listed (a unit kept farther, Repel Ambushers' 12", shows in the
    /// ring's shape and in the breach words).</summary>
    public static string Label(IReadOnlyList<double> over) => $"Too close {Inches(over.Min())}";

    /// <summary>Each proposed model whose base breaks the keep-away, with the words the decision box shows: "placed within 9\" of {unit}". The
    /// proposed models are left out of what they are measured against.</summary>
    public static IReadOnlyDictionary<Guid, string> Breaches(Projection p, KeepAway keep, IReadOnlyDictionary<Guid, Placement> proposed, Func<Guid, string> unitName)
    {
        var found = new Dictionary<Guid, string>();
        var (from, over, lifted, units) = ReadWithUnits(p, keep, proposed.Keys.ToHashSet());
        if (from.Count == 0) return found;
        foreach (var (id, at) in proposed)
        {
            if (p.Models.GetValueOrDefault(id) is not { } m) continue;
            var hit = Clearance.Breaks(new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), at), from, over, lifted, keep.IgnoredWithin ?? 0);
            if (hit >= 0) found[id] = $"placed within {Inches(over[hit])} of {unitName(units[hit])}";
        }
        return found;
    }

    /// <summary>A model's contact words first (ContactCheck.Words), else its keep-away words; null `keep` adds none.</summary>
    public static IReadOnlyDictionary<Guid, string> With(IReadOnlyDictionary<Guid, string> contacts, Projection p, KeepAway? keep, IReadOnlyDictionary<Guid, Placement> proposed,
        Func<Guid, string> unitName)
    {
        if (keep is null) return contacts;
        var all = new Dictionary<Guid, string>(contacts);
        foreach (var (id, words) in Breaches(p, keep, proposed, unitName)) all.TryAdd(id, words);
        return all;
    }

    static (List<PlacedModel> From, List<double> Over, List<PlacedModel> Lifted) Read(Projection p, KeepAway keep, IReadOnlySet<Guid>? moving)
    {
        var (from, over, lifted, _) = ReadWithUnits(p, keep, moving);
        return (from, over, lifted);
    }

    static (List<PlacedModel> From, List<double> Over, List<PlacedModel> Lifted, List<Guid> Units) ReadWithUnits(Projection p, KeepAway keep, IReadOnlySet<Guid>? moving)
    {
        var by = keep.Units.GroupBy(u => u.UnitId).ToDictionary(g => g.Key, g => g.Max(u => u.Over));
        var from = new List<PlacedModel>(); var over = new List<double>(); var units = new List<Guid>();
        foreach (var m in p.Models.Values.OrderBy(m => m.Id))
        {
            if (m.Location is not OnTable on || !by.TryGetValue(m.UnitId, out var d) || moving?.Contains(m.Id) == true) continue;
            from.Add(new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), on.Placement)); over.Add(d); units.Add(m.UnitId);
        }
        var lifted = (keep.IgnoredNear ?? []).Select(id => p.Models.GetValueOrDefault(id)).Where(m => m is { Location: OnTable })
            .Select(m => new PlacedModel(new ModelBody(m!.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();
        return (from, over, lifted, units);
    }

    static string Inches(double d) => d.ToString("0.#", CultureInfo.InvariantCulture) + "\"";
}
