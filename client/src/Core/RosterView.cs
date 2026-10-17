using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Inside: the name of the unit whose model holds this one (passengers spec: the roster's "in {Transport}"), else null. Reserve: the unit is
/// held off the table until a property places it (ReserveOf), else null.</summary>
public sealed record RosterRow(Guid UnitId, string Name, bool Activated, int Left, int Max, bool OffTable, IReadOnlyList<string> Conditions, string? Inside = null, string? Reserve = null);
public sealed record RosterTeam(Guid TeamId, string Name, int Ordinal, IReadOnlyList<RosterRow> Rows);

/// <summary>The roster (spec → Army roster) from the projection only: my team first, the rest by ordinal, units in army order. Left is wounds left over
/// models not removed (UnitSheet's Removed: on the shelf with every wound taken); off table is no model on the table, nor inside a container on it.</summary>
public static class RosterView
{
    public static IReadOnlyList<RosterTeam> Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyDictionary<Guid, int> maxWounds) =>
        game.Teams.OrderBy(t => t.Id == myTeam ? 0 : 1).ThenBy(t => t.Ordinal).Select(t => new RosterTeam(t.Id, n.Team(t.Id), t.Ordinal,
            p.Units.Values.Where(u => game.Participants.Any(x => x.Id == u.ParticipantId && x.TeamId == t.Id)).Select(u => Row(p, u, maxWounds)).ToList())).ToList();

    static RosterRow Row(Projection p, UnitInPlay u, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var models = p.Models.Values.Where(m => m.UnitId == u.Id).Select(m => (M: m, Max: maxWounds.GetValueOrDefault(m.Id, 1))).ToList();
        var left = models.Where(x => !(x.M.Location is OnShelf && x.M.Wounds >= x.Max)).Sum(x => Math.Max(0, x.Max - x.M.Wounds));
        var holder = models.Select(x => x.M.Location).OfType<Inside>().Select(i => p.Models.GetValueOrDefault(i.ContainerId)).FirstOrDefault(h => h is not null);
        bool OnTable(ModelInPlay m) => m.Location is OnTable || (m.Location is Inside i && p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable);
        return new(u.Id, u.Definition.Name, u.Actions.Count > 0, left, models.Sum(x => x.Max), !models.Any(x => OnTable(x.M)),
            u.Conditions.Concat(models.SelectMany(x => x.M.Conditions)).Distinct().Order(StringComparer.Ordinal).ToList(),
            holder is not null && p.Units.GetValueOrDefault(holder.UnitId) is { } h ? h.Definition.Name : null, ReserveOf(p, u));
    }

    /// <summary>"Reserve: {property} ({unit})" for a unit the army holds off the table (every model on the shelf) until that property, held at a model or a unit of the same army,
    /// places it (the snapshot's Unit.Reserve, as imported); null for any other unit. The property is the snapshot's name, not one the client knows.</summary>
    public static string? ReserveOf(Projection p, UnitInPlay u)
    {
        if (u.Definition.Reserve is not { } r) return null;
        if (p.Models.Values.Any(m => m.UnitId == u.Id && (m.Location is not OnShelf || m.Wounds > 0))) return null;   // placed (and maybe since destroyed): no longer held
        var at = p.Units.GetValueOrDefault(r.Of)?.Definition.Name ?? (p.Models.GetValueOrDefault(r.Of) is { } m ? p.Units.GetValueOrDefault(m.UnitId)?.Definition.Name : null);
        return at is null ? $"Reserve: {r.Property}" : $"Reserve: {r.Property} ({at})";
    }

    public static string Pips(int left, int max) => new string('●', left) + new string('○', Math.Max(0, max - left));
    /// <summary>A server name as shown: first letter capitalised, the rest as sent.</summary>
    public static string Words(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
