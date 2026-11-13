using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Takedown);

/// <summary>One group per weapon type, shared by shooting (p.8) and melee (p.9), so a weapon's AP and Deadly reach only its own hits; a group resolves as
/// one attack per carrier signature (Split).</summary>
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

    /// <summary>
    /// The attacks a group resolves as: one per carrier signature, each keeping the group's name (its target declaration). A rule worded "This model …"
    /// acts on that model's attacks and a weapon's rules on that weapon's hits, while one attack's live set judges its rules over every model firing it
    /// (LiveProperties.Gather), so a joined Hero, a Sergeant or an upgraded model firing a weapon of the squad's name attacks apart from the squad:
    /// its model rules (less the unit's own and "This model and its unit" ones, which reach every model of the unit alike) and its weapon's rules and
    /// stats differ from theirs. Parts keep the group's weapon order.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Split(GameState state, PropertySet props, IEnumerable<WeaponGroup> groups)
    {
        var carrier = state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons.Select(w => (Weapon: w, Model: m))).ToDictionary(x => x.Weapon.Id);
        string Signature(Guid weaponId)
        {
            var (weapon, model) = carrier[weaponId];
            var own = model.Slot.Properties.Where(p => props.Entry(p.Name) is { Played: true, Kind: CatalogKind.Model } e && Signs(e)).Select(p => $"{p.Name}({p.Parameter})");
            var rules = weapon.Properties.Select(p => $"{p.Name}({p.Parameter})");
            var stats = weapon.Stats.Select(kv => $"{kv.Key}={kv.Value}");
            return string.Join("|", own.Order(StringComparer.Ordinal)) + "#" + string.Join("|", rules.Order(StringComparer.Ordinal)) + "#" + string.Join("|", stats.Order(StringComparer.Ordinal));
        }
        return groups.SelectMany(g => g.WeaponIds.Count < 2 ? [g] : g.WeaponIds.GroupBy(Signature).Select(part => g with { WeaponIds = part.ToList() })).ToList();
    }

    /// <summary>A model rule that can act on its carrier's attack: not a "This model and its unit" rule, not a pick entry (a Buff, Mark or Debuff acts
    /// only through the grants its pick writes), and not a rule acting on the unit only through its markers ("this unit gets +1 to Defense rolls"),
    /// except one any model may carry, whose markers reach only its carriers' attacks (LiveProperties.Gather).</summary>
    static bool Signs(CatalogEntry e) =>
        !LiveProperties.IsUnitWide(e) && e.Targeting is null
        && (e.Aggregation == Aggregation.Any || e.Effects.Count == 0 || !e.Effects.All(f => f.Duration is EffectDuration.Marker or EffectDuration.SpentMarker or EffectDuration.OneMarker));
}
