using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>A MoveModels is legal only as deployment placement of the unit the actor's team just picked (in-game UX spec → Hand moves under enforcement).</summary>
public class HandMovesTests
{
    /// <summary>A: two units of two models; B: one unit of two. A wins the roll-off (6 against 1), so A's deploy Ask is open.</summary>
    static SchedulerHarness AtDeployment(Enforcement enforcement, Army? a = null, Army? b = null, IGameRules? rules = null)
    {
        var h = new SchedulerHarness([a ?? TestArmies.Build("A", 2, 2), b ?? TestArmies.Build("B", 1, 2)], enforcement: enforcement, rules: rules);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Run(); h.ConfirmRollOff();
        Assert.Equal((AskQuestions.Deploy, h.Teams[0]), (h.Open.Question, h.Open.Teams.Single()));
        return h;
    }

    static Actor TeamOf(SchedulerHarness h, int team) => new(Guid.CreateVersion7(), h.Teams[team]);

    static MoveModels Nudge(SchedulerHarness h, Guid unit) => new(h.P.Units[unit].Definition.Models.Select(m =>
    {
        var at = ((OnTable)h.P.Models[m.Id].Location).Placement;
        return new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 1 } }));
    }).ToList());

    [Fact]
    public void Strict_accepts_placing_the_picked_unit_from_the_shelf()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var seq = h.P.LastSeq;
        h.Deploy(a[0]);
        Assert.Equal("models_moved", h.Kinds(seq)[0]);
        Assert.Null(h.Log.Single(e => e.Seq == seq + 1).Warning);
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.IsType<OnTable>(h.P.Models[m.Id].Location));
    }

    [Fact]
    public void Strict_deploys_every_unit_to_round_one()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 2, 1)], enforcement: Enforcement.Strict);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.SkipDeployment();
        Assert.Equal(1, h.P.Scheduler.Round);
        Assert.All(h.P.Models.Values, m => Assert.IsType<OnTable>(m.Location));
    }

    [Fact]
    public void Strict_places_Scouts_after_deployment()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 2, 1, unitRules: [(1, Scout, null)])],
            enforcement: Enforcement.Strict);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);                                                   // B wins
        h.Run(); h.ConfirmRollOff();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true)); h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), SetAside, true));
        h.Answer(1, new SelectedPieces([b[1]])); h.Deploy(b[1]);
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        h.Answer(1, new SelectedPieces([b[0]])); h.Deploy(b[0]);
        h.Answer(0, new SelectedPieces([a[1]])); h.Deploy(a[1]);
        Assert.Equal(1, h.P.Scheduler.Round);
    }

    [Fact]
    public void Strict_places_an_Ambusher_at_the_round_boundary()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)], enforcement: Enforcement.Strict);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.EndRound();
        Assert.Equal(AskQuestions.DeployAmbusher, h.Open.Question);
        h.Answer(0, new SelectedPieces([a[1]]));
        h.Dispatch(TestGames.Move(h.State, a[1]), team: 0);
        Assert.DoesNotContain(SetAside, h.P.Units[a[1]].Conditions);                               // arrived
    }

    [Fact]
    public void A_picked_unit_may_come_off_the_shelf_one_model_at_a_time()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var all = TestGames.Relocations(h.State, a[0]);
        Assert.Null(HandMoves.Judge(h.State, new MoveModels([all[0]]), TeamOf(h, 0)));
        h.Dispatch(new MoveModels([all[0]]), team: 0);
        Assert.Null(HandMoves.Judge(h.State, new MoveModels([all[1]]), TeamOf(h, 0)));
        h.Dispatch(new MoveModels([all[1]]), team: 0);
        Assert.Equal(h.Teams[1], h.Open.Teams.Single());                                     // the activation completed: B's deploy Ask
    }

    [Fact]
    public void Placing_a_unit_that_was_not_picked_is_a_breach()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var breach = HandMoves.Judge(h.State, TestGames.Move(h.State, a[1]), TeamOf(h, 0));
        Assert.Equal(new Breach(HandMoves.Rule, $"{h.P.Units[a[1]].Definition.Name} moved by hand outside a move step", new StrictRefuses(RefusalReason.Illegal)), breach);
    }

    [Fact]
    public void Before_the_deploy_answer_nothing_is_picked()
    {
        var h = AtDeployment(Enforcement.Strict);
        Assert.NotNull(HandMoves.Judge(h.State, TestGames.Move(h.State, h.Units(0)[0]), TeamOf(h, 0)));
    }

    [Fact]
    public void Another_team_or_a_watcher_may_not_place_the_picked_unit()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        Assert.NotNull(HandMoves.Judge(h.State, TestGames.Move(h.State, a[0]), TeamOf(h, 1)));
        Assert.NotNull(HandMoves.Judge(h.State, TestGames.Move(h.State, a[0]), new Actor(Guid.CreateVersion7(), null)));
    }

    /// <summary>
    /// A: unit 1 plain, unit 2 with Transport(11) on its models, unit 3 plain; B: two units. A wins the roll-off and deploys
    /// <paramref name="first"/>, B deploys its first unit, then A picks unit 1. Returns A's units.
    /// </summary>
    static (SchedulerHarness H, IReadOnlyList<Guid> A) PickingAfter(int first)
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 3, 2, modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 2, 1));
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[first]])); h.Deploy(a[first]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        h.Answer(0, new SelectedPieces([a[0]]));
        return (h, a);
    }

    static MoveModels Embark(SchedulerHarness h, Guid unit, Guid container) =>
        new(h.P.Units[unit].Definition.Models.Select(m => new ModelRelocation(m.Id, new OnShelf(), new Inside(container))).ToList());

    [Fact]
    public void Placing_the_picked_unit_inside_a_Transport_deployed_earlier_is_a_breach()
    {
        var (h, a) = PickingAfter(1);
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;                              // p.15: "units inside Transports are deployed at the same time as the Transport"
        Assert.IsType<OnTable>(h.P.Models[transport].Location);
        var breach = HandMoves.Judge(h.State, Embark(h, a[0], transport), TeamOf(h, 0));
        Assert.Equal(new Breach(HandMoves.Rule, $"{h.P.Units[a[0]].Definition.Name} deployed inside a container the rules do not allow", new StrictRefuses(RefusalReason.Illegal)), breach);
        Assert.Throws<InvalidOperationException>(() => h.Dispatch(Embark(h, a[0], transport), team: 0));   // strict refuses
    }

    [Fact]
    public void The_picked_Transport_deploys_with_a_unit_inside_in_one_placement()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 3, 2, modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 2, 1));
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[1]]));                                              // p.15: "Transports may deploy with units inside"
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        var together = new MoveModels([.. TestGames.Relocations(h.State, a[1]), .. Embark(h, a[0], transport).Moves]);
        Assert.Null(HandMoves.Judge(h.State, together, TeamOf(h, 0)));
        h.Dispatch(together, team: 0);
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.Equal(new Inside(transport), h.P.Models[m.Id].Location));
        Assert.Equal(h.Teams[1], h.Open.Teams.Single());
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal([a[2]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);                // the unit inside deployed with its Transport
    }

    [Fact]
    public void A_reserved_Ambusher_may_not_ride_a_Transport_into_the_ordinary_deployment()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 3, 2, unitRules: [(3, Ambush, null)], modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 2, 1));
        var a = h.Units(0);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[2]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[1]]));
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        Assert.NotNull(HandMoves.Judge(h.State, Embark(h, a[2], transport), TeamOf(h, 0)));
        Assert.Null(HandMoves.Judge(h.State, Embark(h, a[0], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void A_unit_that_was_on_the_table_may_not_board_from_the_shelf()
    {
        var h = AtDeployment(Enforcement.Warn, TestArmies.Build("A", 3, 2, modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 2, 1));
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Dispatch(new MoveModels(h.P.Units[a[0]].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);   // removed, as casualties are
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        h.Answer(0, new SelectedPieces([a[1]]));
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        Assert.All(h.P.Units[a[0]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        Assert.NotNull(HandMoves.Judge(h.State, Embark(h, a[0], transport), TeamOf(h, 0)));
        Assert.Null(HandMoves.Judge(h.State, Embark(h, a[2], transport), TeamOf(h, 0)));
    }

    [Fact]
    public void A_set_aside_unit_rides_a_Scout_Transport_and_both_stop_being_set_aside()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 3, 2, unitRules: [(2, Scout, null)], modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 1, 1));
        var a = h.Units(0);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true)); h.Dispatch(new SetCondition(TestGames.UnitSubject(a[2]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(AskQuestions.DeployScouts, h.Open.Question);
        h.Answer(0, new SelectedPieces([a[1]]));
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        h.Dispatch(new MoveModels([.. Embark(h, a[2], transport).Moves, .. TestGames.Relocations(h.State, a[1])]), team: 0);
        Assert.Equal(1, h.P.Scheduler.Round);
        Assert.DoesNotContain(SetAside, h.P.Units[a[1]].Conditions);
        Assert.DoesNotContain(SetAside, h.P.Units[a[2]].Conditions);
    }

    [Fact]
    public void A_unit_may_board_the_picked_Transport_before_the_Transport_is_placed()
    {
        var h = AtDeployment(Enforcement.Strict, TestArmies.Build("A", 3, 2, modelRules: [(2, Transport, 11)]), TestArmies.Build("B", 2, 1));
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[1]]));
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        Assert.IsType<OnShelf>(h.P.Models[transport].Location);
        Assert.Null(HandMoves.Judge(h.State, Embark(h, a[0], transport), TeamOf(h, 0)));       // the Transport's deployment is under way
        h.Dispatch(Embark(h, a[0], transport), team: 0);
        Assert.Empty(h.P.OpenAsks);                                                            // still A's deploy: the Transport is on the shelf
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Null(HandMoves.Judge(h.State, TestGames.Move(h.State, a[1]), TeamOf(h, 0)));
    }

    [Fact]
    public void Placing_the_picked_unit_inside_a_friendly_model_without_Transport_is_a_breach()
    {
        var (h, a) = PickingAfter(2);
        var plain = h.P.Units[a[2]].Definition.Models[0].Id;
        Assert.IsType<OnTable>(h.P.Models[plain].Location);
        Assert.NotNull(HandMoves.Judge(h.State, Embark(h, a[0], plain), TeamOf(h, 0)));
    }

    [Fact]
    public void The_picked_unit_may_go_inside_a_Transport_still_on_the_shelf_and_deploys_with_it()
    {
        var (h, a) = PickingAfter(2);
        var transport = h.P.Units[a[1]].Definition.Models[0].Id;
        Assert.IsType<OnShelf>(h.P.Models[transport].Location);
        Assert.Null(HandMoves.Judge(h.State, Embark(h, a[0], transport), TeamOf(h, 0)));       // passengers spec: a Transport not yet deployed takes it
    }

    [Fact]
    public void A_wrapping_ruleset_forwards_MayDeployInside()
    {
        var (h, a) = PickingAfter(1);
        var wrapped = new WithSequences(GrimdarkFutureRules.Instance);
        Assert.True(wrapped.MayDeployInside(h.State, a[0], h.P.Units[a[1]].Definition.Models[0].Id, new HashSet<Guid> { a[1] }));
        Assert.False(wrapped.MayDeployInside(h.State, a[0], h.P.Units[a[1]].Definition.Models[0].Id, new HashSet<Guid> { a[0] }));
        Assert.False(wrapped.MayDeployInside(h.State, a[0], h.P.Units[a[2]].Definition.Models[0].Id, new HashSet<Guid> { a[2] }));
    }

    [Fact]
    public void Placing_inside_an_enemy_or_unknown_container_or_the_units_own_model_is_a_breach()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var models = h.P.Units[a[0]].Definition.Models;
        MoveModels Into(Guid container) => new([new ModelRelocation(models[0].Id, new OnShelf(), new Inside(container))]);
        Assert.NotNull(HandMoves.Judge(h.State, Into(h.P.Units[h.Units(1)[0]].Definition.Models[0].Id), TeamOf(h, 0)));
        Assert.NotNull(HandMoves.Judge(h.State, Into(Guid.CreateVersion7()), TeamOf(h, 0)));
        Assert.NotNull(HandMoves.Judge(h.State, Into(models[1].Id), TeamOf(h, 0)));
    }

    [Fact]
    public void Only_shelf_to_table_or_container_is_placement()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var first = h.P.Units[a[0]].Definition.Models[0].Id;
        var back = new MoveModels([new ModelRelocation(first, new OnShelf(), new OnShelf())]);
        Assert.NotNull(HandMoves.Judge(h.State, back, TeamOf(h, 0)));
    }

    [Fact]
    public void A_placed_model_moved_again_is_a_breach()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var all = TestGames.Relocations(h.State, a[0]);
        h.Dispatch(new MoveModels([all[0]]), team: 0);                                       // one placed, one still on the shelf: the pick stands
        var again = new MoveModels([new ModelRelocation(all[0].ModelId, all[0].To, new OnTable(new Placement(new Point3(20, 6, 0), new Yaw(0))))]);
        Assert.NotNull(HandMoves.Judge(h.State, again, TeamOf(h, 0)));
    }

    [Fact]
    public void A_later_Ask_ends_the_pick()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 2), TestArmies.Build("B", 1, 1)], enforcement: Enforcement.Silent);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.SkipDeployment();
        var a0 = h.Units(0)[0];
        h.Dispatch(new MoveModels(h.P.Units[a0].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);
        Assert.NotNull(HandMoves.Judge(h.State, TestGames.Move(h.State, a0), TeamOf(h, 0)));   // round 1's pick is long gone: returning from the shelf is a breach
    }

    [Theory]
    [InlineData(Enforcement.Silent)] [InlineData(Enforcement.Warn)] [InlineData(Enforcement.Strict)]
    public void A_hand_move_during_play_takes_the_settings_three_positions(Enforcement enforcement)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], enforcement: enforcement);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.SkipDeployment();
        var a0 = h.Units(0)[0]; var seq = h.P.LastSeq;
        var detail = $"{h.P.Units[a0].Definition.Name} moved by hand outside a move step";
        if (enforcement == Enforcement.Strict)
        {
            var refused = Assert.Throws<InvalidOperationException>(() => h.Dispatch(Nudge(h, a0), team: 0));
            Assert.Equal($"{RefusalReason.Illegal}: {detail}", refused.Message);
            Assert.Equal(seq, h.P.LastSeq);
            return;
        }
        h.Dispatch(Nudge(h, a0), team: 0);
        var moved = h.Log.Single(e => e.Seq == seq + 1);
        Assert.IsType<ModelsMoved>(moved.Payload);
        Assert.Equal(enforcement == Enforcement.Warn ? detail : null, moved.Warning);
    }

    [Fact]
    public void Strict_refuses_placing_the_picked_unit_on_its_own_model()
    {
        var h = AtDeployment(Enforcement.Strict, rules: GrimdarkFutureRules.Instance);   // FakeRules leaves PlacementBreach at null
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var all = TestGames.Relocations(h.State, a[0]);
        Assert.Equal(MoveOverlapRule, HandMoves.Judge(h.State, new MoveModels([all[0], all[1] with { To = all[0].To }]), TeamOf(h, 0))!.Rule);
    }
}
