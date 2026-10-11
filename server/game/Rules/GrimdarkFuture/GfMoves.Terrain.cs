using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>How a unit's own rules treat terrain on every move the engine makes of it, a spell's forced move, and a granted Flying held for the activation.</summary>
public static partial class GfMoves
{
    /// <summary>
    /// How a unit's own rules let it treat terrain when it moves: one reading for every move the engine makes of it (its action's move, a rule's
    /// reposition, a spell's forced move: owner ruling, "Spells move as however that unit moves") and for the activation's dangerous terrain test.
    /// p.13 Aircraft: "Aircraft ignore all units and terrain when moving and stopping". p.13 Flying: "Models with this special rule may move through
    /// units and terrain, and ignore terrain effects whilst moving". p.14 Strider: "May ignore the effects of difficult terrain when moving". p.11:
    /// "Units may not move through impassable terrain, unless they have any rules that allow them to ignore it".
    /// </summary>
    public sealed record TerrainRules(bool Aircraft, bool Flying, bool Strider)
    {
        /// <summary>No volume is swept or listed at all: Aircraft.</summary>
        public bool IgnoresAllTerrain => Aircraft;
        /// <summary>No impassable volume is swept: Aircraft and Flying. Where a Flying model ends is still judged (LandsInImpassable).</summary>
        public bool PassesImpassable => Aircraft || Flying;
        /// <summary>p.11's 6" cap does not bind: Aircraft, Flying and Strider.</summary>
        public bool IgnoresDifficult => Aircraft || Flying || Strider;
        /// <summary>No dangerous terrain test, moving through it or activated in it: Aircraft and Flying (Strider still tests).</summary>
        public bool IgnoresDangerous => Aircraft || Flying;
        public double? DifficultCap => IgnoresDifficult ? null : GfNames.DifficultCap;
    }

    /// <summary>The unit's terrain rules, read from its live set (Flying and Strider can be granted) and its Aircraft.</summary>
    public static TerrainRules TerrainFor(StepContext ctx, Guid unit, LiveSet live) =>
        new(IsAircraft(ctx, unit), live.Has(Side.Attacker, GfNames.Flying), live.Has(Side.Attacker, GfNames.Strider));

    /// <summary>The volumes a move of this unit sweeps: none for an Aircraft.</summary>
    static IReadOnlyList<WorldVolume> SweptTerrain(GameState state, TerrainRules rules) => rules.IgnoresAllTerrain ? [] : Battlefield.Terrain(state);

    /// <summary>A MoveRegion's impassable and difficult volume ids for this unit's move; its cap is TerrainRules.DifficultCap.</summary>
    public static (List<Guid> Impassable, List<Guid> Difficult) RegionVolumes(GameState state, TerrainRules rules)
    {
        var terrain = SweptTerrain(state, rules);
        return (rules.PassesImpassable ? [] : Declared(terrain, TerrainDeclarations.Impassable).Select(v => v.Id).ToList(),
            Declared(terrain, TerrainDeclarations.Difficult).Select(v => v.Id).ToList());
    }

    /// <summary>The Ask region of a move another team makes for this unit (a spell's move kind): the unit's alive models, the allowance, and the
    /// volumes, cap and separation its own move would have (TerrainFor; no separation for an Aircraft, p.13).</summary>
    public static MoveRegion ForcedMoveRegion(StepContext ctx, Guid unit, double allowance)
    {
        var rules = TerrainFor(ctx, unit, Lone(ctx, unit, null));
        var (impassable, difficult) = RegionVolumes(ctx.State, rules);
        return new MoveRegion(Lookup.AliveModels(ctx.State, unit), allowance, impassable, difficult, rules.DifficultCap,
            Separation: rules.Aircraft ? null : GfNames.Separation, PassesModels: rules.PassesImpassable);
    }

