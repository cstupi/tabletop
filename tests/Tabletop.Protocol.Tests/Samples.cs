using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Protocol.Tests;

public static class Samples
{
    public static readonly Guid Team1 = Guid.CreateVersion7(), Team2 = Guid.CreateVersion7(), P1 = Guid.CreateVersion7();
    public static readonly Guid Frame = Guid.CreateVersion7(), Roll = Guid.CreateVersion7(), AskId = Guid.CreateVersion7();
    public static readonly Army Army = ArmyTests.Sample();
    public static Guid Unit => Army.Units[0].Id;
    public static Guid Model => Army.Units[0].Models[0].Id;
    public static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    public static ModelRelocation Deploy => new(Model, new OnShelf(), new OnTable(At(12, 12)));
    public static readonly Guid Container = Guid.CreateVersion7();
    /// <summary>A move action that ended inside a container (game flow → Inside a container); the reverse is the disembark.</summary>
    public static ModelRelocation Embark => new(Model, new OnTable(At(12, 12)), new Inside(Container));
    public static ModelRelocation Disembark => new(Model, new Inside(Container), new OnTable(At(14, 12)));
    public static GameInfo Game => new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, P1, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0x0123_4567_89AB_CDEF,
        [new Team(Team1, 1), new Team(Team2, 2)],
        [new Participant(P1, Guid.CreateVersion7(), "Clay", Team1, null)]);
    public static Board Board => new(TableStateTests.Sample(), [new ArmyInPlay(P1, ArmySnapshot.Of(Army, Guid.CreateVersion7()))]);
    public static RollRecord Hit => new(Roll, "hit", 4, [new RollModifier("AP", -1)], [new Die(6, 6, true), new Die(2, 2, false)]);
    public static readonly Marker Objective = new(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(1, 1, 0), new Yaw(0), 3, "smoke", VisualRef.OfBuiltin("objective-ring"));
    public static readonly Grant Grant = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Regeneration", Unit, 1);

    public static readonly LegalSet[] LegalSets =
    [
        new Options([new AskOption("a", "Advance", [Unit]), new AskOption("h", "Hold", [Unit], "Immobile: may only use Hold actions")]), new PieceSelection([Unit], 1, 2), new Allocation([Model], 3),
        new PieceSelection([Model], 1, 1),                                                                  // model grain: a Takedown's target (p.14)
        new Assignment([new AskOption("g1", "Rifles", [Model])], [Unit], 2),
        new Assignment([new AskOption("g1", "Rifles", [Model]), new AskOption("g2", "Pistols", [Model])], [Unit, Container], 2,
            new Dictionary<string, IReadOnlyList<Guid>> { ["g1"] = [Unit, Container], ["g2"] = [Unit] }),      // per-group targets (p.8)
        new MoveRegion([Model], 6, [Guid.CreateVersion7()], [Guid.CreateVersion7()], 6),
        new MoveRegion([Model], 12, [], [], 6, DangerousVolumeIds: [Guid.CreateVersion7()], UnstableVolumeIds: [Guid.CreateVersion7()], UnstableOver: 6),
        new MoveRegion([Model], 6, [], [], 6, new MoveContainment([Container], null, null)),                 // may embark
        new MoveRegion([Model], 6, [], [], 6, new MoveContainment([], Container, 6)),                        // is disembarking
        new TokenSpend(new Dictionary<Guid, int> { [Model] = 2 }), new Confirmation(),
    ];
    public static readonly AskAnswer[] Answers =
    [
        new Chosen("a"), new SelectedPieces([Unit]), new Allocated(new Dictionary<Guid, int> { [Model] = 3 }),
        new Assigned(new Dictionary<string, Guid> { ["g1"] = Unit }), new Destinations([Deploy]), new Destinations([Embark]), new Destinations([Disembark]),
        new TokensSpent(new Dictionary<Guid, int> { [Model] = 2 }), new Confirmed(), new Passed(),
    ];
    public static Ask Ask => new(AskId, AskQuestions.Activate, "Activate", [Team1], LegalSets[0], true, false, null, null);

    public static readonly EventPayload[] Events =
    [
        new RoundStarted(1, [Team1, Team2], [new ActionRecordCleared(Unit)]), new TurnStarted(Team1), new TurnEnded(Team1, true, []),
        new PhaseStarted("deployment"), new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Unit, [Unit], [], "Advance"),
            [new OutlineStep("move", "Move"), new OutlineStep("shoot", "Shoot")]),
        new StepCompleted(Frame, "roll-to-hit", new Chosen("a"), [Hit], [new WoundsSet(Model, 1)]) { Values = new Dictionary<string, int> { ["attacks"] = 2, ["hits"] = 1 } }, new SequenceFinished(Frame),
        new SequenceAbandoned(Frame), new AskOpened(Ask), new AskAnswered(AskId, Team1, null, false),
        new AskClosed(AskId, [new TeamAnswer(Team1, new TokensSpent(new Dictionary<Guid, int> { [Model] = 2 }))]),
        new DiceRerolled(Frame, Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]),
        new DiceEdited(Frame, Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
        new Undone(3),
        new ModelsMoved([Deploy]), new ModelsMoved([Embark]), new WoundsSet(Model, 2), new ConditionSet(new Subject(SubjectKind.Unit, Unit), "shaken", true),
        new ActionRecorded(Unit, "Advance"), new ActionRecordCleared(Unit), new GrantAdded(Unit, Grant), new GrantRemoved(Unit, Grant.Id),
        new SpellTokensSet(Model, 3), new WoundsThisActivationSet(Unit, 2), new ScoreSet(Team1, 4),
        new MarkerMoved(Objective.Id, new Point3(2, 2, 0), new Yaw(90)),
        new GameEnded("over", [Team1]),
        new RoundEnded(4, [new ObjectiveControlSet(Objective.Id, Team1)]), new ObjectiveControlSet(Objective.Id, null),
    ];
    public static readonly Command[] Commands =
    [
        new AnswerAsk(AskId, new Passed()), new AnswerAsk(AskId, new Confirmed(), Team2), new MoveModels([Deploy]), new MoveMarker(Objective.Id, new Point3(0, 0, 0), new Yaw(0)),
        new SetWounds(Model, 1), new SetCondition(new Subject(SubjectKind.Model, Model), "fatigued", false),
        new RecordAction(Unit, "Hold"), new ClearActionRecord(Unit), new AddGrant(Unit, Grant), new RemoveGrant(Unit, Grant.Id),
        new SetSpellTokens(Model, 6), new SetWoundsThisActivation(Unit, 0), new SetScore(Team2, 1), new SetActiveTeam(Team2), new SetRound(2),
        new Undo(3), new AbandonSequence(Frame), new SetDice(Frame, Roll, [new DieValue(0, 6)]),
        new SetObjectiveControl(Objective.Id, Team1),
    ];
    public static readonly Edit[] Edits =
    [
        new CreateTeam(), new PickTeam(Team1), new PickArmy(Army.Id), new PickArmy(Army.Id, Team2), new LeaveTeam(Team1), new SetConfiguration("Friday", "sha256:abc", SchedulerShape.PlayerTurn, Enforcement.Strict, true, new Dictionary<string, string> { ["melee_separation"] = "off" }),
        new StartSetup(), new EndSetup(), new SetTableSize(new TableSize(72, 48)), new SetTexture(VisualRef.OfBuiltin("grass")),
        new AddTerrain(TableStateTests.Sample().Terrain[0]), new MoveTerrain(Guid.CreateVersion7(), new Point3(1, 2, 0), new Yaw(0)),
        new ScaleTerrain(Guid.CreateVersion7(), 1.5), new RemoveTerrain(Guid.CreateVersion7()),
        new OverrideTerrainVolumes(Guid.CreateVersion7(), TableStateTests.Sample().Terrain[0].Volumes), new SwapTerrainMesh(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin")),
        new AddSetupMarker(Objective), new MoveSetupMarker(Objective.Id, new Point3(3, 3, 0), new Yaw(0)), new RemoveSetupMarker(Objective.Id),
        new SetZoneStyle("frontline", ["frontline/1", "frontline/2"]), new SwapZones(1, 2), new ReplaceDraft(TableStateTests.Sample()),
    ];
    public static readonly SocketMessage[] Messages =
    [
        new Connect("jwt", Guid.CreateVersion7(), ProtocolVersion.Current), new CommandMessage(Guid.CreateVersion7(), Commands[0]),
        new EditMessage(Guid.CreateVersion7(), Edits[0]), new Welcome(P1, Game), new GameInfoMessage(Game),
        new RulesOptionsMessage(new RulesOptionCatalog([new RulesOption("line_of_sight", "Line of sight method", [new RulesOptionValue("core", "Core line of sight")], "core")],
            [new RulesPreset("Core rules", new Dictionary<string, string>(), null)])),
        new BoardMessage(Board),
        new EventsMessage([new GameEvent(1, P1, Events[0], null), new GameEvent(2, null, Events[5], "outside the legal set")]),
        new PresenceMessage(new Presence(P1, new Cursor(new Point3(1, 2, 3), Model), new Selection(Unit, []), [new PresencePlacement(Model, new Point3(1, 1, 0), new Yaw(0), PlacementKind.Held)], new Measurement(new Point3(0, 0, 0), new Point3(6, 0, 0), MeasureMode.Straight))),
        new PingMessage(new Ping(P1, new Point3(1, 1, 0), null)), new PresenceEnded(P1), new AtRest(2),
        new Refused(Guid.CreateVersion7(), RefusalReason.OutsideLegalSet, "unit already activated"), new Rejected(RejectReason.UpgradeRequired),
        new SightQuery(Guid.CreateVersion7(), Unit, Guid.CreateVersion7()), new Reauth(Guid.CreateVersion7(), "jwt"),
        new SightAnswer(Guid.CreateVersion7(), [new SightLine(Model, Guid.CreateVersion7(), false, true, [Guid.CreateVersion7()])], [Model]),
        new ModelFacts(new Dictionary<Guid, int> { [Model] = 3 }),
        new ModelFacts(new Dictionary<Guid, int> { [Model] = 3 }, null, null, new Dictionary<Guid, UnitThreat> { [Unit] = new(12, 6, new Dictionary<Guid, double> { [Model] = 24 }) }),
    ];
}
