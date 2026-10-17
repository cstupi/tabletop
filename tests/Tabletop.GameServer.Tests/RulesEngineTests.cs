using Refused = Tabletop.GameServer.Dispatch.Refused;
using Microsoft.Extensions.Configuration;
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
    public void Set_dice_naming_a_missing_or_repeated_die_or_an_impossible_face_is_illegal()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        var clay = new Actor(Samples.P1, Samples.Team1);
        Legality Judge(Command c) => sim.Engine.Judge(sim.State, c, clay);

        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(roll.Dice.Count, 3)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(-1, 3)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 3), new DieValue(0, 4)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 0)])));
        Assert.Equal(Legality.Illegal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 7)])));
        Assert.Equal(Legality.Legal, Judge(new SetDice(frame, roll.Id, [new DieValue(0, 6), new DieValue(1, 1)])));
    }

    [Fact]
    public void Set_dice_is_judged_and_executed_against_the_frame()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal([3, 6, 7], roll.Dice.Select(d => d.Modified));
        var clay = new Actor(Samples.P1, Samples.Team1);
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new SetDice(Guid.CreateVersion7(), roll.Id, [new DieValue(0, 3)]), clay));

        var edited = Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(1, 3)]), Samples.P1, Samples.Team1));
        var ed = Assert.IsType<DiceEdited>(edited.Events.Single());
        Assert.Equal((5, 3, 4, true), (ed.Replaced[0].Old.Raw, ed.Replaced[0].New.Raw, ed.Replaced[0].New.Modified, ed.Replaced[0].New.Success));
        Assert.Equal(3, DiceCursor.DrawnSoFar(sim.State));                                  // an edit draws nothing
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(sim.Dispatch(new SetDice(frame, Guid.CreateVersion7(), [new DieValue(0, 3)]), Samples.P1, Samples.Team1)).Reason);
    }

    [Theory]
    [InlineData(Enforcement.Strict)]
    [InlineData(Enforcement.Warn)]
    [InlineData(Enforcement.Silent)]
    public void An_engine_that_does_not_accept_set_dice_refuses_it_under_every_setting(Enforcement enforcement)
    {
        var sim = new Sim(State([A], [B], enforcement: enforcement), new StubRules(Roller()), TestProperties.Core, new ScriptedDice(2, 5, 6), acceptsSetDice: false);
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        var seq = sim.State.Projection.LastSeq;
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new SetDice(frame, roll.Id, [new DieValue(0, 6)]), new Actor(Samples.P1, Samples.Team1)));
        Assert.IsType<Refused>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(0, 6)]), Samples.P1, Samples.Team1));
        Assert.Equal(seq, sim.State.Projection.LastSeq);
    }

    [Fact]
    public void Only_the_development_environment_accepts_set_dice()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TABLETOP_DB_CONNECTION"] = "Host=x", ["TABLETOP_STYTCH_PROJECT_ID"] = "project-test",
        }).Build();
        Assert.True(Tabletop.GameServer.Configuration.GameServerOptions.FromEnvironment(configuration, development: true).AcceptsSetDice);
        Assert.False(Tabletop.GameServer.Configuration.GameServerOptions.FromEnvironment(configuration, development: false).AcceptsSetDice);
    }

    [Fact]
    public void The_factory_loads_the_pinned_set_and_refuses_an_unknown_hash()
    {
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var factory = new RulesEngineFactory(store, new DiceSource(), new NoSchedulerFactory());   // SetDice off unless asked for
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

    /// <summary>A scheduler that proposes one given event until the log holds it.</summary>
    sealed class Proposes(EventPayload payload) : IScheduler
    {
        public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) =>
            state.Applier.Log.Any(e => ReferenceEquals(e.Payload, payload)) ? [] : [payload];
    }

    static readonly RoundStarted Fifth = new(5, [Samples.Team1, Samples.Team2], []);

    static Sim Judged(Enforcement enforcement, bool judge = true) => new(State([A], [B], enforcement: enforcement),
        new StubRules(Roller()) { Judge = judge ? p => p.OfType<RoundStarted>().Any() ? new GameEndingBreach("test_rule", "past the end", [Samples.Team1]) : null : null },
        TestProperties.Core, new ScriptedDice(1), new Proposes(Fifth));

    [Fact]
    public void An_unjudged_proposal_passes_through_with_no_warning()
    {
        var sim = Judged(Enforcement.Strict, judge: false);
        var emit = sim.Engine.Advance(sim.State);
        Assert.Equal([Fifth], emit.Events);
        Assert.Null(emit.Warning);
    }

    [Fact]
    public void Silent_lets_the_breach_through_and_warn_asks_then_marks_it_when_every_team_continues()
    {
        var silent = Judged(Enforcement.Silent);
        var quiet = silent.Engine.Advance(silent.State);
        Assert.Equal([Fifth], quiet.Events);
        Assert.Null(quiet.Warning);

        var warn = Judged(Enforcement.Warn);
        var asked = Assert.IsType<AskOpened>(Assert.Single(warn.Engine.Advance(warn.State).Events)).Ask;
        Assert.Equal((AskQuestions.GameOver, "past the end", true), (asked.Question, asked.Prompt, asked.Sealed));
        warn.Append([new AskOpened(asked)]);
        Assert.Empty(warn.Engine.Advance(warn.State).Events);                 // waiting on the answers
        warn.Append([.. asked.Teams.Select(t => (EventPayload)new AskAnswered(asked.Id, t, new Chosen(GameOverChoices.Continue), false)),
            new AskClosed(asked.Id, asked.Teams.Select(t => new TeamAnswer(t, new Chosen(GameOverChoices.Continue))).ToList())]);
        var marked = warn.Engine.Advance(warn.State);
        Assert.Equal([Fifth], marked.Events);
        Assert.Equal("past the end", marked.Warning);
        warn.Append(marked.Events, warning: marked.Warning);
        Assert.Equal("past the end", warn.State.Applier.Log[^1].Warning);
        Assert.Empty(warn.Engine.Advance(warn.State).Events);                 // the act is in the log: no second warning
    }

    [Fact]
    public void Strict_ends_the_game_in_place_of_the_proposal_and_the_engine_falls_silent()
    {
        var sim = Judged(Enforcement.Strict);
        var emit = sim.Engine.Advance(sim.State);
        var end = Assert.IsType<GameEnded>(Assert.Single(emit.Events));
        Assert.Equal("past the end", end.Reason);
        Assert.Equal([Samples.Team1], end.Winners);
        Assert.Null(emit.Warning);
        sim.Append(emit.Events);
        Assert.Same(end, sim.State.Projection.Ended);
        Assert.Empty(sim.Engine.Advance(sim.State).Events);                  // still proposing round 5, but ended: nothing
        Assert.DoesNotContain(sim.State.Applier.Log, e => e.Payload is RoundStarted);
    }
}
