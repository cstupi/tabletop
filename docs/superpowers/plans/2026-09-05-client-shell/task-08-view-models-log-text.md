# Task 8: View models — log text, the Ask view, the menu, the ruler, the animation queue

**Files:**
- Create: `client/src/Core/Names.cs`, `client/src/Core/LogText.cs`, `client/src/Core/AskViewModel.cs`, `client/src/Core/MenuModel.cs`, `client/src/Core/MoveRuler.cs`, `client/src/Core/AnimationQueue.cs`
- Test: `tests/Tabletop.Client.Tests/ViewModelTests.cs`

**Interfaces:**
- Consumes: `Projection`, `UnitInPlay`, `ModelInPlay`, `Frame`, `OpenAsk`, `Ask`, `LegalSet` kinds, `AskAnswer` kinds, `GameEvent`, every `EventPayload`, `Command` kinds, `MoveRegion` (with `DifficultVolumeIds`, `DifficultCap` and `Containment`) and `MoveContainment` (contracts plan Task 8), `Inside` (Protocol); `GameInfo` (Protocol); `Movement`, `Containment.BaseFullyWithin`, `PlacedModel`, `ModelBody`, `Base`, `WorldVolume`, `Point2`, `TableSize` (Geometry).
- Produces: `sealed class Names(GameInfo game, Projection? projection)` — `string Participant(Guid?)`, `string Team(Guid)` (`"Team {ordinal}"`), `string Unit(Guid)`, `string Model(Guid)`, `string Piece(Guid)` (unit, model, marker or the id's first eight characters); `static class LogText` — `string Describe(GameEvent, Names)`, `string Answer(AskAnswer, Names)` (an answer in words, naming the pieces it carries), `IReadOnlyList<Guid> Touched(GameEvent)`; `enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }`; `sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass)`; `static class AskViewModel` — `AskView? Mine(Projection, GameInfo, Guid? myTeam, IReadOnlySet<string> muted)` (the first open Ask addressed to my team that my team has not answered), `string? WaitingFor(Projection, GameInfo, Guid? myTeam)` (the teams other open Asks name), `string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names)`, `AskMode ModeOf(LegalSet)`, `IReadOnlyDictionary<Guid, int> SpendableBy(Projection, GameInfo, Guid team, TokenSpend)` (the spend map narrowed to the models of one team), `string ConfirmLabel(Ask)` (`"End the round"` for `AskQuestions.EndRound`, else `"Continue"`); `enum MenuInput { None, Integer, Text, PieceId, TablePoint }` (`TablePoint` is the next left click on the table, handed to `Build` as `"x,y,z"`); `sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build)`; `static class MenuModel` — `ForModel(Projection, Guid modelId, IReadOnlyList<string> actionNames)`, `ForUnit(Projection, Guid unitId, IReadOnlyList<string> actionNames)`, `ForTeam(GameInfo, Projection, Guid teamId)`, `ForMarker(Guid markerId)`, `ForFrame(Projection)`, `IReadOnlyList<string> KnownActionNames(Projection, IReadOnlyList<GameEvent>)`; `sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance)`; `static class MoveRuler` — `RulerReadout Read(Point2 start, Point2 current, double? allowance)`, `double UsedInFrame(Frame, Guid modelId)`, `IReadOnlyList<Point2> Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize)` (the `Movement.Region` boundary from the Ask's allowance, impassable and difficult ids and cap), `IReadOnlyList<Point2> ExitRegion(ModelBody body, PlacedModel container, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize)` (the disembark region: the container's base grown by `Containment.ExitWithin`, one vertex per `Movement.Rays` ray, marched out in `Movement.Step` increments and bisected at the first refusal, each position tried at every `ExitYaws` facing for a non-circular base and at one for a circle), `Yaw? ExitYawAt(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> volumes, TableSize)` (a facing that position is legal at, or null — what the answer carries), `const int ExitYaws = 8`, `bool IsDisembark(MoveRegion legal)` (`legal.Containment?.ExitingId is not null`); `sealed class AnimationQueue` — `void Enqueue(IReadOnlyList<GameEvent>)`, `IReadOnlyList<GameEvent> Take(bool animate)` (everything when not animating, one when animating), `int Count`.
- Action names are not on the wire (the `action` rows never reach the client); `KnownActionNames` reads them from `ActionRecorded` events and each unit's recorded `Actions`, never from an Ask's option labels, and the menu falls back to free text — *Interfaces needed* item 5.
- **`Region` cannot serve a disembark, so there are two region functions.** `MoveRegion.Containment.ExitingId` non-null means the unit is *inside* the container and its models have no `Placement` (game flow → Inside a container: *"an occupant has no table position of its own"*), so there is no `start` to sweep rays from and `Movement.Region` is meaningless — `Allowance` is the action's remaining distance and binds nothing here (contracts plan Task 8). `IsDisembark(legal)` is that test, and `ExitRegion` answers instead: the legal area is every placement `Containment.BaseFullyWithin(candidate, container, ExitWithin)` admits, taken about the container's own `PlacedModel`. It is expressed as a **boundary**, not a predicate, so `MoveRegionNode` (Task 14) draws it with no change: `Movement.Rays` rays out of the container's base centre, each marched outward in `Movement.Step` increments and bisected eight times at the first refusal — the same loop shape `Movement.Region` uses, which is what keeps a ring of impassable terrain from re-admitting placements beyond it. Every candidate is also refused when it would leave the slab or touch an impassable volume, asked through `Movement.Check(candidate, its own centre, 0, impassable, [], null, size)`: distance zero, so `Check` samples exactly that placement and answers `CrossesImpassable` and `LeavesTable` for it. No new geometry primitive. The client draws; the server re-checks every model with `GfMoves.ExitPlacement` and leaves a model placed too far inside (interpreter plan Task 14), so this is rendering an offer, not a legality check.
- The region is drawn for **one** body — the lead model's, as `Region` already is. A unit whose models have different bases gets the lead's region and the server refuses the outliers individually; `ExitRegion` therefore takes a `ModelBody` rather than a `PlacedModel`, because an occupant has no placement to pass.

Spec: client shell → The log, undo and the menu (words with actor and sequence; clicking highlights what it touched; every property editable; each item one command; no admin mode, no confirmation dialog), Asks and windows (one panel; the legal set becomes choices and highlights; other teams' Asks are a waiting indicator; mutes auto-pass and the pass is still sent; sealed shows who answered; confirm gates), Input and presence (the ruler: displacement, remaining allowance, distance used from the frame), Animation (a queue between the projection and the renderer that never delays an Ask); scheduler → The turn is an Ask (options are unit × action), There is no round limit (the round advances only when a player answers the `end_round` Ask; the client's whole share of it is two words: the button's label and the answer's log line), Nothing is an invariant (menu edits); game flow → Inside a container (a location, not a condition; an occupant has no table position of its own and disembarks relative to the container; enter or exit with any move action, never both in one activation, exit within 6"); geometry plan → `Movement.Region`, `Containment.BaseFullyWithin` and *Interfaces needed → Client shell plan*; rulebook p.15 `Transport(X)` — *"must stay fully within 6\" of it when exiting"* and *"only one model needs to reach the Transport for everyone to embark"*; rulebook p.13 `Caster(X)` — *"Models within 18\" in line of sight of the caster's unit may spend any number of spell tokens at the same time before rolling"* — and `Takedown`, whose shooter picks the model that takes the wound (p.14). The spend is a sealed `TokenSpend` carrying each eligible model's own maximum and the pick a `PieceSelection` over model ids, so this task's share of both is the mode, the highlights and the words.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run; expect compile errors. Write `Names.cs` and `LogText.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Ids to words. Falls back to the id's first eight characters rather than throwing: a name is presentation.</summary>
public sealed class Names(GameInfo game, Projection? projection)
{
    public string Participant(Guid? id) => id is null ? "the game" : game.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? Short(id.Value);
    public string Team(Guid id) => game.Teams.FirstOrDefault(t => t.Id == id) is { } t ? $"Team {t.Ordinal}" : Short(id);
    public string Unit(Guid id) => projection?.Units.GetValueOrDefault(id)?.Definition.Name ?? Short(id);
    public string Model(Guid id) => projection?.Models.GetValueOrDefault(id) is { } m ? $"{Unit(m.UnitId)} ({m.Slot.Name})" : Short(id);
    public Guid? UnitOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId)?.UnitId;
    public string Piece(Guid id) => projection is null ? Short(id)
        : projection.Units.ContainsKey(id) ? Unit(id) : projection.Models.ContainsKey(id) ? Model(id) : projection.Markers.GetValueOrDefault(id)?.Label ?? Short(id);
    public static string Short(Guid id) => id.ToString("N")[..8];
}
```

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Every event in words, with its actor. Presence never appears here because presence is never an event.</summary>
public static class LogText
{
    public static string Describe(GameEvent e, Names n)
    {
        var who = n.Participant(e.Actor);
        var text = e.Payload switch
        {
            ModelsMoved m => Moved(m, n),
            WoundsSet w => $"set {n.Model(w.ModelId)} to {w.Wounds} wound{(w.Wounds == 1 ? "" : "s")}",
            ConditionSet c => $"{(c.Present ? "marked" : "cleared")} {(c.Subject.Kind == SubjectKind.Unit ? n.Unit(c.Subject.Id) : n.Model(c.Subject.Id))} {c.Condition}",
            ActionRecorded a => $"recorded {a.Action} for {n.Unit(a.UnitId)}",
            ActionRecordCleared a => $"cleared {n.Unit(a.UnitId)}'s action record",
            GrantAdded g => $"granted {g.Grant.EntryName} to {n.Unit(g.UnitId)}",
            GrantRemoved g => $"removed a grant from {n.Unit(g.UnitId)}",
            SpellTokensSet s => $"set {n.Model(s.ModelId)} to {s.Tokens} spell token{(s.Tokens == 1 ? "" : "s")}",
            WoundsThisActivationSet w => $"set {n.Unit(w.UnitId)}'s wounds this activation to {w.Wounds}",
            ScoreSet s => $"set {n.Team(s.TeamId)}'s score to {s.Score}",
            MarkerPlaced m => $"placed a {m.Marker.Kind.ToString().ToLowerInvariant()}{(m.Marker.Label is null ? "" : $" '{m.Marker.Label}'")}",
            MarkerMoved m => $"moved marker {n.Piece(m.MarkerId)}",
            MarkerRemoved m => $"removed marker {n.Piece(m.MarkerId)}",
            RoundStarted r => $"started round {r.Round}",
            TurnStarted t => $"started {n.Team(t.TeamId)}'s turn",
            TurnEnded t => t.Passed ? $"passed for {n.Team(t.TeamId)}" : $"ended {n.Team(t.TeamId)}'s turn",
            PhaseStarted p => $"began {p.Phase}",
            SequenceStarted s => $"began {s.Sequence}{(s.Context.ActorUnitId is { } u ? $" with {n.Unit(u)}" : "")}",
            StepCompleted s => Step(s),
            SequenceFinished => "finished the sequence",
            SequenceAbandoned => "abandoned the sequence",
            AskOpened a => $"asked {string.Join(", ", a.Ask.Teams.Select(n.Team))}: {a.Ask.Prompt}",
            // "confirmed" is what a confirm gate and the round boundary's end_round Ask both are; the round_started line under it says which round began.
            AskAnswered a => a.Answer switch
            {
                Passed => $"passed for {n.Team(a.TeamId)}",
                Confirmed => $"confirmed for {n.Team(a.TeamId)}",
                // An unsealed answer is in hand, so it is in words: a takedown's pick has to name the model it picked (p.13).
                { } answer => $"answered for {n.Team(a.TeamId)}: {Answer(answer, n)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",
                null => $"answered for {n.Team(a.TeamId)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",   // withheld: a sealed Ask reveals at ask_closed
            },
            AskClosed a => a.Answers.Count == 0 ? "closed the question"
                : $"closed the question: {string.Join(", ", a.Answers.Select(x => $"{n.Team(x.TeamId)} {Answer(x.Answer, n)}"))}",   // a sealed Ask's reveal
            DiceRerolled d => $"rerolled {d.Replaced.Count} dice",
            DiceEdited d => $"set {d.Replaced.Count} dice by hand",
            ModifierAdded m => $"added {m.Modifier.Amount:+#;-#;0} to the {m.Roll} roll",
            Undone u => $"undid back to #{u.ToSeq}",
            var other => other.GetType().Name,
        };
        return $"{who} {text}" + (e.Warning is null ? "" : $" — {e.Warning}");
    }

    static string Moved(ModelsMoved m, Names n)
    {
        var first = m.Moves[0];
        var unit = n.UnitOfModel(first.ModelId) is { } unitId ? n.Unit(unitId) : Names.Short(first.ModelId);
        return (first.From, first.To) switch
        {
            (OnTable a, OnTable b) => $"moved {unit} {a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal):0.0}\"",
            (OnShelf, OnTable) => $"deployed {unit}",
            (Inside i, OnTable) => $"took {unit} out of {n.Piece(i.ContainerId)}",
            (_, OnShelf) => $"moved {unit} to the shelf",
            (_, Inside i) => $"put {unit} inside {n.Piece(i.ContainerId)}",
            _ => $"moved {unit}",
        };
    }
    /// <summary>
    /// An answer in words. A sealed Ask's answers reach the client only inside <c>ask_closed</c>, which is the reveal.
    /// Names is taken because two answers have to name pieces (p.13 and p.14): a one-piece selection is
    /// a Takedown's chosen model, and a token spend is a count per model rather than one number.
    /// </summary>
    public static string Answer(AskAnswer a, Names n) => a switch
    {
        Chosen c => $"chose {c.OptionId}",
        SelectedPieces { PieceIds.Count: 1 } s => $"picked {n.Piece(s.PieceIds[0])}",
        SelectedPieces s => $"picked {s.PieceIds.Count} piece{(s.PieceIds.Count == 1 ? "" : "s")}",
        Allocated al => $"allocated {al.Counts.Values.Sum()}",
        Assigned => "assigned targets",
        Destinations d => $"moved {d.Moves.Count} model{(d.Moves.Count == 1 ? "" : "s")}",
        TokensSpent t => $"spent {t.Counts.Values.Sum()} token{(t.Counts.Values.Sum() == 1 ? "" : "s")} ({Parts(t.Counts, n)})",
        Confirmed => "confirmed",
        Passed => "passed",
        _ => "answered",
    };

    /// <summary>Which model gave what, ordered by the name so the words do not depend on a dictionary's order.</summary>
    static string Parts(IReadOnlyDictionary<Guid, int> counts, Names n) =>
        string.Join(", ", counts.Where(kv => kv.Value > 0).Select(kv => $"{n.Piece(kv.Key)} {kv.Value}").Order(StringComparer.Ordinal));

    static string Step(StepCompleted s)
    {
        if (s.Rolls.Count == 0) return $"completed {s.Step}";
        var dice = s.Rolls.SelectMany(r => r.Dice).ToList();
        return $"completed {s.Step}: {dice.Count(d => d.Success)} of {dice.Count} succeeded";
    }

    /// <summary>What clicking the entry highlights on the table.</summary>
    public static IReadOnlyList<Guid> Touched(GameEvent e) => e.Payload switch
    {
        ModelsMoved m => m.Moves.Select(x => x.ModelId).ToList(),
        WoundsSet w => [w.ModelId], SpellTokensSet s => [s.ModelId],
        ConditionSet c => [c.Subject.Id], ActionRecorded a => [a.UnitId], ActionRecordCleared a => [a.UnitId],
        GrantAdded g => [g.UnitId], GrantRemoved g => [g.UnitId], WoundsThisActivationSet w => [w.UnitId],
        MarkerPlaced m => [m.Marker.Id], MarkerMoved m => [m.MarkerId], MarkerRemoved m => [m.MarkerId],
        SequenceStarted s => new[] { s.Context.ActorUnitId }.Where(x => x is not null).Select(x => x!.Value).Concat(s.Context.TargetUnitIds).ToList(),
        StepCompleted s => s.Changes.SelectMany(c => Touched(new GameEvent(0, null, c, null))).ToList(),
        AskOpened a => a.Ask.Legal is Options o ? o.Choices.SelectMany(c => c.Pieces).Distinct().ToList() : a.Ask.Legal is PieceSelection p ? p.Eligible : [],
        // An answer names pieces too: the model a Takedown picked, and the models a team spent its tokens from (p.14).
        AskAnswered a => a.Answer is null ? [] : AnswerPieces(a.Answer),
        AskClosed a => a.Answers.SelectMany(x => AnswerPieces(x.Answer)).Distinct().ToList(),
        _ => [],
    };

    static IReadOnlyList<Guid> AnswerPieces(AskAnswer a) => a switch
    {
        SelectedPieces s => s.PieceIds,
        Allocated al => al.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        TokensSpent t => t.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        Assigned a2 => a2.Targets.Values.Distinct().ToList(),
        Destinations d => d.Moves.Select(m => m.ModelId).ToList(),
        _ => [],
    };
}
```

`Moved` names the unit the first moved model belongs to, through `Names.UnitOfModel`; a unit-wide drag is several relocations and one line. That is what makes the embark line right: p.15 embarks the **whole** unit when one model reaches the transport, so the answer is one relocation per model to the same `Inside`, and the log reads *"Clay put Battle Brothers inside Bus (Bus)"* once, naming the container through `Names.Piece` — a model id, so `Names.Model` resolves it, and an unknown id degrades to its first eight characters like every other name here. The disembark reads *"took … out of …"* rather than falling through to the bare *"moved"*.

- [ ] **Step 3: Write `AskViewModel.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }

/// <summary>What the one Ask panel renders. Highlights are the pieces the legal set names.</summary>
public sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass);

