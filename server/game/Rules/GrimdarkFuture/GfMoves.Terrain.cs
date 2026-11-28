using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
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
    /// "Units may not move through impassable terrain, unless they have any rules that allow them to ignore it". p.36 Banner of Eternal Flame: "The
    /// Hero and its unit may ignore all terrain effects": difficult terrain's cap and the dangerous terrain test, moving or activated, as Flying's
    /// "ignore terrain effects" reads them; moving through impassable terrain is no terrain effect (Flying names it apart: "may move through units
    /// and terrain, and ignore terrain effects"), and cover, a benefit the banner "may" ignore, is kept.
    /// </summary>
    public sealed record TerrainRules(bool Aircraft, bool Flying, bool Strider, bool EternalFlame = false)
    {
        /// <summary>No volume is swept or listed at all: Aircraft.</summary>
        public bool IgnoresAllTerrain => Aircraft;
        /// <summary>No impassable volume is swept: Aircraft and Flying. Where a Flying model ends is still judged (LandsInImpassable).</summary>
        public bool PassesImpassable => Aircraft || Flying;
        /// <summary>p.11's 6" cap does not bind: Aircraft, Flying, Strider and the Banner of Eternal Flame.</summary>
        public bool IgnoresDifficult => Aircraft || Flying || Strider || EternalFlame;
        /// <summary>No dangerous terrain test, moving through it or activated in it: Aircraft, Flying and the Banner of Eternal Flame (Strider still tests).</summary>
        public bool IgnoresDangerous => Aircraft || Flying || EternalFlame;
        public double? DifficultCap => IgnoresDifficult ? null : GfNames.DifficultCap;
    }

    /// <summary>The unit's terrain rules, read from its live set (Flying, Strider and the banner can be granted) and its Aircraft.</summary>
    public static TerrainRules TerrainFor(StepContext ctx, Guid unit, LiveSet live) =>
        new(IsAircraft(ctx, unit), live.Has(Side.Attacker, GfNames.Flying), live.Has(Side.Attacker, GfNames.Strider), live.Has(Side.Attacker, GfNames.EternalFlame));

    /// <summary>
    /// The difficult ground a charge offer holds the unit to, as its charge move would (GfMelee.ChargeTargets): the Difficult volumes, the Steep ones,
    /// and the cap, all empty or null for a unit that ignores difficult terrain (TerrainRules.IgnoresDifficult: Flying, Strider, the Banner of Eternal
    /// Flame), and whether the unit counts as in difficult terrain where it stands (CountsAsIn, the move step's 6" allowance). `live` is the unit's
    /// charge set; nothing is recorded or spent.
    /// </summary>
    public static (IReadOnlyList<WorldVolume> Difficult, IReadOnlyList<WorldVolume> Steep, double? Cap, bool InDifficult) ReachTerrain(GameState state,
        PropertySet props, Guid unit, LiveSet live)
    {
        var rules = new TerrainRules(false, live.Has(Side.Attacker, GfNames.Flying), live.Has(Side.Attacker, GfNames.Strider), live.Has(Side.Attacker, GfNames.EternalFlame));
        if (rules.IgnoresDifficult) return ([], [], null, false);
        var terrain = Battlefield.Terrain(state);
        var under = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, GfNames.Charge, null, Battlefield.TerrainUnder(state, unit)));
        var inDifficult = under.Effects(Side.Attacker, EffectKind.CountAsTerrain).Any(x => x.Effect.TargetTerrain == TargetTerrain.Difficult);
        return (Declared(terrain, TerrainDeclarations.Difficult), Declared(terrain, TerrainDeclarations.Steep), GfNames.DifficultCap, inDifficult);
    }

    /// <summary>A terrain effect the unit skipped was skipped by its banner when it carries one (a relic is the game's, so reading it spends nothing),
    /// else by its Flying, which a move spends (ConsumeKeepingFlying keeps a granted one to the activation's end).</summary>
    static void RecordSkipped(LiveSet live, TerrainRules rules, bool useFlying)
    {
        if (rules.EternalFlame) live.Record(Side.Attacker, GfNames.EternalFlame);
        else if (useFlying) live.Use(Side.Attacker, GfNames.Flying);
        else live.Record(Side.Attacker, GfNames.Flying);
    }

    /// <summary>The volumes a move of this unit sweeps: none for an Aircraft.</summary>
    static IReadOnlyList<WorldVolume> SweptTerrain(GameState state, TerrainRules rules) => rules.IgnoresAllTerrain ? [] : Battlefield.Terrain(state);

    /// <summary>A MoveRegion's impassable, difficult and steep volume ids for this unit's move (steep null when there is none); its cap is TerrainRules.DifficultCap.</summary>
    public static (List<Guid> Impassable, List<Guid> Difficult, List<Guid>? Steep) RegionVolumes(GameState state, TerrainRules rules)
    {
        var terrain = SweptTerrain(state, rules);
        var steep = Declared(terrain, TerrainDeclarations.Steep).Select(v => v.Id).ToList();
        return (rules.PassesImpassable ? [] : Declared(terrain, TerrainDeclarations.Impassable).Select(v => v.Id).ToList(),
            Declared(terrain, TerrainDeclarations.Difficult).Select(v => v.Id).ToList(), steep.Count == 0 ? null : steep);
    }

    /// <summary>
    /// A MoveRegion's dangerous terrain for this unit's move, as ApplyMove tests it: none for a unit that ignores it (Aircraft, Flying, the Banner of
    /// Eternal Flame; Strider still tests). 3.5.1: every Dangerous volume, and on a Rush or a Charge every DangerousOnRushCharge one too (p.11, p.12),
    /// at any length. 3.6.0: every Dangerous volume at any length, and the DangerousOnRushCharge ones as Unstable (p.12: "If any model in a unit moves
    /// over 6” when moving into or through unstable terrain, then it counts as Dangerous terrain"), over what is left of 6" after `usedBefore`, the
    /// earlier segments of the move. An empty list is null.
    /// </summary>
    public static (List<Guid>? Dangerous, List<Guid>? Unstable, double? UnstableOver) RegionDangers(GameState state, TerrainRules rules, string? action, double usedBefore = 0)
    {
        if (rules.IgnoresDangerous) return (null, null, null);
        var terrain = SweptTerrain(state, rules);
        static List<Guid>? Ids(IReadOnlyList<WorldVolume> volumes) => volumes.Count == 0 ? null : volumes.Select(v => v.Id).ToList();
        if (!GfEdition.Of(state).UnstableByDistance) return (Ids(DangerousFor(terrain, RushOrCharge(action))), null, null);
        var unstable = Ids(Declared(terrain, TerrainDeclarations.DangerousOnRushCharge));
        return (Ids(Declared(terrain, TerrainDeclarations.Dangerous)), unstable, unstable is null ? null : Math.Max(0, GfNames.UnstableDistance - usedBefore));
    }

    /// <summary>The Ask region of a move another team makes for this unit (a spell's move kind): the unit's alive models, the allowance, and the
    /// volumes, cap and separation its own move would have (TerrainFor; no separation for an Aircraft, p.13).</summary>
    public static MoveRegion ForcedMoveRegion(StepContext ctx, Guid unit, double allowance)
    {
        var rules = TerrainFor(ctx, unit, Lone(ctx, unit, null));
        var (impassable, difficult, steep) = RegionVolumes(ctx.State, rules);
        return new MoveRegion(Lookup.AliveModels(ctx.State, unit), allowance, impassable, difficult, rules.DifficultCap,
            Separation: rules.Aircraft ? null : GfNames.Separation, PassesModels: rules.PassesImpassable, IgnoresTerrain: rules.IgnoresAllTerrain, SteepVolumeIds: steep);
    }

    /// <summary>
    /// A move another team makes for this unit (a spell's move kind), judged as ForcedMoveRegion offered it: the same settling as an action's move,
    /// measured against the allowance with the unit's own terrain rules (TerrainFor); a path through an impassable volume the unit may not pass is
    /// refused, and so is a landing touching one, unless the unit is an Aircraft (p.13 "when moving and stopping"); the move is measured along the path
    /// its base takes round them (Movement.Check). A model whose base already touches
    /// an impassable volume where it stands (only a hand move puts it there; p.7 "never ... be placed in physically impossible locations", and the book
    /// says nothing of moving out) may be moved out of it, not along it or deeper in, as PathBreach judges it; one chosen by the opponent may not
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
        var steep = Declared(terrain, TerrainDeclarations.Steep);
        var moves = new List<ModelRelocation>();
        // Round the other models as ForcedMoveRegion's Ask is judged (Route: p.7, each other unit's perimeter grown by its Separation).
        var standing = rules.PassesImpassable ? Others.None : Others.Except(ctx.State, proposed.Select(m => m.ModelId).ToHashSet());
        var units = standing.For(unit, rules.Aircraft ? null : GfNames.Separation, null);
        foreach (var m in proposed.Where(m => m.To is OnTable && ctx.Projection.Models.TryGetValue(m.ModelId, out var mm) && mm.UnitId == unit))
        {
            if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
            var settled = Battlefield.Settle(ctx.State, m.ModelId, ((OnTable)m.To).Placement);
            var check = Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, rules.DifficultCap, ctx.State.Board.Table.Size,
                standing.Models, Battlefield.Terrain(ctx.State), steep: steep, units: units);
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
