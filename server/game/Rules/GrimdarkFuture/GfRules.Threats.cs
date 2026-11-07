using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The threat range plan's partial: how far a unit's next activation reaches, for the hover outline.</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>
    /// p.7: "Advance - Moves 6” and can shoot after moving", "Charge - Moves 12” into melee"; p.8 Who can shoot: a weapon "within range"; both from the
    /// base edge (p.5). Fast, Slow and every army-book distance rule are the action's modify_stat rows (StatReader.ActionDistance); a weapon's Range rows
    /// are read on a shooting interaction with no target (StatReader.PeekWeaponRange), so a row that needs a target reads the printed range.
    /// The limits are ActivationChoices' own, read without a target: Aircraft (p.13) Advance only, +30"; Immobile (p.14) and 3.5.1 Artillery (p.13)
    /// Hold only; 3.6.0 p.14 Cumbersome Hold or Advance "but can’t shoot when moving"; p.10 Shaken "must spend their activation being idle", and
    /// ShootOrNotStep ends a Shaken Aircraft's shot too; Quick Shot "may shoot after using Rush actions". Terrain and the target's rules are not read.
    /// </summary>
    public IReadOnlyDictionary<Guid, UnitThreat> Threats(GameState state, PropertySet properties)
    {
        var threats = new Dictionary<Guid, UnitThreat>();
        foreach (var unit in state.Projection.Units.Values)
        {
            var onTable = Lookup.LivingModels(state, unit.Id);
            if (onTable.Count > 0) threats[unit.Id] = ThreatOf(state, properties, unit, onTable);
        }
        return threats;
    }

    static UnitThreat ThreatOf(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<Guid> onTable)
    {
        var edition = GfEdition.Of(state);
        var terrain = Battlefield.TerrainUnder(state, unit.Id);
        LiveSet Live(string? action, AttackKind? kind) => LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit.Id), null, kind, action, null, terrain));
        // A set without the action (a test's bare set) gives no distance for it.
        double? Distance(string action) => props.Actions.Any(a => a.Name == action) ? StatReader.ActionDistance(Live(action, null), Side.Attacker, action) : null;
        bool Has(string rule) => Lookup.UnitHas(state, props, unit.Id, rule);

        var ranges = Ranges(state, Live(Advance, AttackKind.Shooting), onTable);
        if (unit.Conditions.Contains(Shaken)) return new(null, null, ranges);
        if (Has(Aircraft)) return new(null, (Distance(Advance) ?? 0) + GfNames.AircraftMove, ranges);
        if (Has(Immobile) || (edition.ArtilleryHoldOnly && Has(Artillery)) || (edition.HasCumbersome && Has(GfNames.Cumbersome)))
            return new(null, 0, ranges);
        var move = Distance(Advance) ?? 0;
        if (Has(GfNames.QuickShot) && Distance(Rush) is { } rush) move = Math.Max(move, rush);
        return new(Distance(Charge), move, ranges);
    }

    /// <summary>Each model on the table with a ranged weapon: its longest range as the live set leaves it.</summary>
    static IReadOnlyDictionary<Guid, double> Ranges(GameState state, LiveSet live, IReadOnlyList<Guid> onTable)
    {
        var ranges = new Dictionary<Guid, double>();
        foreach (var id in onTable)
        {
            var longest = state.Projection.Models[id].Slot.Weapons.Select(w => StatReader.PeekWeaponRange(live, Side.Attacker, w.Id)).OfType<double>().DefaultIfEmpty(0).Max();
            if (longest > 0) ranges[id] = longest;
        }
        return ranges;
    }
}
