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
            _forced.Add(f);
            if (_rerolls.TryGetValue(plan, out var by))
                foreach (var (face, successOnly, source) in by.Rerolls) if (f.Replaced.Any(d => d.Old.Raw == face && (!successOnly || d.Old.Success))) by.Live.Record(source);
        }
        return result.Record;
    }

    /// <summary>An Ask to one team; any seat of the team answers it (rulebook p.50 Team Games).</summary>
    public Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team], legal, allowPass, false, windowKind, Frame.Id);

    public Ask SealedAsk(string question, string prompt, IReadOnlyList<Guid> teams, LegalSet legal, string windowKind) =>
        new(Guid.CreateVersion7(), question, prompt, teams, legal, true, true, windowKind, Frame.Id);
}
