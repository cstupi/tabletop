using Refused = Tabletop.GameServer.Dispatch.Refused;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RulesEngineTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Roller() => new("roller", [new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [new RollModifier("x", 1)], new RollConvention(6, null), null, new HashSet<int>()), 3); return new Complete([]); }),
        new LambdaStep("wait", ctx => ctx.History.LastClosed() is null ? new OpenAsks([ctx.Ask("go", "Go", ctx.ActorTeam!.Value, new Options([new AskOption("go", "Go", [])]))]) : new Complete([]))]);

    /// <summary>A scheduler that opens one Ask when the stack is empty and nothing has been asked yet.</summary>
    sealed class OneAsk(Ask ask) : IScheduler
    {
        public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) =>
            state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? [] : [new AskOpened(ask)];
    }

    static Sim Make(IScheduler? scheduler = null) => new(State([A], [B]), new StubRules(Roller()), TestProperties.Core, new ScriptedDice(2, 5, 6, 1, 3), scheduler);

    [Fact]
    public void Advance_runs_the_interpreter_while_frames_exist_and_the_scheduler_when_they_do_not()
    {
        var turn = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [Samples.Team1], new Options([new AskOption("a", "A", [])]), true, false, null, null);
        var sim = Make(new OneAsk(turn));
        Assert.Equal(["ask_opened"], sim.Run().Select(EventKinds.Of));                  // empty stack: the scheduler speaks
        Assert.Empty(sim.Run());                                                          // an open Ask blocks everything
        sim.Answer(Samples.Team1, new Passed());
        sim.Append(sim.Engine.Interpreter.Start(sim.State, "roller", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "ask_opened"], sim.Run().Select(EventKinds.Of));  // a frame: the interpreter speaks, the scheduler is silent
    }

    [Fact]
    public void Reroll_set_dice_and_add_modifier_are_judged_and_executed_against_the_frame()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal([3, 6, 7], roll.Dice.Select(d => d.Modified));
        var clay = new Actor(Samples.P1, Samples.Team1);

        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new RerollDice(frame, roll.Id, [0]), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new RerollDice(Guid.CreateVersion7(), roll.Id, []), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new AddModifier(frame, "save", 1), clay));
        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new AddModifier(frame, "defense", -1), clay));

        var rerolled = Assert.IsType<Applied>(sim.Dispatch(new RerollDice(frame, roll.Id, [0]), Samples.P1, Samples.Team1));
        var rr = Assert.IsType<DiceRerolled>(rerolled.Events.Single());
        Assert.Equal((0, 2, 1, 2, false), (rr.Replaced[0].Index, rr.Replaced[0].Old.Raw, rr.Replaced[0].New.Raw, rr.Replaced[0].New.Modified, rr.Replaced[0].New.Success));   // the 4th scripted die, +1, under threshold 4
        Assert.Equal(1, sim.State.Projection.Stack[0].Steps[0].Rolls[0].Dice[0].Raw);
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));

        var edited = Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(1, 3)]), Samples.P1, Samples.Team1));
        var ed = Assert.IsType<DiceEdited>(edited.Events.Single());
        Assert.Equal((5, 3, 4, true), (ed.Replaced[0].Old.Raw, ed.Replaced[0].New.Raw, ed.Replaced[0].New.Modified, ed.Replaced[0].New.Success));
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));                                  // an edit draws nothing

        var mod = Assert.IsType<Applied>(sim.Dispatch(new AddModifier(frame, "hit", 2), Samples.P1, Samples.Team1));
        Assert.Equal(new RollModifier("player", 2), Assert.IsType<ModifierAdded>(mod.Events.Single()).Modifier);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(sim.Dispatch(new RerollDice(frame, Guid.CreateVersion7(), []), Samples.P1, Samples.Team1)).Reason);
    }

    [Fact]
    public void The_factory_loads_the_pinned_set_and_refuses_an_unknown_hash()
    {
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var factory = new RulesEngineFactory(store, new DiceSource(), new NoSchedulerFactory());
        var game = Samples.Game with { PropertiesHash = TestProperties.Core.ContentHash };
        Assert.IsType<RulesEngine>(factory.Create(GrimdarkFutureRules.Instance, game));
        Assert.Throws<PropertiesNotFoundException>(() => factory.Create(GrimdarkFutureRules.Instance, Samples.Game with { PropertiesHash = "sha256:nope" }));
    }

    [Fact]
    public void WithSequences_shadows_a_parked_parent_so_its_child_finishes_and_the_stack_empties()
    {
        var parent = Guid.CreateVersion7();
        var rules = new WithSequences(new StubRules(Roller()), WithSequences.Parent("test-parent", "wounds"));
        var sim = new Sim(State([A], [B]), rules, TestProperties.Core, new ScriptedDice(4));
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(A.Id, [], [], null)), new StepCompleted(parent, "wounds", null, [], [])]);
        Assert.Equal(["sequence_finished"], sim.Run().Select(EventKinds.Of));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
