using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>The one IRulesEngine. Dice commands are judged and executed here; Advance is the interpreter while frames exist and the scheduler when they do not.</summary>
public sealed class RulesEngine(IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler scheduler) : IRulesEngine
{
    public SequenceInterpreter Interpreter { get; } = new(rules, properties, dice);

    public Legality Judge(GameState state, Command command, Actor actor) => command switch
    {
        RerollDice r => Find(state, r.FrameId, r.RollId) is { } rec && DistinctInRange(r.DiceIndices, rec.Dice.Count) ? Legality.Legal : Legality.Illegal,
        SetDice s => Find(state, s.FrameId, s.RollId) is { } rec && DistinctInRange(s.Dice.Select(d => d.Index).ToList(), rec.Dice.Count)
            && s.Dice.All(d => d.Raw >= 1 && d.Raw <= PlanOf(rec).Convention.Sides) ? Legality.Legal : Legality.Illegal,
        AddModifier m => state.Projection.Stack.Any(f => f.Id == m.FrameId) && properties.HasRoll(m.Roll) ? Legality.Legal : Legality.Illegal,
        _ => Legality.Illegal,
    };

    public EngineResult Execute(GameState state, Command command, Actor actor)
    {
        switch (command)
        {
            case RerollDice r when Find(state, r.FrameId, r.RollId) is { } record:
            {
                var roller = new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state));
                return new Emit([new DiceRerolled(r.FrameId, r.RollId, RollOutcome.Reroll(PlanOf(record), record, r.DiceIndices, roller))]);
            }
            case SetDice s when Find(state, s.FrameId, s.RollId) is { } record:
            {
                var plan = PlanOf(record); var roller = new Roller(dice, state.RngSeed, 0);
                return new Emit([new DiceEdited(s.FrameId, s.RollId, s.Dice.Select(d => new DieReplacement(d.Index, record.Dice[d.Index], roller.Judge(plan, d.Raw))).ToList())]);
            }
            case AddModifier m when state.Projection.Stack.Any(f => f.Id == m.FrameId):
                return new Emit([new ModifierAdded(m.FrameId, m.Roll, new RollModifier("player", m.Amount))]);
            default:
                return new RefuseCommand(RefusalReason.Unknown, $"{command.GetType().Name}: no such frame or roll");
        }
    }

    /// <summary>A framed Ask blocks everything; an unframed one is the scheduler's to wait on or close.</summary>
    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        if (state.Projection.OpenAsks.Values.Any(a => a.Ask.FrameId is not null)) return [];
        if (state.Projection.Stack.Count > 0) return Interpreter.Advance(state);
        return scheduler.Advance(state, Interpreter);
    }

    static bool DistinctInRange(IReadOnlyList<int> indices, int count) => indices.All(i => i >= 0 && i < count) && indices.Distinct().Count() == indices.Count;

    static RollRecord? Find(GameState state, Guid frameId, Guid rollId) =>
        state.Projection.Stack.FirstOrDefault(f => f.Id == frameId)?.Steps.SelectMany(s => s.Rolls).FirstOrDefault(r => r.Id == rollId);

    /// <summary>A record re-judged under its own threshold and modifiers, in the ruleset's convention for that roll; RequiredRaw cannot be read back and is not re-applied.</summary>
    RollPlan PlanOf(RollRecord record) => new(record.Roll, record.Threshold, record.Modifiers, rules.RollSpec(record.Roll).Convention, null, new HashSet<int>());
}