public static class AskViewModel
{
    public static AskMode ModeOf(LegalSet legal) => legal switch
    {
        Options => AskMode.Choose, PieceSelection => AskMode.PickPieces, Allocation => AskMode.Allocate,
        Assignment => AskMode.Assign, MoveRegion => AskMode.Move, TokenSpend => AskMode.TokenSpend, Confirmation => AskMode.Confirm,
        _ => throw new ArgumentOutOfRangeException(nameof(legal)),
    };

    /// <summary>
    /// The spend map narrowed to one team's own models. p.13's window addresses both teams — the caster's side spends for
    /// +1 and the other side for -1 — so <c>TokenSpend.MaxByModel</c> names models this player may not spend, and a spin box
    /// over one of them would be a control the player cannot use. The map itself discloses nothing: every projection already
    /// carries each model's <c>SpellTokens</c>. A model whose unit or participant this projection cannot resolve is not mine.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> SpendableBy(Projection p, GameInfo game, Guid team, TokenSpend legal) =>
        legal.MaxByModel.Where(kv => TeamOfModel(p, game, kv.Key) == team).ToDictionary(kv => kv.Key, kv => kv.Value);

    static Guid? TeamOfModel(Projection p, GameInfo game, Guid modelId) =>
        p.Models.GetValueOrDefault(modelId) is { } m && p.Units.GetValueOrDefault(m.UnitId) is { } unit
            ? game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId
            : null;

