using Refused = Tabletop.GameServer.Dispatch.Refused;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A playing game driven the way the session drives it: dispatch a command, append, advance until the engine rests.</summary>
public sealed class Sim
{
    public GameState State { get; }
    public RulesEngine Engine { get; }
    public CommandDispatcher Dispatcher { get; }
    public List<EventPayload> Emitted { get; } = new();

    public Sim(GameState state, IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler? scheduler = null)
    {
        State = state;
        Engine = new RulesEngine(rules, properties, dice, scheduler ?? new NoScheduler());
        Dispatcher = new CommandDispatcher(Engine);
    }

    public static Sim Gf(GameState state, params int[] dice) => new(state, GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice.Length == 0 ? [4] : dice));

    public void Append(IEnumerable<EventPayload> events, Guid? actor = null)
    {
        foreach (var e in events) { State.Applier.Apply(new GameEvent(State.Projection.LastSeq + 1, actor, e, null)); Emitted.Add(e); }
    }

    /// <summary>Advance until the engine returns nothing (the session's AdvanceAsync loop).</summary>
    public IReadOnlyList<EventPayload> Run()
    {
        var from = Emitted.Count;
        for (var i = 0; i < 1000; i++) { var events = Engine.Advance(State); if (events.Count == 0) return Emitted.Skip(from).ToList(); Append(events); }
        throw new InvalidOperationException("the engine did not rest in 1000 steps");
    }

    public IReadOnlyList<EventPayload> Start(string sequence, SequenceContext context) { Append(Engine.Interpreter.Start(State, sequence, context)); return Run(); }

    /// <summary>The open Ask addressed to the team (exactly one expected).</summary>
    public Ask AskFor(Guid team) => State.Projection.OpenAsks.Values.Single(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team)).Ask;
    public Ask? AskForOrNull(Guid team) => State.Projection.OpenAsks.Values.SingleOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team))?.Ask;

    public DispatchOutcome Dispatch(Command command, Guid participant, Guid? team)
    {
        var outcome = Dispatcher.Dispatch(State, command, new Actor(participant, team));
        if (outcome is Applied a) Append(a.Events, participant);
        return outcome;
    }

    /// <summary>Answer the team's one open Ask, then run. Fails the test when the dispatcher refuses.</summary>
    public IReadOnlyList<EventPayload> Answer(Guid team, AskAnswer answer, Guid? participant = null)
    {
        var ask = AskFor(team);
        var outcome = Dispatch(new AnswerAsk(ask.Id, answer), participant ?? State.Info.Participants.First(p => p.TeamId == team).Id, team);
        if (outcome is Refused r) throw new Xunit.Sdk.XunitException($"refused: {r.Reason} {r.Detail}");
        return Run();
    }

    /// <summary>Answer every open step-boundary Confirm, whoever it is addressed to, until none is open; then the open Asks are the steps' own.</summary>
    public void Confirm()
    {
        for (var i = 0; i < 100; i++)
        {
            if (State.Projection.OpenAsks.Values.FirstOrDefault(a => a.Ask.Question == AskQuestions.Confirm) is not { } open) return;
            var team = open.Ask.Teams.First(t => !open.Answers.ContainsKey(t));
            Answer(team, new Confirmed());
        }
        throw new InvalidOperationException("the step-boundary Confirms did not stop");
    }

    public IReadOnlyList<StepCompleted> Steps(string name) => Emitted.OfType<StepCompleted>().Where(s => s.Step == name).ToList();
    public UnitInPlay Unit(Guid id) => State.Projection.Units[id];
    public ModelInPlay Model(Guid id) => State.Projection.Models[id];
    public int Alive(Guid unitId) => State.Projection.Models.Values.Count(m => m.UnitId == unitId && m.Location is OnTable);
}
