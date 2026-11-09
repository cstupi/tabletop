using System.Globalization; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Threat range plan: a unit's reach in its next activation, from the server's <see cref="UnitThreat"/>, drawn as <see cref="RangeRings"/> are.
/// One "Charge" outline at the charge move round every model on the table, and one "Shoot" outline per distinct ShootMove + range round the models
/// with that reach, ascending. The client adds the two numbers; every distance is the server's.</summary>
public static class ThreatRings
{
    public static IReadOnlyList<RangeRing> Of(Projection p, Guid unitId, UnitThreat threat)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable)
            .Select(m => (m.Id, Placed: new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
        var rings = new List<RangeRing>();
        if (threat.Charge is { } charge && models.Count > 0) rings.Add(new(charge, $"Charge {Inches(charge)}", RangeOutline.Of(models.Select(m => m.Placed).ToList(), charge), RingKind.Charge));
        if (threat.ShootMove is { } move)
            foreach (var reach in models.Where(m => threat.Ranges.ContainsKey(m.Id)).GroupBy(m => move + threat.Ranges[m.Id]))
                rings.Add(new(reach.Key, $"Shoot {Inches(reach.Key)}", RangeOutline.Of(reach.Select(m => m.Placed).ToList(), reach.Key), RingKind.Shoot));
        return rings.OrderBy(r => r.Inches).ToList();
    }

    /// <summary>The owner's enemy: a unit not on the team I act for now (Seats.Acting), so one account holding both teams sees the other side's.
    /// A watcher acts for none and sees every unit's.</summary>
    public static bool OfAnotherTeam(GameInfo game, Projection p, Guid? acting, Guid unitId)
    {
        if (p.Units.GetValueOrDefault(unitId) is not { } u) return false;
        return acting is not { } mine || TeamOf(game, u) != mine;
    }

    /// <summary>The enemy unit whose threat stays drawn: the selected unit when it is of another team; selecting a unit of my own (picking a unit
    /// to deploy or move, or its ghost) or a terrain piece keeps the one before, so the reach stays up while I place; an empty selection clears it.</summary>
    public static Guid? Pinned(GameInfo game, Projection p, Guid? acting, Guid? selected, Guid? before) =>
        selected is not { } s ? null : OfAnotherTeam(game, p, acting, s) ? s : before;

    /// <summary>The units whose threat draws: the pinned unit (<see cref="Pinned"/>), then the hovered one; each of another team, with a threat, once.</summary>
    public static IReadOnlyList<Guid> Shown(GameInfo game, Projection p, Guid? acting, Guid? pinned, Guid? hovered, IReadOnlyDictionary<Guid, UnitThreat> threats)
    {
        var shown = new List<Guid>(2);
        foreach (var unit in new[] { pinned, hovered })
            if (unit is { } id && !shown.Contains(id) && threats.ContainsKey(id) && OfAnotherTeam(game, p, acting, id)) shown.Add(id);
        return shown;
    }

    /// <summary>The T key: per team other than the one I act for, the union of its units' reach by kind, one Charge area (each unit's charge round
    /// its models) and one Shoot area (each model's ShootMove + range), labelled with the distance, or the shortest–longest when they differ.</summary>
    public static IReadOnlyList<TeamThreat> All(GameInfo game, Projection p, Guid? acting, IReadOnlyDictionary<Guid, UnitThreat> threats)
    {
        var charge = new Dictionary<Guid, List<(PlacedModel, double)>>(); var shoot = new Dictionary<Guid, List<(PlacedModel, double)>>();
        foreach (var m in p.Models.Values)
        {
            if (m.Location is not OnTable on || p.Units.GetValueOrDefault(m.UnitId) is not { } u || threats.GetValueOrDefault(m.UnitId) is not { } t) continue;
            if (TeamOf(game, u) is not { } team || team == acting) continue;
            var placed = new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), on.Placement);
            if (t.Charge is { } c) Add(charge, team, (placed, c));
            if (t.ShootMove is { } move && t.Ranges.TryGetValue(m.Id, out var range)) Add(shoot, team, (placed, move + range));
        }
        return game.Teams.OrderBy(t => t.Ordinal).Select(t => new TeamThreat(t.Id, t.Ordinal, Area(charge, t.Id, "Charge", RingKind.Charge).Concat(Area(shoot, t.Id, "Shoot", RingKind.Shoot)).ToList()))
            .Where(t => t.Rings.Count > 0).ToList();

        static void Add(Dictionary<Guid, List<(PlacedModel, double)>> into, Guid team, (PlacedModel, double) carrier)
        { if (!into.TryGetValue(team, out var list)) into[team] = list = []; list.Add(carrier); }
        static IEnumerable<RangeRing> Area(Dictionary<Guid, List<(PlacedModel M, double R)>> by, Guid team, string word, RingKind kind)
        {
            if (!by.TryGetValue(team, out var carriers)) yield break;
            double lo = carriers.Min(c => c.R), hi = carriers.Max(c => c.R);
            var label = lo == hi ? $"{word} {Inches(hi)}" : $"{word} {lo.ToString("0.#", CultureInfo.InvariantCulture)}–{Inches(hi)}";
            yield return new RangeRing(hi, label, RangeOutline.Of(carriers.Select(c => c.M).ToList(), carriers.Select(c => c.R).ToList()), kind);
        }
    }

    static Guid? TeamOf(GameInfo game, UnitInPlay u) => game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId;

    static string Inches(double d) => d.ToString("0.#", CultureInfo.InvariantCulture) + "\"";

    /// <summary>Two threats that draw the same rings.</summary>
    public static bool Same(UnitThreat a, UnitThreat b) => a.Charge == b.Charge && a.ShootMove == b.ShootMove && a.Ranges.Count == b.Ranges.Count
        && a.Ranges.All(r => b.Ranges.TryGetValue(r.Key, out var v) && v == r.Value);
}

