using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Four teams of two players, an army each (four-team games spec). Four sides is rulebook p.50's free-for-all, "3-4 players", with each side a
/// team. Free-for-all: "All players roll-off to determine the order in which they will deploy. Then the players alternate in picking a table
/// corner and deploying one unit each within a 2'x1' area, starting with the player that got the highest score, and then alternating in
/// descending order." "During the first round, turn order goes from the player that deployed first to the one that deployed last. Starting from
/// the second round, turn order goes from the player that finished activating first to the one that finished last." Team Games: "players from
/// the same team must deploy their units in the same deployment zone, placing a single unit when it's their team's turn to deploy"; "for each
/// team's turn only one of the players may activate a single unit"; "when checking who won, all objective markers and deployment zones are
/// shared between players of the same team".
/// </summary>
public class FourTeamGameTests
{
    /// <summary>Eight seats, seat i on team i % 4; each seat's army has `units[i % 4]` units of one model.</summary>
    static SchedulerHarness FourTeams(params int[] units) =>
        new(Enumerable.Range(0, 8).Select(i => (Army?)TestArmies.Build($"P{i}", units.Length == 0 ? 1 : units[i % 4], 1)).ToList(), teamOf: [0, 1, 2, 3, 0, 1, 2, 3]);

    static void ActivateFirst(SchedulerHarness h)
    {
        var option = ((Options)h.Open.Legal).Choices[0];
        h.AnswerAs(h.OwnerOf(option.Pieces[0]), new Chosen(option.Id));
    }

    [Fact]
    public void All_four_teams_roll_off_and_teams_still_tied_roll_again_among_themselves_for_the_whole_order()
    {
        var h = FourTeams();
        foreach (var face in new[] { 5, 5, 2, 5, 3, 6, 1 }) h.Dice.Enqueue(face);       // teams 1, 2 and 4 tie on 5 and roll again: 3, 6, 1
        h.Run(); h.ConfirmRollOff();
        var steps = h.Log.Select(e => e.Payload).OfType<StepCompleted>().ToList();
        var rolls = steps.Single(s => s.Step == GfNames.RollOffRollStep).Rolls;
        Assert.Equal([4, 3], rolls.Select(r => r.Dice.Count));                           // one die a team, then one for each team still tied
        var t = h.Teams;
        Assert.Equal([t[1], t[0], t[3], t[2]], h.P.Scheduler.Order);                     // 5+6, 5+3, 5+1, 2: "in descending order"
        var ranking = steps.Single(s => s.Step == GfNames.RollOffOrderStep).Values!;
        Assert.Equal((2, 1, 4, 3), (ranking["place_1"], ranking["place_2"], ranking["place_3"], ranking["place_4"]));
        Assert.Equal(t[1], h.P.Scheduler.ActiveTeamId);                                  // "starting with the player that got the highest score"
    }

    [Fact]
    public void Deployment_goes_round_the_four_teams_in_roll_off_order_one_unit_a_turn_from_either_army_and_round_one_follows_it()
    {
        var h = FourTeams();
        foreach (var face in new[] { 3, 6, 1, 4 }) h.Dice.Enqueue(face);
        h.Run(); h.ConfirmRollOff();
        var t = h.Teams;
        IReadOnlyList<Guid> order = [t[1], t[3], t[0], t[2]];
        for (var turn = 0; turn < 8; turn++)
        {
            var team = h.P.Scheduler.ActiveTeamId!.Value;
            Assert.Equal(order[turn % 4], team);
            var ask = h.Open;
            Assert.Equal((AskQuestions.Deploy, team), (ask.Question, ask.Teams.Single()));
            var eligible = ((PieceSelection)ask.Legal).Eligible;
            Assert.Equal(turn < 4 ? 2 : 1, eligible.Count);                              // both players' units are the team's to place
            var unit = eligible[^1];
            var other = h.Participants.First(p => p != h.OwnerOf(unit) && h.State.Info.Participants.Single(x => x.Id == p).TeamId == team);
            h.AnswerAs(other, new SelectedPieces([unit]));                              // the teammate picks for the other player
            h.Deploy(unit);
        }
        Assert.Equal(1, h.P.Scheduler.Round);
        Assert.Equal(order, h.P.Scheduler.Order);                                        // "from the player that deployed first to the one that deployed last"
        Assert.Equal(t[1], h.P.Scheduler.ActiveTeamId);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Turns_go_round_the_four_teams_and_round_two_starts_with_the_team_that_finished_activating_first()
    {
        var h = FourTeams(1, 2, 1, 1);                                                   // team 2 brings four units, the others two
        foreach (var face in new[] { 3, 6, 1, 4 }) h.Dice.Enqueue(face);
        h.SkipDeployment();
        var t = h.Teams;
        var turns = new List<Guid>();
        while (h.Open.Question != AskQuestions.EndRound)
        {
            turns.Add(h.P.Scheduler.ActiveTeamId!.Value);
            ActivateFirst(h);
        }
        Assert.Equal([t[1], t[3], t[0], t[2], t[1], t[3], t[0], t[2], t[1], t[1]], turns);   // one unit a team's turn, round the order; team 2 alone at the end
        h.EndRound();
        var round2 = h.Log.Select(e => e.Payload).OfType<RoundStarted>().Single(r => r.Round == 2);
        Assert.Equal([t[3], t[0], t[2], t[1]], round2.Order);                            // finished at turns 6, 7, 8 and 10
        Assert.Equal(t[3], h.P.Scheduler.ActiveTeamId);
    }

    static void Put(TestGames.Built g, Guid unit, double x, double y) => TestGames.Apply(g, new ModelsMoved(g.State.Projection.Units[unit].Definition.Models.Select(m =>
        new ModelRelocation(m.Id, g.State.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))).ToList()));
    static TestGames.Built FourTeamState() => TestGames.Build(Enumerable.Range(0, 8).Select(i => (Army?)TestArmies.Build($"P{i}", 1, 2)).ToList(), teamOf: [0, 1, 2, 3, 0, 1, 2, 3]);
    static Guid UnitOfSeat(TestGames.Built g, int seat) => g.State.Projection.Units.Values.Single(u => u.ParticipantId == g.Participants[seat]).Id;