    /// <summary>
    /// A move another team makes for this unit (a spell's move kind), judged as ForcedMoveRegion offered it: the same settling as an action's move,
    /// measured against the allowance with the unit's own terrain rules (TerrainFor); a path through an impassable volume the unit may not pass is
    /// refused, and so is a landing touching one, unless the unit is an Aircraft (p.13 "when moving and stopping"); one chosen by the opponent may not
    /// carry a model off the table. Dangerous terrain crossed is not tested, since no rule asks it of a forced move. A granted Flying is read for the
    /// terrain but neither spent nor held (owner ruling: the forced move is not the target's activation, so the grant waits for its own); the Values
    /// stay empty and are returned for the step to write.
    /// </summary>
    public static (IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int> Values) ApplyForcedMove(StepContext ctx, Guid unit, double allowance, IReadOnlyList<ModelRelocation> proposed)
    {
        var live = Lone(ctx, unit, null);
        var rules = TerrainFor(ctx, unit, live);
        var terrain = SweptTerrain(ctx.State, rules);
        var impassable = rules.PassesImpassable ? [] : Declared(terrain, TerrainDeclarations.Impassable);
        var landing = Declared(terrain, TerrainDeclarations.Impassable);
        var difficult = Declared(terrain, TerrainDeclarations.Difficult);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => m.To is OnTable && ctx.Projection.Models.TryGetValue(m.ModelId, out var mm) && mm.UnitId == unit))
        {
            if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
            var settled = Battlefield.Settle(ctx.State, m.ModelId, ((OnTable)m.To).Placement);
            var check = Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, rules.DifficultCap, ctx.State.Board.Table.Size);
            if (check.WithinAllowance && !check.CrossesImpassable && !check.LeavesTable && !LandsInImpassable(ctx.State, m.ModelId, settled, landing))
                moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return (moves.Count == 0 ? [] : [new ModelsMoved(moves)], new Dictionary<string, int>());
    }

    /// <summary>
    /// Owner ruling: "Granted flying applies to the whole move - its per activation". RollEffects.Consume, less the grants of every spell that granted
    /// a Flying live on this set (a spell's grants go together: spell library → consumed together). Those are kept on the unit and named in the Values
    /// instead, one GfNames.FlyingHeldPrefix key per grant, so the Flying stays live for the rest of the activation (the activation-start test, every
    /// leg of a split move, the landing) and FlyingHeldThisActivation removes it when the activation ends. A spell's forced move of the unit does not
    /// call it: that is not the unit's activation. This is the one place a granted rule is held past the step that used it, and it is held by name.
    /// </summary>
    public static (List<StateChange> Changes, Dictionary<string, int> Values) ConsumeKeepingFlying(LiveSet live)
    {
        var units = live.Interaction.State.Projection.Units;
        var flying = live.Live.Where(p => p.Entry.Name == GfNames.Flying && p.GrantId is not null).Select(p => p.GrantId!.Value).ToHashSet();
        var spells = units.Values.SelectMany(u => u.Grants.Where(g => flying.Contains(g.Id)).Select(g => (u.Id, g.EntryName))).ToHashSet();
        var changes = new List<StateChange>(); var held = new Dictionary<string, int>();
        foreach (var change in RollEffects.Consume(live))
        {
            if (change is GrantRemoved r && units[r.UnitId].Grants.FirstOrDefault(g => g.Id == r.GrantId) is { } grant && spells.Contains((r.UnitId, grant.EntryName)))
                held[$"{GfNames.FlyingHeldPrefix}{r.UnitId:N}_{r.GrantId:N}"] = 1;
            else changes.Add(change);
        }
        return (changes, held);
    }

    /// <summary>
    /// At the end of an activation (GfHooks.OnTurnEnded): a GrantRemoved for every grant a step held since the activation was answered
    /// (ConsumeKeepingFlying), that is still on its unit. Idempotent, as the player-turn shape's repeated turn-end hook requires.
    /// </summary>
    public static IReadOnlyList<StateChange> FlyingHeldThisActivation(GameState state, long answeredAtSeq)
    {
        var removed = new List<StateChange>();
        var keys = EventApplier.EffectiveLog(state.Applier.Log).Where(e => e.Seq > answeredAtSeq).Select(e => e.Payload).OfType<StepCompleted>()
            .SelectMany(s => s.Values?.Keys ?? []).Where(k => k.StartsWith(GfNames.FlyingHeldPrefix, StringComparison.Ordinal)).Distinct();
        foreach (var key in keys)
        {
            var ids = key[GfNames.FlyingHeldPrefix.Length..].Split('_');
            if (ids.Length == 2 && Guid.TryParseExact(ids[0], "N", out var unit) && Guid.TryParseExact(ids[1], "N", out var grant)
                && state.Projection.Units.TryGetValue(unit, out var u) && u.Grants.Any(g => g.Id == grant))
                removed.Add(new GrantRemoved(unit, grant));
        }
        return removed;
    }
}
