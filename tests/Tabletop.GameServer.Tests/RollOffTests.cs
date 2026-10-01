using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class RollOffTests
{
    static TestGames.Built Three() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
    /// <summary>The ranking step, which runs after the gate and carries the values (GfNames.RollOffOrderStep).</summary>
    static StepCompleted Places(Guid frame, params int[] ordinals) =>
        new(frame, GfNames.RollOffOrderStep, null, [], []) { Values = ordinals.Select((o, i) => ($"place_{i + 1}", o)).Append(("winner_ordinal", ordinals[0])).ToDictionary(x => x.Item1, x => x.Item2) };
    /// <summary>The rolling step, which carries dice and no values: the reader must not mistake it for the ranking (GfNames.RollOffRollStep).</summary>
    static StepCompleted Rolled(Guid frame) => new(frame, GfNames.RollOffRollStep, null, [], []);
    static SchedulerLog Log(TestGames.Built g) => SchedulerLog.Of(g.State);

    [Fact]
    public void Order_reads_place_values_as_team_ordinals_best_first_and_fills_in_teams_the_values_miss()
    {
        var g = Three(); var t = g.Teams; var frame = Guid.CreateVersion7();
        Assert.Equal([t[1], t[2], t[0]], RollOff.Order(g.State, Places(frame, 2, 3, 1)));
        Assert.Equal([t[2], t[0], t[1]], RollOff.Order(g.State, Places(frame, 3)));                      // a hand-edited log: the rest follow in ordinal order
        Assert.Equal([t[0], t[1], t[2]], RollOff.Order(g.State, null));                                  // no step at all: ordinal order
        Assert.Equal([t[1], t[0], t[2]], RollOff.Order(g.State, Places(frame, 2, 2, 9)));                // duplicates and unknown ordinals are skipped
    }

    [Fact]
    public void With_no_roll_off_since_the_round_anchor_the_named_sequence_is_started_with_no_actor()
    {
        var g = Three(); var starter = new ScriptedStarter();
        var emit = Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));
        var started = Assert.IsType<SequenceStarted>(Assert.Single(emit.Events));
        Assert.Equal((GfNames.RollOffSeq, (Guid?)null, (string?)null), (started.Sequence, started.Context.ActorUnitId, started.Context.Action));
        Assert.Equal([(GfNames.RollOffSeq, started.Context)], starter.Started);
    }

    [Fact]
    public void A_live_frame_waits_a_finished_one_resolves_and_an_abandoned_one_is_started_again()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));   // started and unfinished: the whole interval a confirm gate holds
        TestGames.Apply(g, Places(frame, 3, 1, 2), new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);
        TestGames.Apply(g, new RoundStarted(0, [t[2], t[0], t[1]], []));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // a new round anchor: the old roll-off does not count
        var again = Guid.CreateVersion7();
        TestGames.Apply(g, new SequenceStarted(again, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), new SequenceAbandoned(again));
        Assert.IsType<RollOff.Emit>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));      // abandoned from the menu: start over
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void A_gated_roll_off_waits_behind_its_confirm_and_resolves_from_the_ranking_step_when_the_frame_finishes()
    {
        var g = Three(); var t = g.Teams; var starter = new ScriptedStarter();
        var frame = Guid.CreateVersion7();
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Confirm", g.Teams, new Confirmation(), false, false, null, frame);
        TestGames.Apply(g, new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)), Rolled(frame), new AskOpened(gate));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // rolled, and the gate holds the frame with the dice editable
        TestGames.Apply(g, new AskClosed(gate.Id, [new TeamAnswer(t[0], new Confirmed())]), Places(frame, 3, 1, 2));
        Assert.IsType<RollOff.Waiting>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter));    // confirmed and ranked, but the frame has not finished: still nothing to read
        TestGames.Apply(g, new SequenceFinished(frame));
        Assert.Equal([t[2], t[0], t[1]], Assert.IsType<RollOff.Resolved>(RollOff.Advance(g.State, Log(g), GfNames.RollOffSeq, starter)).Order);   // the ranking step, not the rolling one
        Assert.Empty(starter.Started);                                                                    // waiting starts nothing, so no second roll-off is ever begun
    }
}