    /// <summary>The first open Ask addressed to my team that my team has not answered. Null for a watcher: a participant with no team is never Asked. `myTeam` comes from the participant's row by way of `Welcome`, so a player who reconnects mid-Ask is addressed by it again (game flow → Teams are frozen at the flip); callers pass null only for a real watcher, never for a client that is mid-reconnect.</summary>
    public static AskView? Mine(Projection p, GameInfo game, Guid? myTeam, IReadOnlySet<string> muted)
    {
        if (myTeam is not { } team) return null;
        var open = p.OpenAsks.Values.OrderBy(a => a.Ask.Id).FirstOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team));
        if (open is null) return null;
        var names = new Names(game, p);
        var ask = open.Ask;
        var highlights = ask.Legal switch
        {
            Options o => o.Choices.SelectMany(c => c.Pieces).Distinct().ToList(),
            PieceSelection s => s.Eligible, Allocation a => a.Targets, Assignment a => a.Targets, MoveRegion m => MoveHighlights(m),
            TokenSpend t => SpendableBy(p, game, team, t).Keys.ToList(),          // my own spenders only (p.13)
            _ => (IReadOnlyList<Guid>)[],
        };
        var autoPass = ask.WindowKind is { } kind && muted.Contains(kind) && ask.AllowPass;
        return new AskView(ask, ModeOf(ask.Legal), highlights, open.Answers.Keys.Select(names.Team).ToList(), autoPass);
    }

    /// <summary>
    /// The pieces a move Ask names: its models, plus the containers containment offers. An enterable container is a click
    /// target for the answer, and the container a unit is exiting anchors the region, so both are highlights — "the legal set
    /// from the server becomes the panel's choices and the table's highlights" (client shell → Asks and windows). An occupant
    /// has no node, so highlighting its model id is a no-op; the container's node is the one the player can see and click.
    /// </summary>
    static IReadOnlyList<Guid> MoveHighlights(MoveRegion m)
    {
        var ids = m.ModelIds.ToList();
        if (m.Containment is { } c) { ids.AddRange(c.Enterable); if (c.ExitingId is { } exiting) ids.Add(exiting); }
        return ids.Distinct().ToList();
    }

    /// <summary>The waiting indicator: which teams the other open Asks are addressed to.</summary>
    public static string? WaitingFor(Projection p, GameInfo game, Guid? myTeam)
    {
        var names = new Names(game, p);
        var teams = p.OpenAsks.Values.SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t) && t != myTeam)).Distinct().Select(names.Team).ToList();
        return teams.Count == 0 ? null : string.Join(", ", teams);
    }

    /// <summary>
    /// What the one button of a Confirm Ask says. A step gate says Continue; the round boundary's end_round Ask (unframed, addressed to
    /// every team that has a participant) says what it does, because the round advances only when a player answers it (scheduler → There is no round limit).
    /// </summary>
    public static string ConfirmLabel(Ask ask) => ask.Question == AskQuestions.EndRound ? "End the round" : "Continue";

    /// <summary>Who on the team answered: the ask_answered event's actor.</summary>
    public static string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names names) =>
        log.LastOrDefault(e => e.Payload is AskAnswered a && a.AskId == askId) is { } e ? names.Participant(e.Actor) : null;
}
```

- [ ] **Step 4: Write `MenuModel.cs`**

```csharp
using System.Globalization;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum MenuInput { None, Integer, Text, PieceId, TablePoint }

