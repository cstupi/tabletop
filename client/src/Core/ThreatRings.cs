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

    /// <summary>The owner's "a model of the other team": a unit on no team of mine. A watcher holds none, so every unit is another's.</summary>
    public static bool OfAnotherTeam(GameInfo game, Projection p, IReadOnlyList<Guid> myTeams, Guid unitId)
    {
        if (p.Units.GetValueOrDefault(unitId) is not { } u) return false;
        var team = game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId;
        return team is null || !myTeams.Contains(team.Value);
    }

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
