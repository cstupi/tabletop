using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ViewModelTests
{
    static readonly GameInfo Game = Samples.Game;
    static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Samples.Board, Game); a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null))); return a; }
    static readonly Ask Activate = new(Samples.AskId, AskQuestions.Activate, "Activate a unit", [Samples.Team1],
        new Options([new AskOption($"{Samples.Unit}:Advance", "Advance", [Samples.Unit]), new AskOption($"{Samples.Unit}:Hold", "Hold", [Samples.Unit])]), true, false, null, null);
    /// <summary>The other team's spender: a model of no unit this projection holds, so it resolves to no team and Names.Piece degrades it to its first eight characters. A fixed id keeps the words stable.</summary>
    static readonly Guid Spender = Guid.Parse("0198b7ac-1111-7000-8000-000000000001");
    /// <summary>The simultaneous spend (p.13): one maximum per eligible model, over both teams' models, sealed and passable. Team 1 may spend only its own.</summary>
    static readonly Ask SpendTokens = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend spell tokens", [Samples.Team1, Samples.Team2],
        new TokenSpend(new Dictionary<Guid, int> { [Samples.Model] = 2, [Spender] = 1 }), true, true, "casting", Samples.Frame);
    /// <summary>A Takedown group picking the model it targets (p.14): one model of the target unit, min and max both 1, addressed to the attacker's team (interpreter plan, question `takedown_target`).</summary>
    static readonly Ask TakedownAsk = new(Guid.CreateVersion7(), "takedown_target", "Pick the model this Takedown group targets", [Samples.Team1], new PieceSelection([Samples.Model], 1, 1), false, false, null, Samples.Frame);
    /// <summary>The scheduler's round boundary: unframed, unsealed, no pass, every team that has a participant.</summary>
    static readonly Ask EndRound = new(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Samples.Team1, Samples.Team2], new Confirmation(), false, false, null, null);

    [Fact]
    public void Log_lines_name_actor_and_subject()
    {
        var a = Applier(new ModelsMoved([new ModelRelocation(Samples.Model, new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnTable(new Placement(new Point3(3, 4, 0), new Yaw(0))))]),
            new ScoreSet(Samples.Team1, 4), new TurnEnded(Samples.Team1, true, []), new Undone(1),
            new AskOpened(EndRound), new AskAnswered(EndRound.Id, Samples.Team1, new Confirmed(), false), new RoundStarted(3, [Samples.Team1, Samples.Team2], []),
            new ModelsMoved([Samples.Embark]), new ModelsMoved([Samples.Disembark]));
        var names = new Names(Game, a.Projection);
        var lines = a.Log.Select(e => LogText.Describe(e, names)).ToList();
        Assert.Equal("Clay moved Battle Brothers 5.0\"", lines[0]);
        Assert.Equal("Clay set Team 1's score to 4", lines[1]);
        Assert.Equal("Clay passed for Team 1", lines[2]);
        Assert.Equal("Clay undid back to #1", lines[3]);
        Assert.Equal("Clay asked Team 1, Team 2: End the round", lines[4]);
        Assert.Equal("Clay confirmed for Team 1", lines[5]);      // the end_round answer: a player ended the round (scheduler plan)
        Assert.Equal("Clay started round 3", lines[6]);           // and the engine's own line under it says which round began (actor null in the real log: "the game started round 3")
        // Containment names the container: Names.Piece resolves a container that is a model, and degrades to its first eight characters otherwise.
        Assert.Equal($"Clay put Battle Brothers inside {Names.Short(Samples.Container)}", lines[7]);
        Assert.Equal($"Clay took Battle Brothers out of {Names.Short(Samples.Container)}", lines[8]);
        // p.15 embarks the whole unit when one model reaches the transport, so the answer is one relocation per model — and still one line.
        Assert.Equal(lines[7], LogText.Describe(new GameEvent(99, Samples.P1,
            new ModelsMoved([Samples.Embark, Samples.Embark with { ModelId = Guid.CreateVersion7() }]), null), names));
        Assert.Equal([Samples.Model], LogText.Touched(a.Log[0]));
        Assert.Equal("the game", new Names(Game, a.Projection).Participant(null));
    }

    /// <summary>Two answers in the log: who spent what over which models (p.13), and which model a Takedown picked (p.14).</summary>
    [Fact]
    public void An_answer_names_the_pieces_it_carries()
    {
        var a = Applier(new AskOpened(SpendTokens), new AskOpened(TakedownAsk),
            new AskAnswered(TakedownAsk.Id, Samples.Team1, new SelectedPieces([Samples.Model]), false),
            new AskClosed(SpendTokens.Id, [new TeamAnswer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [Samples.Model] = 2 })), new TeamAnswer(Samples.Team2, new TokensSpent(new Dictionary<Guid, int> { [Spender] = 1 }))]));
        var names = new Names(Game, a.Projection);
        var lines = a.Log.Select(e => LogText.Describe(e, names)).ToList();
        // A pick of one model reads as the model, so a takedown model is named on the line that answered (p.14 Takedown).
        Assert.Equal("Clay answered for Team 1: picked Battle Brothers (Battle Brother)", lines[2]);
        // The sealed spend reads on the line that closed it: each team's total, then the models it came from, ordered by name so the words are stable.
        Assert.Equal("Clay closed the question: Team 1 spent 2 tokens (Battle Brothers (Battle Brother) 2), Team 2 spent 1 token (0198b7ac 1)", lines[3]);
        Assert.Equal([Samples.Model], LogText.Touched(a.Log[2]));
        Assert.Equal([Samples.Model, Spender], LogText.Touched(a.Log[3]));
        Assert.Equal("picked 2 pieces", LogText.Answer(new SelectedPieces([Samples.Model, Spender]), names));   // more than one is still a count
    }

    [Fact]
    public void My_ask_is_the_one_addressed_to_my_team_and_others_are_a_wait()
    {
        var a = Applier(new AskOpened(Activate));
        var mine = AskViewModel.Mine(a.Projection, Game, Samples.Team1, new HashSet<string>());
        Assert.Equal((AskMode.Choose, false), (mine!.Mode, mine.AutoPass));
        Assert.Equal([Samples.Unit], mine.Highlights);
        Assert.Null(AskViewModel.Mine(a.Projection, Game, Samples.Team2, new HashSet<string>()));
        Assert.Equal("Team 1", AskViewModel.WaitingFor(a.Projection, Game, Samples.Team2));
        Assert.Null(AskViewModel.Mine(a.Projection, Game, null, new HashSet<string>()));      // a watcher is never asked
    }

    [Fact]
    public void A_muted_window_auto_passes_and_a_simultaneous_spend_shows_who_answered_without_what()
    {
        var a = Applier(new AskOpened(SpendTokens), new AskAnswered(SpendTokens.Id, Samples.Team2, null, false));
        var mine = AskViewModel.Mine(a.Projection, Game, Samples.Team1, new HashSet<string> { "casting" })!;
        Assert.True(mine.AutoPass);
        Assert.Equal(AskMode.TokenSpend, mine.Mode);
        // The spend names both teams' models (p.13 gives the caster's side +1 and the other side -1), and only my own are mine to spend or to highlight.
        Assert.Equal([Samples.Model], mine.Highlights);
        Assert.Equal(new Dictionary<Guid, int> { [Samples.Model] = 2 }, AskViewModel.SpendableBy(a.Projection, Game, Samples.Team1, (TokenSpend)SpendTokens.Legal));
        Assert.Equal(["Team 2"], mine.AnsweredTeams);
        Assert.Null(AskViewModel.Mine(a.Projection, Game, Samples.Team2, new HashSet<string>()));     // already answered
        Assert.Equal("Clay", AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, new Names(Game, a.Projection)));
        Assert.Equal(AskMode.Confirm, AskViewModel.ModeOf(new Confirmation()));
        Assert.Equal(AskMode.Move, AskViewModel.ModeOf(new MoveRegion([Samples.Model], 6, [], [], null)));
        // A containment offer is still a move: the mode is the legal set's kind, and the containers it names are highlights too (p.14).
        var embarkable = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([Samples.Container], null, null));
        Assert.Equal(AskMode.Move, AskViewModel.ModeOf(embarkable));
        var moveAsk = new Ask(Guid.CreateVersion7(), "move", "Move up to 6\"", [Samples.Team1], embarkable, true, false, null, Samples.Frame);
        Assert.Equal([Samples.Model, Samples.Container], AskViewModel.Mine(Applier(new AskOpened(moveAsk)).Projection, Game, Samples.Team1, new HashSet<string>())!.Highlights);
        var exitAsk = moveAsk with { Id = Guid.CreateVersion7(), Question = "disembark", Legal = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([], Samples.Container, 6)) };
        Assert.Equal([Samples.Model, Samples.Container], AskViewModel.Mine(Applier(new AskOpened(exitAsk)).Projection, Game, Samples.Team1, new HashSet<string>())!.Highlights);
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Continue: move", [Samples.Team1], new Confirmation(), false, false, null, Samples.Frame);
        Assert.Equal(["Continue", "End the round"], new[] { gate, EndRound }.Select(AskViewModel.ConfirmLabel));
        var both = Applier(new AskOpened(EndRound));                                                       // unframed and addressed to every team: mine for either of them
        Assert.Equal(AskMode.Confirm, AskViewModel.Mine(both.Projection, Game, Samples.Team2, new HashSet<string>())!.Mode);
        Assert.Equal("Team 2", AskViewModel.WaitingFor(both.Projection, Game, Samples.Team1));             // reported, but never rendered: the panel shows WaitingFor only when Mine is null
    }

    [Fact]
    public void Menu_items_build_the_commands_the_spec_lists()
    {
        var p = Applier().Projection;
        var recorded = Applier(new AskOpened(Activate), new ActionRecorded(Samples.Unit, "Advance"), new ActionRecordCleared(Samples.Unit), new ActionRecorded(Samples.Unit, "Hold"));
        var actions = MenuModel.KnownActionNames(recorded.Projection, recorded.Log);
        Assert.Equal(["Advance", "Hold"], actions);                          // the activate Ask's labels ("{unit}: Advance") contribute nothing
        var unit = MenuModel.ForUnit(p, Samples.Unit, actions);
        Assert.IsType<RecordAction>(unit.Single(i => i.Label == "Mark as having taken Advance").Build(null));
        Assert.IsType<ClearActionRecord>(unit.Single(i => i.Label == "Clear the action record").Build(null));
        var shaken = unit.Single(i => i.Label == "Add a condition");
        Assert.Equal(("shaken", true), (((SetCondition)shaken.Build("shaken")!).Condition, ((SetCondition)shaken.Build("shaken")!).Present));
        var model = MenuModel.ForModel(p, Samples.Model, actions);
        Assert.Equal(3, ((SetWounds)model.Single(i => i.Label == "Set wounds").Build("3")!).Wounds);
        Assert.IsType<OnShelf>(((MoveModels)model.Single(i => i.Label == "Move to the shelf").Build(null)!).Moves.Single().To);
        Assert.Equal(MenuInput.PieceId, model.Single(i => i.Label == "Put inside a container").Input);
        Assert.IsType<Inside>(((MoveModels)model.Single(i => i.Label == "Put inside a container").Build(Samples.Unit.ToString())!).Moves.Single().To);
        Assert.Null(model.Single(i => i.Label == "Set wounds").Build("x"));
        // The manual override stays beside the action-driven path: a player may put a model in or take it out with no Ask open.
        var occupant = MenuModel.ForModel(Applier(new ModelsMoved([Samples.Embark])).Projection, Samples.Model, actions);
        var takeOut = occupant.Single(i => i.Label == "Take out of the container, here");
        Assert.Equal(MenuInput.TablePoint, takeOut.Input);
        Assert.Equal((typeof(Inside), typeof(OnTable)), (((MoveModels)takeOut.Build("14,12,0")!).Moves.Single().From.GetType(), ((MoveModels)takeOut.Build("14,12,0")!).Moves.Single().To.GetType()));
        Assert.DoesNotContain("Place on the table, here", occupant.Select(i => i.Label));
        var team = MenuModel.ForTeam(Game, p, Samples.Team1);
        Assert.Equal(2, ((SetScore)team.Single(i => i.Label == "Set score").Build("2")!).Score);
        Assert.IsType<SetActiveTeam>(team.Single(i => i.Label == "Make this the active team").Build(null));
        Assert.IsType<SetRound>(team.Single(i => i.Label == "Set the round").Build("3"));
        Assert.IsType<RemoveMarker>(MenuModel.ForMarker(Samples.Token.Id).Single(i => i.Label == "Remove").Build(null));
        var frame = MenuModel.ForFrame(Applier(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))).Projection);
        Assert.Equal(Samples.Frame, ((AbandonSequence)frame.Single().Build(null)!).FrameId);
        Assert.Empty(MenuModel.ForFrame(p));
    }

    [Fact]
    public void The_ruler_reads_displacement_and_remaining_allowance()
    {
        Assert.Equal(new RulerReadout(5, 1, false), MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), 6));
        Assert.Equal(new RulerReadout(5, null, false), MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), null));
        Assert.True(MoveRuler.Read(new Point2(0, 0), new Point2(7, 0), 6).OverAllowance);
        var a = Applier(new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "move", null, [], [new ModelsMoved([new ModelRelocation(Samples.Model, new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnTable(new Placement(new Point3(2, 0, 0), new Yaw(0))))])]));
        Assert.Equal(2, MoveRuler.UsedInFrame(a.Projection.Stack[0], Samples.Model), 6);
        var start = new PlacedModel(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(36, 24, 0), new Yaw(0)));
        var region = MoveRuler.Region(start, new MoveRegion([Samples.Model], 6, [], [], null), [], new TableSize(72, 48));
        Assert.Equal(Movement.Rays, region.Count);
        Assert.All(region, p => Assert.Equal(6, p.DistanceTo(new Point2(36, 24)), 6));
        var edge = MoveRuler.Region(start with { Placement = new Placement(new Point3(71, 24, 0), new Yaw(0)) }, new MoveRegion([Samples.Model], 6, [], [], null), [], new TableSize(72, 48));
        Assert.True(edge.Max(p => p.X) <= 72);
        var forest = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(1), new Point2(0, 0), new Yaw(0), 0, 8, TerrainDeclarations.Difficult, null), new TerrainTransform(new Point3(39, 24, 0), new Yaw(0), 1));
        var capped = MoveRuler.Region(start, new MoveRegion([Samples.Model], 12, [], [forest.Id], 6), [forest], new TableSize(72, 48));
        Assert.Equal(42, capped[0].X, 6);                                                        // the ray into the forest stops at the 6" cap
        Assert.Equal(24, capped[Movement.Rays / 2].X, 6);                                        // the ray away from it reaches the full 12"
    }

    /// <summary>
    /// A disembark has no start placement, so the region is the container's base grown by the exit distance
    /// (rulebook p.15: "must stay fully within 6\" of it when exiting") rather than the straight-line reach.
    /// </summary>
    [Fact]
    public void The_disembark_region_is_the_container_grown_by_the_exit_distance()
    {
        var bus = new PlacedModel(new ModelBody(Base.Rect(2, 2), 3), new Placement(new Point3(36, 24, 0), new Yaw(0)));
        var trooper = new ModelBody(Base.Circle(1), 1.5);
        var size = new TableSize(72, 48);
        var exit = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([], Samples.Container, 6));
        Assert.True(MoveRuler.IsDisembark(exit));
        Assert.False(MoveRuler.IsDisembark(new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([Samples.Container], null, null))));
        Assert.False(MoveRuler.IsDisembark(new MoveRegion([Samples.Model], 6, [], [], null)));

        var region = MoveRuler.ExitRegion(trooper, bus, exit, [], size);
        Assert.Equal(Movement.Rays, region.Count);
        // Edge to edge: the bus's edge is 1" from its centre and the trooper's far rim is 0.5" past its own, so 36 + 1 + 6 − 0.5.
        Assert.Equal(42.5, region[0].X, 2);
        Assert.Equal(30.5, region[Movement.Rays / 4].Y, 2);
        Assert.All(region, p => Assert.True(Containment.BaseFullyWithin(
            new PlacedModel(trooper, new Placement(new Point3(p.X, p.Y, 0), new Yaw(0))), bus, 6)));
        // It is not a circle about the centre: the diagonal reaches further than the axis, because the rect's corner is further out.
        Assert.True(region[Movement.Rays / 8].DistanceTo(new Point2(36, 24)) > region[0].DistanceTo(new Point2(36, 24)));

        // The slab clamps it, exactly as Movement.Region is clamped: a bus in the corner offers nothing off the table.
        var corner = MoveRuler.ExitRegion(trooper, bus with { Placement = new Placement(new Point3(1, 1, 0), new Yaw(0)) }, exit, [], size);
        Assert.All(corner, p => Assert.True(p.X >= 0.4 && p.Y >= 0.4));           // the 0.5" base radius is the whole clamp

        // An impassable volume beside the bus stops the rays that would end in it.
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(2), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable, null), new TerrainTransform(new Point3(40, 24, 0), new Yaw(0), 1));
        var blocked = MoveRuler.ExitRegion(trooper, bus, exit with { ImpassableVolumeIds = [wall.Id] }, [wall], size);
        Assert.Equal(37.5, blocked[0].X, 1);                                             // stopped at the wall's near face, 0.5" of base short of it
        Assert.Equal(30.5, blocked[Movement.Rays / 4].Y, 2);                             // and the ray away from it is untouched
    }

    [Fact]
    public void The_animation_queue_drains_at_once_when_not_animating()
    {
        var q = new AnimationQueue();
        q.Enqueue([new GameEvent(1, null, new ScoreSet(Samples.Team1, 1), null), new GameEvent(2, null, new ScoreSet(Samples.Team1, 2), null)]);
        Assert.Single(q.Take(animate: true)); Assert.Equal(1, q.Count);
        Assert.Single(q.Take(animate: false)); Assert.Equal(0, q.Count);
    }
}
