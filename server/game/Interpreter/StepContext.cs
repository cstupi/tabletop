using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

public static class Teams
{
    public static Guid? Of(GameState state, Guid unitId) =>
        state.Projection.Units.TryGetValue(unitId, out var u) ? state.Info.Participants.FirstOrDefault(p => p.Id == u.ParticipantId)?.TeamId : null;
    public static IReadOnlyList<Guid> Others(GameState state, Guid? team) => state.Info.Teams.Select(t => t.Id).Where(t => t != team).ToList();
}

/// <summary>What a step sees: state, the pinned set, its frame and history, and a roller at the log's cursor.</summary>
public sealed class StepContext(GameState state, PropertySet properties, IGameRules rules, FrameView frame, Roller roller)
{
    private readonly List<RollRecord> _rolls = new();
    private readonly List<DiceRerolled> _forced = new();
    private readonly Dictionary<RollPlan, (LiveSet Live, IReadOnlyList<(int Face, bool SuccessOnly, LiveProperty Source)> Rerolls)> _rerolls = new(ReferenceEqualityComparer.Instance);

    public GameState State { get; } = state;
    public PropertySet Properties { get; } = properties;
    public IGameRules Rules { get; } = rules;
    public FrameView Frame { get; } = frame;
    public StepHistory History => Frame.History;
    public Projection Projection => State.Projection;
    public Guid? ActorTeam => Frame.Context.ActorUnitId is { } u ? Teams.Of(State, u) : null;
    public Guid? TeamOf(Guid unitId) => Teams.Of(State, unitId);
    public IReadOnlyList<RollRecord> Rolls => _rolls;
    public IReadOnlyList<DiceRerolled> ForcedRerolls => _forced;
    /// <summary>The catalog entries that acted in this run, for StepCompleted.Applied. Live sets settled through Settle record into it; a ruleset hook adds the rule it names.</summary>
    public AppliedEntries Applied { get; } = new();

    /// <summary>`held_&lt;unit&gt;_&lt;grant&gt;` for each grant this run read and held; the interpreter writes them into StepCompleted.Values.</summary>
    public IReadOnlyDictionary<string, int> Held => _held;
    private readonly Dictionary<string, int> _held = new();
    public const string HeldPrefix = "held_";

    /// <summary>
    /// The grants the live set read, spent: at once outside a sequence the ruleset holds once grants under (RollEffects.Consume), and under one
    /// (this frame or an ancestor) nothing now, each read grant held as `held_&lt;unit&gt;_&lt;grant&gt;` for the step that closes that sequence to release.
    /// </summary>
    public IReadOnlyList<StateChange> Consume(LiveSet live)
    {
        if (!UnderHoldingSequence) return RollEffects.Consume(live);
        foreach (var (unit, grant) in live.GrantsRead) _held[$"{HeldPrefix}{unit:N}_{grant:N}"] = 1;
        return [];
    }

    bool UnderHoldingSequence
    {
        get
        {
            for (var f = Frame; f is not null; f = f.Parent) if (Rules.HoldsOnceGrants(f.Frame.Sequence)) return true;
            return false;
        }
    }

    /// <summary>The two passes for this step: the live set records what its consumers apply into Applied.</summary>
    public LiveSet Settle(Interaction interaction) => LiveProperties.Settle(interaction, Applied);

    /// <summary>
    /// The roll spec supplies the threshold, unless the ruleset names one. A ruleset overrides it where the target number is a
    /// model's rather than the unit's, so the ruleset passes the value it read
    /// off the attacking model's slot and one such plan is built per distinct value.
    /// </summary>
    public RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null, TargetRead? threshold = null, CoverVerdict? cover = null)
    {
        var spec = Rules.RollSpec(roll);
        var read = threshold ?? spec.Threshold(live, roller);
        var (attributed, rerolls) = RollEffects.Attributed(live, roller, roll, read?.Value, baseModifiers, spec.Convention, requiredRaw, Rules.HitRoll);
        var plan = attributed with { Basis = read?.Basis, Cover = cover };   // before keying _rerolls, which compares by reference
        if (rerolls.Count > 0) _rerolls[plan] = (live, rerolls);
        return plan;
    }

    /// <summary>Every face of the plan's convention judged under it, lowest first: what a die showing that face would be, for a decision read off the
    /// face a die shows when the decision is used rather than when it was rolled.</summary>
    public IReadOnlyList<Die> Faces(RollPlan plan) => Enumerable.Range(1, plan.Convention.Sides).Select(raw => roller.Judge(plan, raw)).ToList();

    /// <summary>A forced reroll acted when the roller redrew a die showing its face: its source is recorded then, and only then.</summary>
    /// <summary>`triggers`, when given, names what a die set off (Die.Triggered). It is asked once per face of the convention, each face
    /// judged under the plan, into the record's FaceTriggers; the record's dice and a forced reroll's carry their face's, and so does a die
    /// the rules suite sets later (SetDice).</summary>
    public RollRecord Roll(RollPlan plan, int count, Func<Die, IReadOnlyList<string>?>? triggers = null)
    {
        var result = roller.Roll(Frame.Id, plan, count);
        if (triggers is not null)
        {
            var faces = Enumerable.Range(1, plan.Convention.Sides).Select(raw => triggers(roller.Judge(plan, raw)) is { Count: > 0 } t ? new FaceTriggers(raw, t) : null)
                .OfType<FaceTriggers>().ToList();
            var record = result.Record with { FaceTriggers = faces.Count == 0 ? null : faces };
            result = result with
            {
                Record = record with { Dice = record.Dice.Select(record.Stamp).ToList() },
                ForcedRerolls = result.ForcedRerolls is { } forced ? forced with { Replaced = forced.Replaced.Select(r => r with { Old = record.Stamp(r.Old), New = record.Stamp(r.New) }).ToList() } : null,
            };
        }
        _rolls.Add(result.Record);
        if (result.ForcedRerolls is { } f)
        {
            if (_rerolls.TryGetValue(plan, out var by))
            {
                foreach (var (face, successOnly, source) in by.Rerolls) if (f.Replaced.Any(d => d.Old.Raw == face && (!successOnly || d.Old.Success))) by.Live.Record(source);
                // The rule whose row matched each redrawn die, for the log (detailed-log spec): its face, and its success when the row asks for one.
                f = f with { Replaced = f.Replaced.Select(d => d with { By = by.Rerolls.FirstOrDefault(r => r.Face == d.Old.Raw && (!r.SuccessOnly || d.Old.Success)).Source?.Entry.Name }).ToList() };
            }
            _forced.Add(f);
        }
        return result.Record;
    }

    /// <summary>An Ask to one team; any seat of the team answers it (rulebook p.50 Team Games).</summary>
    public Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team], legal, allowPass, false, windowKind, Frame.Id);

    public Ask SealedAsk(string question, string prompt, IReadOnlyList<Guid> teams, LegalSet legal, string windowKind) =>
        new(Guid.CreateVersion7(), question, prompt, teams, legal, true, true, windowKind, Frame.Id);
}
