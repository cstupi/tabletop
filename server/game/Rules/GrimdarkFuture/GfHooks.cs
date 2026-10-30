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
        foreach (var u in state.Projection.Units.Values)
            foreach (var c in new[] { GfNames.MovedThisRound, GfAmbush.AmbushedThisRound, GfAmbush.ReDeployed }.Where(u.Conditions.Contains))
                changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, u.Id), c, false));
        // Caster Group's caster and Spell Accumulator's tokens with them (GfSpellSupport).
        changes.AddRange(GfSpellSupport.RoundStartTokens(state));
        // The Growths: "place one marker on it at the beginning of each round if it's on the table and isn't Shaken".
        changes.AddRange(GfMarkers.AtRoundStart(state, props));
        return changes;
    }

    /// <summary>turn_ended: the wounds-this-activation counters and moved clear (whole-design review → Gaps filled); an activation spent idle stops the unit being Shaken (p.10).</summary>
    public static IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet props, Scheduler.TurnContext turn)
    {
        var changes = state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0)).ToList();
        // moved is "moved this activation", which is what p.14 Indirect's "shooting after moving" reads.
        changes.AddRange(state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Moved)).Select(u => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Moved, false)));
        // Instinctive's mark is the activation's: "that attack".
        changes.AddRange(state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.InstinctiveCondition)).Select(u => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.InstinctiveCondition, false)));
        // Owner ruling: a granted Flying "applies to the whole move - its per activation": what this activation used and kept is spent now.
        changes.AddRange(GfMoves.FlyingHeldThisActivation(state, turn.AnsweredAtSeq));
        // p.10: "Shaken units must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation."
        // p.13: an Aircraft "may only use Advance actions … (even if Shaken)" and "their mandatory move doesn't break the staying idle requirement
        // to stop being Shaken" — so a Shaken Aircraft's Advance counts as having idled.
        if (turn.Activation is { } act && state.Projection.Units[act.UnitId].Conditions.Contains(GfNames.Shaken)
            && (act.Action == Scheduler.ActivationOption.Idle || (act.Action == GfNames.Advance && Lookup.UnitHas(state, props, act.UnitId, GfNames.Aircraft))))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, act.UnitId), GfNames.Shaken, false));
        // p.14 Scout: "May be set aside before deployment. After all other units are deployed, may be deployed ..." — once deployed it is no longer set aside.
        if (turn.Activation is { Action: GfNames.Deploy } deployed) changes.AddRange(ArrivedFromSetAside(state, deployed.UnitId));
        return changes;
    }

    /// <summary>The set-aside conditions a deployed unit clears: its own, and (p.15: "units inside Transports are deployed at the same time as the Transport") those of the units inside its models.</summary>
    public static IReadOnlyList<StateChange> ArrivedFromSetAside(GameState state, Guid unitId)
    {
        var units = new[] { unitId }.Concat(state.Projection.Units[unitId].Definition.Models.SelectMany(m => Lookup.OccupantUnits(state, m.Id))).Distinct();
        return units.Where(u => state.Projection.Units[u].Conditions.Contains(GfNames.SetAside))
            .Select(u => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, u), GfNames.SetAside, false)).ToList();
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
    /// Who may react at a point. At before-attacking the acting unit's team may cast if a caster can afford an untried spell (p.13); under 3.6.0
    /// only until the activation's one cast window is over. At every point,
    /// each unit the point concerns (the actor; at after-melee the charger and then the defender) offers its live move rows at that point
    /// (army-rule hooks spec), one reaction per entry, pushing `reposition`. A Shaken unit offers nothing: 3.5.1 p.10 says being idle means it "can't
    /// take any actions, or use any active special rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc.".
    /// At before-attacking each carrying model also offers its Buff, Mark or Debuff (GfPick), pushing `pick-rule`.
    /// </summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (frame.Context.ActorUnitId is not { } actor) return [];
        var reactions = new List<Reaction>();
        // 3.6.0 p.13 "Once per activation": no cast once the activation's one window is over (GfCasting.WindowClosed).
        if (point == GfNames.BeforeAttacking && !Shaken(state, actor) && !(GfEdition.Of(state).OneCastWindow && GfCasting.WindowClosed(frame, actor))
            && GfCasting.SpellOptions(state, props, actor).Count > 0 && Teams.Of(state, actor) is { } own)
            reactions.Add(new Reaction(own, GfCasting.CastOption, "Cast a spell", GfNames.CastingSeq, new SequenceContext(actor, [], [], null)));
        IEnumerable<Guid> units = point == GfNames.AfterMelee ? [actor, .. frame.Context.TargetUnitIds.Take(1)] : [actor];
        foreach (var unit in units)
        {
            if (Shaken(state, unit) || Lookup.LivingModels(state, unit).Count == 0 || Teams.Of(state, unit) is not { } team) continue;
            // Army-book rules spec → the pick: one use per carrying model that has not used it this activation and has a target now.
            if (point == GfNames.BeforeAttacking)
                foreach (var (entry, model) in GfPick.Offers(state, props, unit))
                    reactions.Add(new Reaction(team, GfPick.Key(entry.Name, model), $"{entry.Name} ({state.Projection.Models[model].Slot.Name}): pick a target",
                        GfNames.PickRuleSeq, new SequenceContext(unit, [], [], GfPick.Key(entry.Name, model))));
            // Spawn: "Once per game, when this model is activated, you may place a new unit of X" (GfReserves).
            if (point == GfNames.ActivationStart)
                foreach (var (model, reserve) in GfReserves.SpawnOffers(state, unit))
                    reactions.Add(new Reaction(team, $"{GfReserves.Spawn}|{model:N}", $"{GfReserves.Spawn} ({state.Projection.Models[model].Slot.Name}): place {state.Projection.Units[reserve].Definition.Name}",
                        GfReserves.PlaceReserveSeq, new SequenceContext(unit, [reserve], [], GfReserves.Spawn)));
            var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, frame.Context.Action, null, Battlefield.TerrainUnder(state, unit), point));
            if (point == GfNames.ActivationEnd && GfAmbush.MayReDeploy(state, unit))
                reactions.Add(new Reaction(team, $"{GfAmbush.AmbushReDeployment}|{unit:N}", $"{GfAmbush.AmbushReDeployment}: remove, and deploy as an Ambusher next round",
                    GfAmbush.AmbushRedeploySeq, new SequenceContext(unit, [], [], GfAmbush.AmbushReDeployment)));
            // A set_condition row at a point is one effect of a rule's choice ("pick one effect"): the rule offers one choose-effect use, which asks which.
            foreach (var (source, row) in live.AtPoint(Side.Attacker, point).Where(x => x.Effect.Kind is EffectKind.Move or EffectKind.Place or EffectKind.SetCondition).DistinctBy(x => x.Source.Entry.Name))
                reactions.Add(new Reaction(team, $"{source.Entry.Name}|{unit:N}", Label(source.Entry.Name, row),
                    row.Kind == EffectKind.SetCondition ? GfNames.ChooseEffectSeq : GfNames.RepositionSeq, new SequenceContext(unit, [], [], source.Entry.Name)));
        }
        return reactions;
    }

    static bool Shaken(GameState state, Guid unit) => state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken);
    static string Label(string entry, Effect row) => row.Kind == EffectKind.Place
        ? $"{entry}: place within {(row.AmountDice is null ? row.Amount?.ToString() : (row.Amount == 1 ? "" : row.Amount?.ToString()) + (row.AmountDice == "d3+1" ? "D3+1" : "D3"))}\""
        : row.Kind == EffectKind.SetCondition ? $"{entry}: pick an effect"
        : $"{entry}: move up to {row.Amount}\"";
}
