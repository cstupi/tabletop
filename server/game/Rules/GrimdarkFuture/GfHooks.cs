using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The hooks the scheduler calls, and who may react at a window. Every reading is of the projection and the log, never of a flag.</summary>
public static class GfHooks
{
    /// <summary>Round boundary: fatigue clears (action sequences → Fatigue); every Caster(X) gets X tokens, capped at 6, on the table or not (p.13).</summary>
    public static IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet props)
    {
        var changes = new List<StateChange>();
        foreach (var u in state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Fatigued)))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Fatigued, false));
        // p.13 gives tokens "even if they are not on the table" and says nothing of casualties; a dead model's tokens are never spent because ReactionsAt only counts AliveModels.
        foreach (var m in state.Projection.Models.Values)
            if (Lookup.ModelParameter(state, m.Id, GfNames.Caster) is { } x && x > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + x)));
        return changes;
    }

    /// <summary>turn_ended: the wounds-this-activation counters and moved clear (whole-design review → Gaps filled); an activation spent idle stops the unit being Shaken (p.10).</summary>
    public static IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet props, Scheduler.TurnContext turn)
    {
        var changes = state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0)).ToList();
        // moved is "moved this activation", which is what p.14 Indirect's "shooting after moving" reads.
        changes.AddRange(state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Moved)).Select(u => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Moved, false)));
        // p.10: "Shaken units must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation."
        // p.13: an Aircraft "may only use Advance actions … (even if Shaken)" and "their mandatory move doesn't break the staying idle requirement
        // to stop being Shaken" — so a Shaken Aircraft's Advance counts as having idled.
        if (turn.Activation is { } act && state.Projection.Units[act.UnitId].Conditions.Contains(GfNames.Shaken)
            && (act.Action == Scheduler.ActivationOption.Idle || (act.Action == GfNames.Advance && Lookup.UnitHas(state, props, act.UnitId, GfNames.Aircraft))))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, act.UnitId), GfNames.Shaken, false));
        return changes;
    }

    /// <summary>p.10: at the end of an activation, a unit that took wounds leaving it at half or less tests; units that fought this melee compared wounds instead. The actor bounds "this activation".</summary>
    public static IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet props, Guid? actor) =>
        UnitsToTestAtEndOfActivation(state, props, actor).Select(u => new SequenceStart(GfNames.MoraleTest, new SequenceContext(u, [], [], "wounds"))).ToList();

    public static IReadOnlyList<Guid> UnitsToTestAtEndOfActivation(GameState state, PropertySet props, Guid? actor) =>
        state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0 && AtHalfOrLess(state, props, u.Id) && !FoughtMeleeThisActivation(state, actor, u.Id) && Lookup.AliveModels(state, u.Id).Count > 0)
            .Select(u => u.Id).ToList();

    public static int StartingSize(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models.Count;

    /// <summary>Half or less of starting size, or of the Tough value for a single-model unit (p.10). Counted over the models still in play: a unit inside a transport has not lost a model by being carried.</summary>
    public static bool AtHalfOrLess(GameState state, PropertySet props, Guid unitId)
    {
        var start = StartingSize(state, unitId);
        if (start == 1)
        {
            var model = state.Projection.Units[unitId].Definition.Models[0];
            var tough = Lookup.ModelParameter(state, model.Id, GfNames.Tough) ?? 1;
            var remaining = state.Projection.Models[model.Id].Location is not OnShelf ? tough - state.Projection.Models[model.Id].Wounds : 0;
            return remaining * 2 <= tough;
        }
        return Lookup.AliveModels(state, unitId).Count * 2 <= start;
    }

    /// <summary>A melee sequence started since the actor's activation began, with this unit as charger or target. No activation frame in the log means no melee this activation — never a scan of the whole log, which would report a melee from an earlier round.</summary>
    public static bool FoughtMeleeThisActivation(GameState state, Guid? actor, Guid unitId)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        // Anchored on the four root sequences, which only the scheduler starts: nested frames inherit the action name in their context (R-N3).
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted { Sequence: GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq } s
            && (actor is null || s.Context.ActorUnitId == actor));
        if (activation < 0) return false;
        return log.Skip(activation).Any(e => e.Payload is SequenceStarted { Sequence: GfNames.Melee } m && (m.Context.ActorUnitId == unitId || m.Context.TargetUnitIds.Contains(unitId)));
    }

    /// <summary>
    /// Who may react at a point. At before-attacking the acting unit's team may cast if a caster can afford an untried spell (p.13). At every point,
    /// each unit the point concerns (the actor; at after-melee the charger and then the defender) offers its live move rows at that point
    /// (army-rule hooks spec), one reaction per entry, pushing `reposition`. A Shaken unit offers nothing: 3.5.1 p.10 says being idle means it "can't
    /// take any actions, or use any active special rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc.".
    /// </summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (frame.Context.ActorUnitId is not { } actor) return [];
        var reactions = new List<Reaction>();
        if (point == GfNames.BeforeAttacking && !Shaken(state, actor) && GfCasting.SpellOptions(state, props, actor).Count > 0 && Teams.Of(state, actor) is { } own)
            reactions.Add(new Reaction(own, "cast", "Cast a spell", GfNames.CastingSeq, new SequenceContext(actor, [], [], null)));
        IEnumerable<Guid> units = point == GfNames.AfterMelee ? [actor, .. frame.Context.TargetUnitIds.Take(1)] : [actor];
        foreach (var unit in units)
        {
            if (Shaken(state, unit) || Lookup.LivingModels(state, unit).Count == 0 || Teams.Of(state, unit) is not { } team) continue;
            var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, frame.Context.Action, null, Battlefield.TerrainUnder(state, unit), point));
            foreach (var (source, row) in live.AtPoint(Side.Attacker, point).Where(x => x.Effect.Kind is EffectKind.Move).DistinctBy(x => x.Source.Entry.Name))
                reactions.Add(new Reaction(team, $"{source.Entry.Name}|{unit:N}", Label(source.Entry.Name, row), GfNames.RepositionSeq, new SequenceContext(unit, [], [], source.Entry.Name)));
        }
        return reactions;
    }

    static bool Shaken(GameState state, Guid unit) => state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken);
    static string Label(string entry, Effect row) => $"{entry}: move up to {row.Amount}\"";
}