/// <summary>One unit's threat rings, recomputed only when the unit, its models' slots or placements (<see cref="RangeRings.Key"/>) or its threat change.</summary>
public sealed class ThreatRingCache
{
    Guid _unit; IReadOnlyList<(Guid, ModelSlot, Placement)>? _key; UnitThreat? _threat; IReadOnlyList<RangeRing> _rings = [];

    public IReadOnlyList<RangeRing> Of(Projection p, Guid unitId, UnitThreat threat)
    {
        var key = RangeRings.Key(p, unitId);
        if (_key is not null && _threat is not null && unitId == _unit && key.SequenceEqual(_key) && ThreatRings.Same(threat, _threat)) return _rings;
        (_unit, _key, _threat, _rings) = (unitId, key, threat, ThreatRings.Of(p, unitId, threat));
        return _rings;
    }
}

/// <summary>One enemy team's threat areas under the T key: a Charge area and a Shoot area, in the team's colour.</summary>
public sealed record TeamThreat(Guid TeamId, int Ordinal, IReadOnlyList<RangeRing> Rings);

/// <summary>The T key's areas (<see cref="ThreatRings.All"/>), recomputed only when the team acted for, a unit with a threat, its models' slots or
/// placements, or its threat change; otherwise the same list, so the drawn meshes stand.</summary>
public sealed class AllThreatCache
{
    Guid? _acting; List<(Guid Unit, IReadOnlyList<(Guid, ModelSlot, Placement)> At, UnitThreat Threat)>? _key; IReadOnlyList<TeamThreat> _teams = [];

    public IReadOnlyList<TeamThreat> Of(GameInfo game, Projection p, Guid? acting, IReadOnlyDictionary<Guid, UnitThreat> threats)
    {
        var key = threats.OrderBy(t => t.Key).Where(t => ThreatRings.OfAnotherTeam(game, p, acting, t.Key)).Select(t => (t.Key, RangeRings.Key(p, t.Key), t.Value)).ToList();
        if (_key is not null && acting == _acting && key.Count == _key.Count
            && key.Zip(_key).All(x => x.First.Key == x.Second.Unit && x.First.Item2.SequenceEqual(x.Second.At) && ThreatRings.Same(x.First.Value, x.Second.Threat))) return _teams;
        (_acting, _key, _teams) = (acting, key.Select(k => (k.Key, k.Item2, k.Value)).ToList(), ThreatRings.All(game, p, acting, threats));
        return _teams;
    }
}
