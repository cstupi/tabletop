using Tabletop.Client.Core;
using Tabletop.Content;
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
    public void Answered_by_names_a_teammate_s_live_answer_for_my_team_only()
    {
        var a = Applier(new AskOpened(SpendTokens), new AskAnswered(SpendTokens.Id, Samples.Team2, null, false));
        var names = new Names(Game, a.Projection);
        Assert.Equal("Clay", AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, Samples.Team2, Guid.Empty, names));
        Assert.Null(AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, Samples.Team1, Guid.Empty, names));   // the other team's answer is not my team's
        Assert.Null(AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, Samples.Team2, Samples.P1, names));   // my own answer is not news to me
        Assert.Null(AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, null, Guid.Empty, names));            // a watcher has no team
        var undone = Applier(new AskOpened(SpendTokens), new AskAnswered(SpendTokens.Id, Samples.Team2, null, false), new Undone(1));
        Assert.Null(AskViewModel.AnsweredBy(undone.Log, SpendTokens.Id, Samples.Team2, Guid.Empty, new Names(Game, undone.Projection)));   // an undo took the answer back
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
        Assert.Equal("Clay", AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, Samples.Team2, Guid.Empty, new Names(Game, a.Projection)));
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
        Assert.DoesNotContain(unit, i => i.Label.StartsWith("Mark") && i.Input == MenuInput.Text);   // only actions the property set names: the server refuses any other
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
        var placed = Applier(new MarkerPlaced(Samples.Token)).Projection;
        Assert.IsType<RemoveMarker>(MenuModel.ForMarker(Game, placed, Samples.Token.Id).Single(i => i.Label == "Remove").Build(null));
        var frame = MenuModel.ForFrame(Applier(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))).Projection);
        Assert.Equal(Samples.Frame, ((AbandonSequence)frame.Single().Build(null)!).FrameId);
        Assert.Empty(MenuModel.ForFrame(p));
    }

    [Fact]
    public void The_team_menu_lists_off_table_models_of_that_team_only()
    {
        var shelf = Applier().Projection;
        var name = new Names(Game, shelf).Model(Samples.Model);
        var place = MenuModel.OffTable(shelf, Samples.Team1, new Names(Game, shelf)).Single();
        Assert.Equal(($"Place {name} on the table, here", MenuInput.TablePoint), (place.Label, place.Input));
        var move = ((MoveModels)place.Build("5,6,0")!).Moves.Single();
        Assert.Equal((Samples.Model, typeof(OnShelf)), (move.ModelId, move.From.GetType()));
        Assert.Equal(new Point3(5, 6, 0), ((OnTable)move.To).Placement.Position);
        Assert.Null(place.Build("nope"));
        Assert.Empty(MenuModel.OffTable(shelf, Samples.Team2, new Names(Game, shelf)));

        var inside = Applier(new ModelsMoved([Samples.Embark])).Projection;
        var take = MenuModel.OffTable(inside, Samples.Team1, new Names(Game, inside)).Single();
        Assert.Equal($"Take {new Names(Game, inside).Model(Samples.Model)} out of the container, here", take.Label);
        Assert.IsType<Inside>(((MoveModels)take.Build("1,2,0")!).Moves.Single().From);

        var onTable = Applier(new ModelsMoved([Samples.Deploy])).Projection;
        Assert.Empty(MenuModel.OffTable(onTable, Samples.Team1, new Names(Game, onTable)));
    }

    [Fact]
    public void A_minimum_move_reads_short_moves_red_and_cuts_a_ring_from_the_region()
    {
        // p.13 Aircraft: the move Ask sets a 30" minimum; the table holds it to what the base can reach.
        Assert.True(MoveRuler.Read(new Point2(0, 0), new Point2(20, 0), 36, 30).OutOfRange);
        Assert.False(MoveRuler.Read(new Point2(0, 0), new Point2(31, 0), 36, 30).OutOfRange);
        var legal = new MoveRegion([Samples.Model], 36, [], [], null, null, 30);
        var start = new PlacedModel(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(36, 24, 0), new Yaw(0)));
        Assert.Equal(30, MoveRuler.Minimum(start, legal, new TableSize(72, 48)));
        Assert.True(MoveRuler.Minimum(start with { Placement = new Placement(new Point3(15, 15, 0), new Yaw(0)) }, legal, new TableSize(30, 30)) < 30);
        Assert.Null(MoveRuler.Minimum(start, legal with { MinDistance = null }, new TableSize(72, 48)));
        var ring = new MoveRegions(new Dictionary<Guid, PlacedModel> { [Samples.Model] = start }, legal, [], new TableSize(72, 48), []).Outline(Samples.Model);
        Assert.False(Areas.Inside(ring, new Point2(40, 24)));
        Assert.True(Areas.Inside(ring, new Point2(67, 24)));
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
        IReadOnlyList<Point2> Area(PlacedModel at, MoveRegion legal, IReadOnlyList<WorldVolume> volumes) =>
            new MoveRegions(new Dictionary<Guid, PlacedModel> { [Samples.Model] = at }, legal, volumes, new TableSize(72, 48), []).Outline(Samples.Model);
        var region = Area(start, new MoveRegion([Samples.Model], 6, [], [], null), []);
        Assert.True(Areas.Inside(region, new Point2(41.9, 24)) && !Areas.Inside(region, new Point2(42.1, 24)));
        var edge = Area(start with { Placement = new Placement(new Point3(71, 24, 0), new Yaw(0)) }, new MoveRegion([Samples.Model], 6, [], [], null), []);
        Assert.True(edge.Max(p => p.X) <= 72 - 0.63 + 1e-3);
        var forest = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(4), new Point2(0, 0), new Yaw(0), 0, 8, TerrainDeclarations.Difficult, null), new TerrainTransform(new Point3(44, 24, 0), new Yaw(0), 1));
        var capped = Area(start, new MoveRegion([Samples.Model], 12, [], [forest.Id], 6), [forest]);
        Assert.False(Areas.Inside(capped, new Point2(43, 24)));                                  // deep in the wood: past the 6" cap
        Assert.True(Areas.Inside(capped, new Point2(24.1, 24)));                                 // away from it: the full 12"
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

    [Fact]
    public void Game_ended_names_the_winner_or_the_tie()
    {
        var names = new Names(Game, Applier().Projection);
        string Line(params Guid[] winners) => LogText.Describe(new GameEvent(9, null, new GameEnded("4 rounds have been played", winners), null), names);
        Assert.Equal("Game ended: 4 rounds have been played; Team 1 won", Line(Samples.Team1));
        Assert.Equal("Game ended: 4 rounds have been played; tied between Team 1, Team 2", Line(Samples.Team1, Samples.Team2));
        Assert.Equal("Game ended: 4 rounds have been played", Line());
    }

    [Fact]
    public void An_answer_that_broke_a_named_rule_is_labelled_with_it()
    {
        var a = Applier();
        var names = new Names(Game, a.Projection);
        string Line(bool outside, string? breach) => LogText.Describe(new GameEvent(9, Samples.P1, new AskAnswered(Samples.AskId, Samples.Team1, null, outside, breach), null), names);
        Assert.EndsWith(" (breaks difficult_cap)", Line(false, "difficult_cap"));
        Assert.EndsWith(" (outside the legal set)", Line(true, null));
    }

    [Fact]
    public void Control_reads_as_taking_a_marker_or_making_it_neutral()
    {
        var a = Applier(new MarkerPlaced(Samples.Token));
        var names = new Names(Game, a.Projection);
        string Line(EventPayload p) => LogText.Describe(new GameEvent(9, null, p, null), names);
        Assert.Equal("the game ended round 4: Team 1 took smoke", Line(new RoundEnded(4, [new ObjectiveControlSet(Samples.Token.Id, Samples.Team1)])));
        Assert.Equal("the game ended round 4", Line(new RoundEnded(4, [])));
        Assert.Equal("the game made smoke neutral", Line(new ObjectiveControlSet(Samples.Token.Id, null)));
        var setup = a.Projection.Board.Table.Markers[0].Id;
        Assert.Equal("the game gave Objective 1 to Team 2", Line(new ObjectiveControlSet(setup, Samples.Team2)));   // a setup marker with no label: its kind and place

        // A labelled setup marker verifies the setup marker lookup works (would fail if Names.Marker's board lookup were deleted)
        var labelledMarker = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(30, 20, 0), new Yaw(0), 3, "centre", VisualRef.OfBuiltin("objective-ring"));
        var labelledTable = new TableState(Guid.CreateVersion7(), "Labelled", new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
            [], [labelledMarker], []);
        var labelledBoard = new Board(labelledTable, [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(Samples.Army, Guid.CreateVersion7()))]);
        var b = new EventApplier(labelledBoard, Game);
        var namesb = new Names(Game, b.Projection);
        Assert.Equal("the game gave centre to Team 2", LogText.Describe(new GameEvent(9, null, new ObjectiveControlSet(labelledMarker.Id, Samples.Team2), null), namesb));
    }

    [Fact]
    public void An_objective_from_setup_or_play_can_be_given_to_a_team_or_made_neutral_and_a_token_cannot()
    {
        var p = Applier(new MarkerPlaced(Samples.Token)).Projection;
        var setup = p.Board.Table.Markers.Single(m => m.Kind == Tabletop.Content.MarkerKind.Objective).Id;
        var items = MenuModel.ForMarker(Game, p, setup);
        Assert.Equal(["Give to Team 1", "Give to Team 2", "Make neutral"], items.Select(i => i.Label));    // no Remove: a setup row is not a play marker
        Assert.Equal(new SetObjectiveControl(setup, Samples.Team2), items[1].Build(null));
        Assert.Equal(new SetObjectiveControl(setup, null), items[2].Build(null));
        Assert.Equal(["Remove"], MenuModel.ForMarker(Game, p, Samples.Token.Id).Select(i => i.Label));      // a token holds nothing
        Assert.Empty(MenuModel.ForMarker(Game, p, Guid.CreateVersion7()));
    }
}