/// <summary>One menu entry: a label, what it asks the player for, and the command it becomes. Null from Build means the input did not parse.</summary>
public sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build);

/// <summary>Every property is editable, each item one ordinary command through the same path automation uses. No admin mode.</summary>
public static class MenuModel
{
    static int? Int(string? s) => int.TryParse(s, out var n) ? n : null;
    static Guid? Id(string? s) => Guid.TryParse(s, out var g) ? g : null;
    /// <summary>A TablePoint item's input: the wire-frame point the player clicked, as "x,y,z".</summary>
    static Point3? Point(string? s) => s?.Split(',') is [var x, var y, var z]
        && double.TryParse(x, CultureInfo.InvariantCulture, out var px) && double.TryParse(y, CultureInfo.InvariantCulture, out var py)
        && double.TryParse(z, CultureInfo.InvariantCulture, out var pz) ? new Point3(px, py, pz) : null;

    public static IReadOnlyList<MenuItem> ForUnit(Projection p, Guid unitId, IReadOnlyList<string> actionNames)
    {
        var items = new List<MenuItem>();
        foreach (var a in actionNames) items.Add(new($"Mark as having taken {a}", MenuInput.None, _ => new RecordAction(unitId, a)));
        items.Add(new("Mark as having taken…", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new RecordAction(unitId, s.Trim())));
        items.Add(new("Clear the action record", MenuInput.None, _ => new ClearActionRecord(unitId)));
        items.Add(new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Unit, unitId), s.Trim(), true)));
        foreach (var c in p.Units.GetValueOrDefault(unitId)?.Conditions ?? new HashSet<string>())
            items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Unit, unitId), c, false)));
        items.Add(new("Set wounds this activation", MenuInput.Integer, s => Int(s) is { } n ? new SetWoundsThisActivation(unitId, n) : null));
        foreach (var g in p.Units.GetValueOrDefault(unitId)?.Grants ?? [])
            items.Add(new($"Remove grant {g.EntryName}", MenuInput.None, _ => new RemoveGrant(unitId, g.Id)));
        var models = p.Models.Values.Where(m => m.UnitId == unitId).ToList();
        items.Add(new("Move the unit to the shelf", MenuInput.None, _ => new MoveModels(models.Select(m => new ModelRelocation(m.Id, m.Location, new OnShelf())).ToList())));
        return items;
    }

    public static IReadOnlyList<MenuItem> ForModel(Projection p, Guid modelId, IReadOnlyList<string> actionNames)
    {
        var model = p.Models[modelId];
        var items = new List<MenuItem>
        {
            new("Set wounds", MenuInput.Integer, s => Int(s) is { } n ? new SetWounds(modelId, n) : null),
            new("Set spell tokens", MenuInput.Integer, s => Int(s) is { } n ? new SetSpellTokens(modelId, n) : null),
            new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Model, modelId), s.Trim(), true)),
            new("Move to the shelf", MenuInput.None, _ => new MoveModels([new ModelRelocation(modelId, model.Location, new OnShelf())])),
            new("Put inside a container", MenuInput.PieceId, s => Id(s) is { } c ? new MoveModels([new ModelRelocation(modelId, model.Location, new Inside(c))]) : null),
        };
        foreach (var c in model.Conditions) items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Model, modelId), c, false)));
        // "move to the shelf or back", "put inside a container or take out": coming back is one relocation to OnTable,
        // and the position comes from the next click on the table (client shell -> the menu).
        if (model.Location is not OnTable)
            items.Add(new(model.Location is Inside ? "Take out of the container, here" : "Place on the table, here", MenuInput.TablePoint,
                s => Point(s) is { } at ? new MoveModels([new ModelRelocation(modelId, model.Location, new OnTable(new Placement(at, new Yaw(0))))]) : null));
        items.AddRange(ForUnit(p, model.UnitId, actionNames).Where(i => i.Label.StartsWith("Mark") || i.Label == "Clear the action record"));
        return items;
    }

    public static IReadOnlyList<MenuItem> ForTeam(GameInfo game, Projection p, Guid teamId) =>
    [
        new("Set score", MenuInput.Integer, s => Int(s) is { } n ? new SetScore(teamId, n) : null),
        new("Make this the active team", MenuInput.None, _ => new SetActiveTeam(teamId)),
        new("Set the round", MenuInput.Integer, s => Int(s) is { } n ? new SetRound(n) : null),
    ];

    public static IReadOnlyList<MenuItem> ForMarker(Guid markerId) => [new("Remove", MenuInput.None, _ => new RemoveMarker(markerId))];

    /// <summary>Abandon the top frame: pops it and everything above; applied effects stay applied.</summary>
    public static IReadOnlyList<MenuItem> ForFrame(Projection p) =>
        p.Stack.Count == 0 ? [] : [new($"Abandon {p.Stack[^1].Sequence}", MenuInput.None, _ => new AbandonSequence(p.Stack[^1].Id))];

    /// <summary>The action names the client has seen: every action recorded in the log and each unit's current record. The rows themselves never reach the client,
    /// and the activate Ask's option labels are display text ("{unit name}: {action}"), never parsed.</summary>
    public static IReadOnlyList<string> KnownActionNames(Projection p, IReadOnlyList<GameEvent> log)
    {
        var names = new List<string>();
        names.AddRange(log.Select(e => e.Payload).OfType<ActionRecorded>().Select(a => a.Action));
        names.AddRange(p.Units.Values.SelectMany(u => u.Actions));
        return names.Distinct().ToList();
    }
}
```

`Place on the table, here` / `Take out of the container, here` is a `TablePoint` item: `PieceMenu` (Task 15) waits for the next left click on the table and hands `Build` the wire-frame point as `"x,y,z"`. That is the "or back" and the "take out" half of the spec's list; the shelf and container items are the other half.

**"Put inside a container" and "Take out of the container, here" stay, beside the action-driven path.** The move Ask's `Containment` (Task 14) is how a player embarks or disembarks *as part of an action*, under the ruleset's own conditions — the transport in reach, not both in one activation, the exit inside 6". The menu items are the manual override the spec asks for by name (*"put inside a container or take out"*, client shell → The log, undo and the menu), and they are the only path when no Ask is open at all: a transport deploying with units inside (p.14: *"Transports may deploy with units inside"*, and deployment's Ask is a `Destinations` for the transport, not a containment offer), a correction after a misclick, or a game played under `warn`/`silent` enforcement where stepping outside the offer is how the players fix things. `MoveModels` is the same command either path sends, logged with the same actor.

- [ ] **Step 5: Write `MoveRuler.cs` and `AnimationQueue.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Distance is the base's displacement from where it started (GF p.7); Remaining is what the allowance leaves.</summary>
public sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance);

