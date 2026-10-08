using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public enum AttackKind { Shooting, Melee, Spell }
public enum Side { Attacker, Defender }

/// <summary>One side of an interaction: the unit, the models taking part, the weapons in use.</summary>
public sealed record Party(Guid UnitId, IReadOnlyList<Guid> ModelIds, IReadOnlyList<Guid> WeaponIds)
{
    public static Party Whole(GameState state, Guid unitId) => new(unitId, Lookup.AliveModels(state, unitId), []);
    public static Party Of(GameState state, Guid unitId, IEnumerable<Guid> modelIds, IEnumerable<Guid> weaponIds) => new(unitId, modelIds.ToList(), weaponIds.ToList());
}

/// <summary>What the two passes evaluate against. Defender is null for a lone roll (a resolve check, a spell attempt, a hazard test).
/// SpellRules are a spell's weapon rules (the child rules of its add_hit): they join the attacker's instances before pass one, as a weapon's rules do.</summary>
public sealed record Interaction(GameState State, PropertySet Properties, Party Attacker, Party? Defender, AttackKind? Attack, string? ActionTaken, double? RangeInches, IReadOnlyList<string> DefenderTerrain,
    string? Point = null, IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? Faces = null,
    IReadOnlyList<(string Entry, int? Parameter)>? SpellRules = null)
{
    public Party Of(Side side) => side == Side.Attacker ? Attacker : Defender ?? throw new InvalidOperationException("no defender in this interaction");
    public static Side Other(Side side) => side == Side.Attacker ? Side.Defender : Side.Attacker;
    public int Round => State.Projection.Scheduler.Round;
}
