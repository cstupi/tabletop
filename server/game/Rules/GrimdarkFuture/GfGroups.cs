using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Takedown);

/// <summary>One group per weapon type, shared by shooting (p.8) and melee (p.9), so a weapon's AP and Deadly reach only its own hits.</summary>
public static class GfGroups
{
    /// <summary>
    /// Groups by weapon name. A Takedown weapon type splits into one group per weapon, because p.14 gives the pick to "This model" and
    /// the group's name (the key a target declaration uses) carries the ordinal so two groups never share one. Order: p.14 says
    /// "Takedown attacks must be resolved before other weapons" and p.13 says "Hits from Deadly must be resolved first"; both say
    /// first, so Takedown leads on p.14's "before other weapons", then Deadly, then the rest, each by name.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Of(GameState state, IEnumerable<Weapon> weapons)
    {
        var groups = new List<(WeaponGroup Group, bool Deadly)>();
        foreach (var g in weapons.GroupBy(w => w.Name))
        {
            var ids = g.Select(w => w.Id).ToList();
            var deadly = g.Any(w => w.Properties.Any(p => p.Name == "Deadly"));
            if (!GfAttack.IsTakedownGroup(state, ids)) { groups.Add((new WeaponGroup(g.Key, ids, false), deadly)); continue; }
            groups.AddRange(ids.Select((id, i) => (new WeaponGroup(ids.Count == 1 ? g.Key : $"{g.Key} ({i + 1})", [id], true), deadly)));
        }
        return groups.OrderByDescending(x => x.Group.Takedown).ThenByDescending(x => x.Deadly).ThenBy(x => x.Group.Name, StringComparer.Ordinal).Select(x => x.Group).ToList();
    }
}