    [Fact]
    public void A_marker_goes_to_the_one_team_of_four_within_three_inches_and_any_second_team_contests_it()
    {
        var g = FourTeamState();
        var objective = g.State.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
        Put(g, UnitOfSeat(g, 6), 36, 28);                                                // team 3's second player
        Assert.Equal([new ObjectiveControlSet(objective, g.Teams[2])], GfObjectives.Seize(g.State, TestProperties.Core));
        TestGames.Apply(g, GfObjectives.Seize(g.State, TestProperties.Core).ToArray());
        Put(g, UnitOfSeat(g, 3), 36, 20);                                                // team 4 arrives: "if units from both sides contest a marker"
        Assert.Equal([new ObjectiveControlSet(objective, null)], GfObjectives.Seize(g.State, TestProperties.Core));
    }

    [Fact]
    public void The_team_of_four_with_most_markers_wins_and_an_equal_count_ties_only_those_teams()
    {
        var g = FourTeamState();
        var markers = Enumerable.Range(0, 3).Select(i => new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(10 + i * 10, 10, 0), new Yaw(0), 2, $"M{i}", VisualRef.OfBuiltin("objective-ring"))).ToList();
        TestGames.Apply(g, markers.Select(m => (EventPayload)new MarkerPlaced(m)).ToArray());
        var t = g.Teams;
        TestGames.Apply(g, new ObjectiveControlSet(markers[0].Id, t[3]), new ObjectiveControlSet(markers[1].Id, t[3]), new ObjectiveControlSet(markers[2].Id, t[1]));
        Assert.Equal([t[3]], GfObjectives.Winners(g.State));                             // "the player that controls most markers wins"
        Assert.Equal("Markers held: team 1: 0, team 2: 1, team 3: 0, team 4: 2.", GfObjectives.Standing(g.State));
        var setup = g.State.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
        TestGames.Apply(g, new ObjectiveControlSet(setup, t[1]));
        Assert.Equal([t[1], t[3]], GfObjectives.Winners(g.State));                       // two of the four tie; teams 1 and 3 hold none
    }

    [Fact]
    public void Units_of_teams_three_and_four_at_half_strength_take_the_end_of_activation_morale_test()
    {
        var g = FourTeamState();
        var (third, fourth, first) = (UnitOfSeat(g, 2), UnitOfSeat(g, 7), UnitOfSeat(g, 0));
        foreach (var unit in new[] { third, fourth, first }) Put(g, unit, 20, 20);
        foreach (var unit in new[] { third, fourth })
        {
            var lost = g.State.Projection.Units[unit].Definition.Models[0].Id;
            TestGames.Apply(g, new ModelsMoved([new ModelRelocation(lost, g.State.Projection.Models[lost].Location, new OnShelf())]), new WoundsThisActivationSet(unit, 1));
        }
        // p.10: "At the end of an activation in which a unit takes wounds that leave it with half or less of its starting size … it must take a
        // morale test", whichever of the four teams holds it.
        Assert.Equal(new[] { third, fourth }.Order(), GfHooks.UnitsToTestAtEndOfActivation(g.State, TestProperties.Core, first).Order());
    }
}
