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
/// their team's turn to deploy". Teams alternate as before; inside a team each seat activates, deploys and answers for its own army.
/// </summary>
public class TeamGameTests
{
    /// <summary>Team 1: Ann (A1) and Cid (C1); Team 2: Bob (B1) and Dee (D1). One unit of two models each.</summary>
    static SchedulerHarness TwoAside() => new([TestArmies.Build("Ann", 1, 2), TestArmies.Build("Bob", 1, 2), TestArmies.Build("Cid", 1, 2), TestArmies.Build("Dee", 1, 2)],
        teamOf: [0, 1, 0, 1]);

    static Guid UnitOf(SchedulerHarness h, int seat) => h.P.Units.Values.Single(u => u.ParticipantId == h.Participants[seat]).Id;

    [Fact]
    public void Deployment_alternates_by_team_and_a_seat_deploys_only_its_own_units()
    {
        var h = TwoAside(); h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        var (ann, cid) = (h.Participants[0], h.Participants[2]);
        var deploy = h.OpenAsk(0)!;
        Assert.True(deploy.OwnUnitsOnly);
        Assert.Equal(new[] { UnitOf(h, 0), UnitOf(h, 2) }.Order(), ((PieceSelection)deploy.Legal).Eligible.Order());     // both seats' units are offered
        var refused = Assert.IsType<Refused>(h.As(cid, new AnswerAsk(deploy.Id, new SelectedPieces([UnitOf(h, 0)]))).Outcome);
        Assert.Equal(RefusalReason.NotPermitted, refused.Reason);
        Assert.Contains("Player 1's unit", refused.Detail);   // seat names are the harness's: Ann is Player 1
        h.AnswerAs(cid, new SelectedPieces([UnitOf(h, 2)]));
        h.Deploy(UnitOf(h, 2));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                        // team 2 deploys next, not Ann
        h.AnswerAs(h.Participants[3], new SelectedPieces([UnitOf(h, 3)])); h.Deploy(UnitOf(h, 3));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal([UnitOf(h, 0)], ((PieceSelection)h.OpenAsk(0)!.Legal).Eligible);
        h.AnswerAs(ann, new SelectedPieces([UnitOf(h, 0)])); h.Deploy(UnitOf(h, 0));
        h.AnswerAs(h.Participants[1], new SelectedPieces([UnitOf(h, 1)])); h.Deploy(UnitOf(h, 1));
        Assert.Equal(1, h.P.Scheduler.Round);
    }

    [Fact]
    public void A_teammate_may_not_place_another_seats_pick_under_strict()
    {
        var h = new SchedulerHarness([TestArmies.Build("Ann", 1, 2), TestArmies.Build("Bob", 1, 2), TestArmies.Build("Cid", 1, 2), TestArmies.Build("Dee", 1, 2)],
            enforcement: Enforcement.Strict, teamOf: [0, 1, 0, 1]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        h.AnswerAs(h.Participants[0], new SelectedPieces([UnitOf(h, 0)]));
        var refused = Assert.IsType<Refused>(h.As(h.Participants[2], TestGames.Move(h.State, UnitOf(h, 0))).Outcome);
        Assert.Contains("another player's unit", refused.Detail);
        h.Deploy(UnitOf(h, 0));                                                                       // Ann places her own
    }

    [Fact]
    public void Each_teams_turn_one_of_its_players_activates_one_of_their_own_units()
    {
        var h = TwoAside(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var (ann, bob, cid, dee) = (h.Participants[0], h.Participants[1], h.Participants[2], h.Participants[3]);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var activate = h.OpenAsk(0)!;
        Assert.True(activate.OwnUnitsOnly);
        var refused = Assert.IsType<Refused>(h.As(ann, new AnswerAsk(activate.Id, new Chosen(TestGames.Activate(UnitOf(h, 2))))).Outcome);
        Assert.Equal(RefusalReason.NotPermitted, refused.Reason);
        Assert.Contains("Player 3's unit", refused.Detail);   // Cid is Player 3
        h.AnswerAs(cid, new Chosen(TestGames.Activate(UnitOf(h, 2))));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                        // then team 2, whoever of it goes
        h.AnswerAs(dee, new Chosen(TestGames.Activate(UnitOf(h, 3))));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.AnswerAs(ann, new Chosen(TestGames.Activate(UnitOf(h, 0))));
        h.AnswerAs(bob, new Chosen(TestGames.Activate(UnitOf(h, 1))));
        var activations = h.Log.Where(e => e.Payload is AskAnswered { Answer: Chosen }).Select(e => e.Actor).ToList();
        Assert.Equal([cid, dee, ann, bob], activations);
    }

    [Fact]
    public void One_account_holding_two_seats_of_a_team_may_pick_either_seats_unit()
    {
        var h = TwoAside();
        var ann = h.State.Info.Participants[0];
        h.State.Info = h.State.Info with { Participants = [.. h.State.Info.Participants.Select(p => p.Id == h.Participants[2] ? p with { AccountId = ann.AccountId } : p)] };
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        h.AnswerAs(ann.Id, new Chosen(TestGames.Activate(UnitOf(h, 2))));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
    }

    /// <summary>A defender's Ask goes to the seat whose unit is hit: Dee removes Dee's casualties, not her teammate Bob.</summary>
    [Fact]
    public void Casualties_are_allocated_by_the_owner_of_the_unit_hit()
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
        Assert.Equal(dee.Id, ask.Seat);
        var refused = Assert.IsType<Refused>(sim.Dispatch(new AnswerAsk(ask.Id, new Allocated(new Dictionary<Guid, int> { [dees.Models[0].Id] = 1 })), P2, Samples.Team2));
        Assert.Contains("Dee's", refused.Detail);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dees.Models[0].Id] = 1 }), dee.Id);
        Assert.Equal(1, sim.Alive(dees.Id));
    }
}
