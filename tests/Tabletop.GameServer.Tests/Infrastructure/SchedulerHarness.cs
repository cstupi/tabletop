using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Dice for the turn tests: the next queued face, then a seeded Random. The engine draws each cursor index once, in order, so a queue is enough; Rehydrate redraws nothing.</summary>
public sealed class QueueDice : IDiceSource
{
    private readonly Random _random = new(7);
    public Queue<int> Dice { get; } = new();
    public int Draw(ulong seed, long index, int sides) => Dice.Count > 0 ? Dice.Dequeue() : _random.Next(1, sides + 1);
}

/// <summary>
/// The session's loop without the session, over the real engine: a Sim (interpreter plan Task 10) holding RulesEngine(rules, TestProperties.Core,
/// QueueDice, scheduler). Run() advances until the engine rests — interpreter while a frame exists, scheduler between sequences, exactly as
/// RulesEngine.Advance does it. Answers go through the real dispatcher. Default rules: FakeRules.Gf(), so action sequences are empty and finish at once.
/// </summary>
public sealed class SchedulerHarness
{
    readonly TestGames.Built _game;
    readonly Func<PropertySet, IScheduler> _scheduler;
    readonly IGameRules _rules;
    Sim _sim;

    public SchedulerHarness(IReadOnlyList<Army?> armies, Func<PropertySet, IScheduler>? scheduler = null, Enforcement enforcement = Enforcement.Warn,
        bool waits = false, int spectators = 0, IGameRules? rules = null, SchedulerShape shape = SchedulerShape.AlternatingActivation)
    {
        _rules = rules ?? FakeRules.Gf();
        _game = TestGames.Build(armies, shape, enforcement, waits, spectators, _rules);
        _scheduler = scheduler ?? (p => new AlternatingActivationScheduler(p));
        _sim = new Sim(_game.State, _rules, TestProperties.Core, Source, _scheduler(TestProperties.Core));
    }

    public GameState State => _sim.State;
    public QueueDice Source { get; } = new();
    public Queue<int> Dice => Source.Dice;
    public IReadOnlyList<Guid> Teams => _game.Teams;
    public IReadOnlyList<GameEvent> Log => State.Applier.Log;
    public Projection P => State.Projection;
    public IReadOnlyList<Guid> Units(int team) => TeamUnits.Of(State, Teams[team]).Select(u => u.Id).ToList();
    public Ask? OpenAsk(int team) => P.OpenAsks.Values.Select(a => a.Ask).FirstOrDefault(a => a.Teams.Contains(Teams[team]));
    public Ask Open => P.OpenAsks.Values.Select(a => a.Ask).Single();
    public IReadOnlyList<string> Kinds(long afterSeq) => Log.Where(e => e.Seq > afterSeq).Select(e => e.Kind).ToList();

    public void Append(IReadOnlyList<EventPayload> events, Guid? actor = null) => _sim.Append(events, actor);

    /// <summary>Until the engine rests: RulesEngine.Advance — interpreter while a frame exists, scheduler when the stack is empty and no framed Ask is open.</summary>
    public IReadOnlyList<EventPayload> Run() => _sim.Run();

    public IReadOnlyList<EventPayload> Dispatch(Command command, int? team = null)
    {
        var participant = team is { } t ? _game.Participants[t] : _game.Participants[^1];
        var outcome = _sim.Dispatch(command, participant, team is { } tt ? Teams[tt] : TeamUnits.TeamOf(State, participant));   // Dispatch.Applied / Dispatch.Refused: qualified, because Tabletop.Protocol has a Refused of its own
        if (outcome is Dispatch.Refused r) throw new InvalidOperationException($"{r.Reason}: {r.Detail}");
        return [.. ((Dispatch.Applied)outcome).Events, .. Run()];
    }

    public IReadOnlyList<EventPayload> Answer(int team, AskAnswer answer) => Dispatch(new AnswerAsk(OpenAsk(team)!.Id, answer), team);

    /// <summary>Ends the round: the boundary Ask is addressed to every team that has a participant, and any one of them may confirm it.</summary>
    public IReadOnlyList<EventPayload> EndRound(int team = 0) => Answer(team, new Confirmed());

    /// <summary>Releases the roll-off's confirm gate. The interpreter gates an actor-less root frame past position 0 whatever the wait setting says, with one unsealed Confirm to every team, so every roll-off waits here and any team may release it (interpreter plan Task 8). Its dice are editable until it is answered.</summary>
    public IReadOnlyList<EventPayload> ConfirmRollOff(int team = 0) => Answer(team, new Confirmed());

    public IReadOnlyList<EventPayload> Deploy(Guid unitId)
    {
        var team = Teams.ToList().IndexOf(TeamUnits.TeamOf(State, P.Units[unitId].ParticipantId)!.Value);
        return Dispatch(TestGames.Move(State, unitId), team);
    }

    /// <summary>Answers every deploy Ask with its first eligible unit and places it, until round one starts — releasing any roll-off gate it meets on the way, since deployment's roll-off and a free-for-all's round-one roll-off both wait at one.</summary>
    public void SkipDeployment()
    {
        Run();
        for (var i = 0; i < 200 && P.Scheduler.Round == 0; i++)
        {
            var ask = Open;
            if (ask.FrameId is not null) { ConfirmRollOff(); continue; }          // the interpreter's gate, not a deploy Ask
            var unit = ((PieceSelection)ask.Legal).Eligible[0];
            Answer(Teams.ToList().IndexOf(ask.Teams[0]), new SelectedPieces([unit]));
            Deploy(unit);
        }
    }

    /// <summary>A fresh applier over the same board and log, and a fresh engine: the one hydrate path. Run() afterwards must add nothing (and draws no dice: nothing is re-rolled).</summary>
    public void Rehydrate()
    {
        var applier = new EventApplier(State.Board, State.Info);
        applier.ApplyAll(State.Applier.Log);
        _sim = new Sim(new GameState(State.Info, State.Board, applier, State.Rules), _rules, TestProperties.Core, Source, _scheduler(TestProperties.Core));
    }
}
