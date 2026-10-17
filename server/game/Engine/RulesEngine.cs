using System.Diagnostics;
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
    public PropertySet Properties => properties;

    public Legality Judge(GameState state, Command command, Actor actor) => command switch
    {
        RerollDice or SetDice or AddModifier => DiceLegal(state, command) ? Legality.Legal : Legality.Illegal,
        _ => Legality.Illegal,
    };

    /// <summary>The one predicate for a dice command: the roll exists on the frame, every index is a die of it and none repeats, and every set face is on the roll's convention; a modifier names a frame on the stack and a roll the pinned set defines. Judge asks it, and Execute asks it again because warn and silent run a command Judge called illegal.</summary>
    bool DiceLegal(GameState state, Command command) => command switch
    {
        RerollDice r => Find(state, r.FrameId, r.RollId) is { } rec && DistinctInRange(r.DiceIndices, rec.Dice.Count),
        SetDice s => Find(state, s.FrameId, s.RollId) is { } rec && DistinctInRange(s.Dice.Select(d => d.Index).ToList(), rec.Dice.Count)
            && s.Dice.All(d => d.Raw >= 1 && d.Raw <= PlanOf(rec).Convention.Sides),
        AddModifier m => state.Projection.Stack.Any(f => f.Id == m.FrameId) && properties.HasRoll(m.Roll),
        _ => false,
    };

    public EngineResult Execute(GameState state, Command command, Actor actor)
    {
        switch (command)
        {
            case RerollDice r when DiceLegal(state, r) && Find(state, r.FrameId, r.RollId) is { } record:
            {
                var roller = new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state));
                return new Emit([new DiceRerolled(r.FrameId, r.RollId, RollOutcome.Reroll(PlanOf(record), record, r.DiceIndices, roller).Select(d => d with { New = record.Stamp(d.New) }).ToList())]);
            }
            case SetDice s when DiceLegal(state, s) && Find(state, s.FrameId, s.RollId) is { } record:
            {
                var plan = PlanOf(record); var roller = new Roller(dice, state.RngSeed, 0);
                return new Emit([new DiceEdited(s.FrameId, s.RollId, s.Dice.Select(d => new DieReplacement(d.Index, record.Dice[d.Index], record.Stamp(roller.Judge(plan, d.Raw)))).ToList())]);
            }
            case AddModifier m when DiceLegal(state, m):
                return new Emit([new ModifierAdded(m.FrameId, m.Roll, new RollModifier("player", m.Amount))]);
            default:
                return new RefuseCommand(RefusalReason.Unknown, $"{command.GetType().Name}: no such frame, roll or die");
        }
    }

    /// <summary>
    /// Nothing while the game has ended. A framed Ask blocks everything; an unframed one is the scheduler's to wait on or close, except
    /// game_over, which GameOverAsk settles first. The proposal — the interpreter's while frames exist, the scheduler's otherwise — is
    /// judged by the ruleset and decided by the game's enforcement: through as it is, through with a warning, replaced by game_ended, or,
    /// under warn, held behind the game_over Ask whose answers choose between the last two.
    /// </summary>
    public Emit Advance(GameState state)
    {
        if (state.Projection.Ended is not null) return Emit.None;
        if (state.Projection.OpenAsks.Values.Any(a => a.Ask.FrameId is not null)) return Emit.None;
        if (GameOverAsk.Settle(state) is { Count: > 0 } settled) return new Emit(settled);
        var proposed = state.Projection.Stack.Count > 0 ? Interpreter.Advance(state) : scheduler.Advance(state, Interpreter);
        if (proposed.Count == 0) return Emit.None;
        var found = rules.JudgeAdvance(state, properties, proposed);
        var breach = found is null ? null : new Breach(found.Rule, found.Detail, new StrictEndsGame(found.Winners));
        return EnforcementPolicy.Decide(state.Info.Enforcement, breach) switch
        {
            Accept a => new Emit(proposed, a.Warning),
            EndGame e => new Emit(EnforcementPolicy.EndGamePayloads(state.Projection, e.Reason, e.Winners)),
            OfferEnd o => GameOverAsk.Read(state) switch
            {
                GameOverAsk.Decision.Continue => new Emit(proposed, o.Reason),
                GameOverAsk.Decision.End => new Emit(EnforcementPolicy.EndGamePayloads(state.Projection, o.Reason, o.Winners)),
                GameOverAsk.Decision.Waiting => Emit.None,
                _ => GameOverAsk.Teams(state) is { Count: > 0 } teams ? new Emit([new AskOpened(GameOverAsk.Of(teams, o.Reason))]) : new Emit(proposed, o.Reason),
            },
            Refuse r => throw new InvalidOperationException($"a breach in the engine's own proposal cannot refuse it: {r.Detail}"),
            var v => throw new UnreachableException(v.GetType().Name),
        };
    }

    public IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => scheduler.RoundStartChanges(state, round);

    static bool DistinctInRange(IReadOnlyList<int> indices, int count) => indices.All(i => i >= 0 && i < count) && indices.Distinct().Count() == indices.Count;

    static RollRecord? Find(GameState state, Guid frameId, Guid rollId) =>
        state.Projection.Stack.FirstOrDefault(f => f.Id == frameId)?.Steps.SelectMany(s => s.Rolls).FirstOrDefault(r => r.Id == rollId);

    /// <summary>A record re-judged under its own threshold, modifiers and required raw face, in the ruleset's convention for that roll.</summary>
    RollPlan PlanOf(RollRecord record) => new(record.Roll, record.Threshold, record.Modifiers, rules.RollSpec(record.Roll).Convention, record.RequiredRaw, new HashSet<int>());
}
