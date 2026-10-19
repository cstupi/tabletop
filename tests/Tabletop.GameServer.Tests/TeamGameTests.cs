using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Rulebook p.50 Team Games: "the turn order is on a team basis and not on a player basis, so for each team's turn only one of the players may
/// activate a single unit"; "players from the same team must deploy their units in the same deployment zone, placing a single unit when it's
/// their team's turn to deploy". Owner ruling: "Either player from the team can take the activation across any unit in their team." Teams
/// alternate; any seat of the team answers any of the team's Asks, for any unit of the team.
/// </summary>
public class TeamGameTests
{
    /// <summary>Team 1: Ann (A1) and Cid (C1); Team 2: Bob (B1) and Dee (D1). One unit of two models each.</summary>
    static SchedulerHarness TwoAside(Enforcement enforcement = Enforcement.Warn) =>
        new([TestArmies.Build("Ann", 1, 2), TestArmies.Build("Bob", 1, 2), TestArmies.Build("Cid", 1, 2), TestArmies.Build("Dee", 1, 2)],
            enforcement: enforcement, teamOf: [0, 1, 0, 1]);

    static Guid UnitOf(SchedulerHarness h, int seat) => h.P.Units.Values.Single(u => u.ParticipantId == h.Participants[seat]).Id;

    [Fact]
    public void Deployment_alternates_by_team_and_either_player_deploys_any_unit_of_the_team()
    {
        var h = TwoAside(); h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        var (ann, bob, cid, dee) = (h.Participants[0], h.Participants[1], h.Participants[2], h.Participants[3]);
        var deploy = h.OpenAsk(0)!;
        Assert.Equal(new[] { UnitOf(h, 0), UnitOf(h, 2) }.Order(), ((PieceSelection)deploy.Legal).Eligible.Order());     // both seats' units are offered
        h.AnswerAs(cid, new SelectedPieces([UnitOf(h, 0)]));                                          // Cid deploys Ann's unit
        h.Deploy(UnitOf(h, 0));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                        // team 2 deploys next
        h.AnswerAs(bob, new SelectedPieces([UnitOf(h, 3)])); h.Deploy(UnitOf(h, 3));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal([UnitOf(h, 2)], ((PieceSelection)h.OpenAsk(0)!.Legal).Eligible);
        h.AnswerAs(ann, new SelectedPieces([UnitOf(h, 2)])); h.Deploy(UnitOf(h, 2));
        h.AnswerAs(dee, new SelectedPieces([UnitOf(h, 1)])); h.Deploy(UnitOf(h, 1));
        Assert.Equal(1, h.P.Scheduler.Round);
    }

    [Fact]
    public void A_teammate_may_place_the_teams_pick_under_strict()
    {
        var h = TwoAside(Enforcement.Strict);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        h.AnswerAs(h.Participants[0], new SelectedPieces([UnitOf(h, 0)]));
        var (outcome, _) = h.As(h.Participants[2], TestGames.Move(h.State, UnitOf(h, 0)));                 // Cid places Ann's pick
        Assert.IsType<Dispatch.Applied>(outcome);
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
    }

    [Fact]
    public void Either_player_of_the_team_activates_any_unit_of_the_team_and_the_other_team_is_refused()
    {
        var h = TwoAside(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var (ann, bob, cid, dee) = (h.Participants[0], h.Participants[1], h.Participants[2], h.Participants[3]);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var activate = h.OpenAsk(0)!;
        var refused = Assert.IsType<Refused>(h.As(bob, new AnswerAsk(activate.Id, new Chosen(TestGames.Activate(UnitOf(h, 0))))).Outcome);
        Assert.Equal(RefusalReason.NotPermitted, refused.Reason);                                    // Bob is on the other team
        h.AnswerAs(cid, new Chosen(TestGames.Activate(UnitOf(h, 0))));                                // Cid activates Ann's unit
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.AnswerAs(bob, new Chosen(TestGames.Activate(UnitOf(h, 3))));                                // Bob activates Dee's unit
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.AnswerAs(cid, new Chosen(TestGames.Activate(UnitOf(h, 2))));                                // Cid again, his own
        h.AnswerAs(dee, new Chosen(TestGames.Activate(UnitOf(h, 1))));
        var activations = h.Log.Where(e => e.Payload is AskAnswered { Answer: Chosen }).Select(e => e.Actor).ToList();
        Assert.Equal([cid, bob, cid, dee], activations);
    }

    /// <summary>A defender's Ask is the defending team's: Bob removes his teammate Dee's casualties; the attacking team may not.</summary>
    [Fact]
    public void Casualties_are_allocated_by_any_player_of_the_team_hit()
    {
        var attacker = Unit("A", "4+", "4+", [], 1, []);
        var bobs = Unit("B", "4+", "4+", [], 2, []);
        var dees = Unit("D", "4+", "4+", [], 2, []);
        var state = State([attacker], [bobs]);
        var dee = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Dee", Samples.Team2, null);
        state.Info = state.Info with { Participants = [.. state.Info.Participants, dee] };
        var board = state.Board with { Armies = [.. state.Board.Armies, new ArmyInPlay(dee.Id, new ArmySnapshot(Guid.CreateVersion7(), "Three", "grimdark-future", [dees]))] };
        var applier = new EventApplier(board, state.Info);
        applier.ApplyAll(state.Applier.Log);
        applier.Apply(new GameEvent(applier.Log.Count + 1, null, new ModelsMoved(dees.Models.Select((m, i) => new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(14, i * 1.5, 0), new Yaw(0))))).ToList()), null));
        state = new GameState(state.Info, board, applier, state.Rules);
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds")), TestProperties.Core, new Dice.ScriptedDice([1]));
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [dees.Id], [], GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = 1 } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [dees.Id], [], GfNames.Hold));
        var ask = sim.AskFor(Samples.Team2);
        var refused = Assert.IsType<Refused>(sim.Dispatch(new AnswerAsk(ask.Id, new Allocated(new Dictionary<Guid, int> { [dees.Models[0].Id] = 1 })), Samples.P1, Samples.Team1));
        Assert.Equal(RefusalReason.NotPermitted, refused.Reason);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dees.Models[0].Id] = 1 }), P2);
        Assert.Equal(1, sim.Alive(dees.Id));
    }
}