public static class MoveRuler
{
    public static RulerReadout Read(Point2 start, Point2 current, double? allowance)
    {
        var d = start.DistanceTo(current);
        return new RulerReadout(d, allowance is { } a ? Math.Max(0, a - d) : null, allowance is { } b && d > b + 1e-6);
    }

    /// <summary>Distance a model has already moved inside a frame: the sum of its logged table-to-table relocations in the frame's completed steps.</summary>
    public static double UsedInFrame(Frame frame, Guid modelId) =>
        frame.Steps.SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves)
            .Where(r => r.ModelId == modelId && r.From is OnTable && r.To is OnTable)
            .Sum(r => ((OnTable)r.From).Placement.Position.Horizontal.DistanceTo(((OnTable)r.To).Placement.Position.Horizontal));

    /// <summary>The reachable region: Movement.Region from the Ask's parameters — the allowance, the impassable and difficult volumes resolved by id from the board, and the cap GameRules computed. One frame, no conversion.</summary>
    public static IReadOnlyList<Point2> Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        return Movement.Region(start, legal.Allowance, impassable, difficult, legal.DifficultCap, size).Boundary;
    }

    /// <summary>The unit is inside a container, so this move is a disembark and <see cref="Region"/> cannot serve it: there is no start placement to sweep from and Allowance binds nothing (contracts plan Task 8).</summary>
    public static bool IsDisembark(MoveRegion legal) => legal.Containment?.ExitingId is not null;

    /// <summary>
    /// The disembark region: rulebook p.15, "must stay fully within 6\" of it when exiting", measured edge to edge from the
    /// container's base — so it is the container's footprint grown by the exit distance, not a circle about its centre. One
    /// vertex per ray out of the container's base centre, marched outward in Movement.Step increments and bisected at the
    /// first refusal, which is the loop Movement.Region uses and is what stops a ring of impassable terrain re-admitting
    /// placements beyond it. The candidate takes the container's Z, because the drop settles the height locally while the
    /// server settles it for the log; its yaw is whatever fits, since p.7 lets a model take any facing.
    /// </summary>
    public static IReadOnlyList<Point2> ExitRegion(ModelBody body, PlacedModel container, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var within = legal.Containment?.ExitWithin ?? 0;
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var origin = container.Placement.Position.Horizontal;
        var reach = within + Math.Max(body.Base.A, body.Base.B) + Math.Max(container.Body.Base.A, container.Body.Base.B);
        var boundary = new List<Point2>(Movement.Rays);
        for (var r = 0; r < Movement.Rays; r++)
        {
            var angle = 2 * Math.PI * r / Movement.Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Movement.Step, reach);
                if (!Admits(body, container, within, origin + dir * d, impassable, size))
                {
                    var (lo, hi) = (reached, d);                                  // bisect the last step: eight halvings of 0.25" is under 0.001"
                    for (var i = 0; i < 8; i++)
                    {
                        var mid = (lo + hi) / 2;
                        if (Admits(body, container, within, origin + dir * mid, impassable, size)) lo = mid; else hi = mid;
                    }
                    reached = lo;
                    break;
                }
                reached = d;
                if (d >= reach) break;
            }
            boundary.Add(origin + dir * reached);
        }
        return boundary;
    }

    /// <summary>How many facings a non-circular base is tried at. p.7 lets a model take any facing, so a placement is legal
    /// if some yaw fits; the drawn region is the union over fitting yaws. Eight is 22.5° apart, which is
    /// finer than the region's own 2° rays are deep. A circular base is yaw-invariant and skips every one of them.</summary>
    public const int ExitYaws = 8;

    /// <summary>
    /// Is this position offered at any facing? Fully within the exit distance of the container's base, on the slab, and clear
    /// of the impassable volumes the Ask named. The last two are asked through Movement.Check with the candidate as both start
    /// and destination: the distance is zero, so Check samples exactly that placement and answers CrossesImpassable and
    /// LeavesTable for it — no new geometry primitive, and the same predicates the straight-line region uses.
    /// </summary>
    static bool Admits(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size) =>
        ExitYawAt(body, container, within, at, impassable, size) is not null;

    /// <summary>A facing this position is legal at, or null if none is. The panel asks for it when it builds the answer, so the
    /// placement it sends is one the server's own ExitPlacement will accept; a circular base answers at the first try.</summary>
    public static Yaw? ExitYawAt(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var yaws = body.Base.Shape == BaseShape.Circle ? 1 : ExitYaws;
        for (var i = 0; i < yaws; i++)
        {
            var yaw = new Yaw(360.0 * i / yaws);
            if (AdmitsAt(body, container, within, at, yaw, impassable, size)) return yaw;
        }
        return null;
    }

    static bool AdmitsAt(ModelBody body, PlacedModel container, double within, Point2 at, Yaw yaw, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var candidate = new PlacedModel(body, new Placement(new Point3(at.X, at.Y, container.Placement.Position.Z), yaw));
        if (!Containment.BaseFullyWithin(candidate, container, within)) return false;
        var check = Movement.Check(candidate, at, 0, impassable, [], null, size);
        return !check.CrossesImpassable && !check.LeavesTable;
    }
}
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Between the projection and the renderer. The projection has already applied everything; this only paces what the table plays out.</summary>
public sealed class AnimationQueue
{
    readonly Queue<GameEvent> _pending = new();
    public int Count => _pending.Count;
    public void Enqueue(IReadOnlyList<GameEvent> events) { foreach (var e in events) _pending.Enqueue(e); }

    /// <summary>Not animating: everything, at once. Animating: one event per call, so the table plays them out in order.</summary>
    public IReadOnlyList<GameEvent> Take(bool animate)
    {
        if (!animate) { var all = _pending.ToList(); _pending.Clear(); return all; }
        return _pending.Count == 0 ? [] : [_pending.Dequeue()];
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Client.Tests`; expect 45 passed. Commit: `Add the client view models: log text, the Ask view, the menu, the ruler with its disembark region, the animation queue`.**
