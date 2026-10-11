# Task 2: Hand moves under enforcement

**Wave:** 2 · **Depends on:** 1 · **Model:** opus

**Files:**
- Create: `server/game/Dispatch/HandMoves.cs`
- Modify: `server/game/Dispatch/CommandDispatcher.cs` (`Override` takes the actor and puts a `MoveModels` through
  `HandMoves.Judge` and `EnforcementPolicy.Decide`)
- Test: `tests/Tabletop.GameServer.Tests/HandMovesTests.cs` (new)
- Test: `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs` (`MoveModels` leaves the always-applied theory; a
  three-position test replaces it)
- Test: `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs` (`OntoTheObjective` appends the move as an event rather
  than dispatching it: its strict tests set up a position, they do not test hand moves)

**Interfaces:**
- Consumes: `Breach`, `StrictRefuses`, `EnforcementPolicy.Decide(Enforcement, Breach?)`, `Accept`, `Refuse`, `EndGame`
  (`server/game/Dispatch/EnforcementPolicy.cs`, unchanged); `EventApplier.EffectiveLog`; `AskQuestions.Deploy`,
  `DeployScouts`, `DeployAmbusher`.
- Produces: `public static class HandMoves { public const string Rule = "hand_move"; public static Breach? Judge(GameState state, MoveModels command, Actor actor); }`

Spec: *Server and protocol changes → Hand moves under enforcement*. A `MoveModels` is legal when every relocation
moves a model from the shelf to the table and the model belongs to a unit the actor's team picked in the last
`deploy`, `deploy_scouts` or `deploy_ambusher` answer, while that unit still has models on the shelf. That is the
state in which the scheduler waits: `GfRules.ActivationComplete` holds a `deploy` activation open while any model of
the unit is `OnShelf` (`TurnCycle` → `WaitingCompletion`), and `RoundBoundaryStep` returns `HookWaiting` while a picked
Ambusher has models on the shelf. Both wait without opening an Ask, so "the last pick" is read from the effective
log as: the newest `AskOpened` is a deploy-family scheduler Ask (`FrameId` null), and it has closed with the actor's
team answering `SelectedPieces`. Any later `AskOpened` ends the pick; this keeps a stale round-0 pick from licensing
a destroyed unit's return from the shelf in round 3.

Anything else is a breach: rule `hand_move`, detail `"{unit name} moved by hand outside a move step"` naming the
first offending model's unit, `StrictRefuses(RefusalReason.Illegal)`. `silent` accepts, `warn` accepts with the detail
as the event's warning, `strict` refuses. A relocation naming a model the projection lacks stays
`MenuTranslation`'s `Refused(Unknown)` under every setting, decided before the judge runs.

The callers this must keep legal under `strict`, which is every sim scenario's default
(`ScenarioBuilder._enforcement = Enforcement.Strict`):

- `sim/Run/RunLoop.cs` `PlaceAsync`: after answering a deploy-family Ask with `SelectedPieces`, one `MoveModels` of
  `HandPlacement.IntoZone`'s relocations, each `new ModelRelocation(slot.Id, new OnShelf(), new OnTable(...))`.
- `client/src/Panels/AskPanel.cs` (today) and the *Deployment placement* the spec moves to the decision box: one
  `MoveModels(DeployView.Layout(models, at, size))` of the picked unit's shelved models.
- `SchedulerHarness.Deploy` / `SkipDeployment` and `RoundBoundaryTests`' Ambusher placed one model per command.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/HandMovesTests.cs`:

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

/// <summary>A MoveModels is legal only as deployment placement of the unit the actor's team just picked (in-game UX spec → Hand moves under enforcement).</summary>
public class HandMovesTests
{
    /// <summary>A: two units of two models; B: one unit of two. A wins the roll-off (6 against 1), so A's deploy Ask is open.</summary>
    static SchedulerHarness AtDeployment(Enforcement enforcement)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 1, 2)], enforcement: enforcement);
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

    [Fact]
    public void Only_shelf_to_table_is_placement()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var first = h.P.Units[a[0]].Definition.Models[0].Id;
        var intoContainer = new MoveModels([new ModelRelocation(first, new OnShelf(), new Inside(Guid.CreateVersion7()))]);
        Assert.NotNull(HandMoves.Judge(h.State, intoContainer, TeamOf(h, 0)));
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
}
```

`SchedulerHarness.Dispatch` throws `InvalidOperationException($"{r.Reason}: {r.Detail}")` on a refusal, which is what
the strict arm reads. `Nudge` relies on `SkipDeployment` having put every model `OnTable`.

In `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs`, take `new MoveModels([Samples.Deploy]), ` out of
`Overrides()` and rename the theory it feeds:

```csharp
    public static IEnumerable<object[]> Overrides() =>
        new Command[] { new SetWounds(Samples.Model, 2), new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true),
            new RecordAction(Samples.Unit, "Advance"), new ClearActionRecord(Samples.Unit), new AddGrant(Samples.Unit, Samples.Grant), new RemoveGrant(Samples.Unit, Samples.Grant.Id),
            new SetSpellTokens(Samples.Model, 3), new SetWoundsThisActivation(Samples.Unit, 1), new SetScore(Samples.Team1, 5), new SetActiveTeam(Samples.Team2), new SetRound(2),
            new PlaceMarker(Samples.Token) }
        .SelectMany(c => new[] { Enforcement.Silent, Enforcement.Warn, Enforcement.Strict }.Select(e => new object[] { c, e }));

    [Theory, MemberData(nameof(Overrides))]
    public void Menu_edits_are_applied_under_every_setting_with_no_warning(Command command, Enforcement enforcement)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement), command, Watcher);
        var applied = Assert.IsType<Applied>(outcome);
        Assert.Single(applied.Events); Assert.Null(applied.Warning);
        var expectedKind = command switch { SetActiveTeam => typeof(TurnStarted), SetRound => typeof(RoundStarted), PlaceMarker => typeof(MarkerPlaced), _ => null };
        if (expectedKind is not null) Assert.IsType(expectedKind, applied.Events[0]);
        else Assert.IsAssignableFrom<StateChange>(applied.Events[0]);
    }

    [Theory]
    [InlineData(Enforcement.Silent, true, false)] [InlineData(Enforcement.Warn, true, true)] [InlineData(Enforcement.Strict, false, false)]
    public void A_hand_move_with_no_deploy_pick_follows_the_enforcement_setting(Enforcement enforcement, bool applied, bool warned)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement), new MoveModels([Samples.Deploy]), Clay);
        if (!applied) { Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(outcome).Reason); return; }
        var ok = Assert.IsType<Applied>(outcome);
        Assert.IsType<ModelsMoved>(Assert.Single(ok.Events));
        Assert.Equal(warned, ok.Warning is not null);
    }

    [Fact]
    public void A_hand_move_naming_no_model_is_unknown_under_every_setting()
    {
        var ghost = new MoveModels([new ModelRelocation(Guid.CreateVersion7(), new OnShelf(), Samples.Deploy.To)]);
        foreach (var e in Enum.GetValues<Enforcement>())
            Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(e), ghost, Clay)).Reason);
    }
```

In `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`, replace `OntoTheObjective` so the position is set up without
the dispatcher (two of its callers run under `strict`, where a mid-round hand move is now refused):

```csharp
    /// <summary>Every model of team 0's unit onto the setup objective's edge (36, 26.5): 2.5" from its centre, well inside 3" of its edge.
    /// Appended, not dispatched: under strict a hand move mid-round is refused, and these tests are about seizing, not moving.</summary>
    static void OntoTheObjective(SchedulerHarness h)
    {
        var unit = h.Units(0)[0];
        h.Append([new ModelsMoved(h.P.Units[unit].Definition.Models.Select(m =>
            new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnTable(new Placement(new Point3(36, 26.5, 0), new Yaw(0))))).ToList())]);
        h.Run();
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "HandMovesTests|CommandDispatcherTests|ObjectivesTests"`
Expected: compile error: `HandMoves` does not exist. With a stub `Judge` returning `null` the three-position tests
fail: strict applies the move instead of refusing it.

- [ ] **Step 3: Write the judge**

Create `server/game/Dispatch/HandMoves.cs`:

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// A hand move (MoveModels) under the enforcement setting (in-game UX spec → Hand moves under enforcement). Legal only as deployment
/// placement: every relocation takes a model from the shelf to the table, and the model's unit is one the actor's team picked in the
/// deploy-family Ask the scheduler is waiting on. The scheduler waits for placement without opening another Ask (a deploy activation is
/// incomplete while a model is on the shelf; the Ambusher boundary step waits likewise), so the pick is live exactly while the newest
/// AskOpened in the effective log is that deploy Ask and it has closed. Everything else is the hand_move breach, which strict refuses.
/// </summary>
public static class HandMoves
{
    public const string Rule = "hand_move";
    static readonly HashSet<string> DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    /// <summary>Null when legal. A model the projection lacks is skipped: MenuTranslation has already refused it as Unknown.</summary>
    public static Breach? Judge(GameState state, MoveModels command, Actor actor)
    {
        var p = state.Projection;
        var placing = actor.TeamId is { } team ? Placing(state, team) : new HashSet<Guid>();
        foreach (var move in command.Moves)
        {
            if (!p.Models.TryGetValue(move.ModelId, out var model)) continue;
            if (model.Location is OnShelf && move.To is OnTable && placing.Contains(model.UnitId)) continue;
            return new Breach(Rule, $"{p.Units[model.UnitId].Definition.Name} moved by hand outside a move step", new StrictRefuses(RefusalReason.Illegal));
        }
        return null;
    }

    /// <summary>The units the team picked in the deploy-family Ask the scheduler waits on; empty when any Ask has opened since, or none was answered.</summary>
    static HashSet<Guid> Placing(GameState state, Guid team)
    {
        var deployAsks = new HashSet<Guid>();
        var picked = new HashSet<Guid>();
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log))
            switch (e.Payload)
            {
                case AskOpened o:
                    picked = [];
                    if (o.Ask.FrameId is null && DeployQuestions.Contains(o.Ask.Question)) deployAsks.Add(o.Ask.Id);
                    break;
                case AskClosed c when deployAsks.Contains(c.AskId):
                    picked = c.Answers.Where(a => a.TeamId == team).Select(a => a.Answer).OfType<SelectedPieces>().SelectMany(s => s.PieceIds).ToHashSet();
                    break;
            }
        return picked;
    }
}
```

`SelectedPieces.PieceIds` may name several units under `warn`/`silent` (an out-of-set answer is accepted there); each
of them is then being placed.

- [ ] **Step 4: Put the hand move through the policy**

In `server/game/Dispatch/CommandDispatcher.cs`, change the dispatch arm

```csharp
        CommandClass.Override => Override(state, command),
```

to

```csharp
        CommandClass.Override => Override(state, command, actor),
```

and replace `Override` with:

```csharp
    /// <summary>
    /// The menu's one event. A set_round carries the round-start changes the scheduler would write, so a round started by hand is a whole round
    /// (enforcement spec → A menu round start). A hand move is judged and decided under the enforcement setting (in-game UX spec → Hand moves
    /// under enforcement); every other menu edit is applied under every setting.
    /// </summary>
    private DispatchOutcome Override(GameState state, Command command, Actor actor)
    {
        if (MenuTranslation.Translate(state, command, out var missing) is not { } payload)
            return new Refused(RefusalReason.Unknown, missing ?? "unknown command");
        if (payload is RoundStarted r) payload = r with { Changes = engine.RoundStartChanges(state, r.Round) };
        var verdict = command is MoveModels move ? EnforcementPolicy.Decide(state.Info.Enforcement, HandMoves.Judge(state, move, actor)) : new Accept(null);
        switch (verdict)
        {
            case Refuse refuse: return new Refused(refuse.Reason, refuse.Detail);
            case EndGame: throw new UnreachableException("a hand move's breach is always StrictRefuses");
        }
        return new Applied([payload], ((Accept)verdict).Warning);
    }
```

`server/game/Dispatch/MenuTranslation.cs` is unchanged: it still checks references only, before the judge runs.

- [ ] **Step 5: Run them to see them pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "HandMovesTests|CommandDispatcherTests|ObjectivesTests"`
Expected: PASS.

- [ ] **Step 6: Run the whole server suite and the simulator under strict**

Every sim scenario defaults to `strict` (`ScenarioBuilder`), so they exercise the deployment path end to end. The
`final-round` scenario is the Review Focus case: strict, deploys both armies through `HandPlacement`, plays to the
round-4 end.

```bash
docker compose up -d
dotnet test tests/Tabletop.GameServer.Tests
dotnet run --project sim -- final-round --seeds 1..5 --no-recording
dotnet run --project sim -- skirmish --seeds 1..5 --no-recording
dotnet run --project sim -- army-rules --no-recording
dotnet test tests/Tabletop.Sim.Tests
```

Expected: every test passes; each sim run ends `Ended` (final-round) or as its expectations say, with no
`Stuck(SubmitRefused, "hand placement of unit …")`; exit code 0. A `hand placement` refusal means the judge has
missed a deploy path: read the run's last Ask before changing the judge. No catalog scenario fields Scouts or
Ambush; `Strict_places_Scouts_after_deployment` and `Strict_places_an_Ambusher_at_the_round_boundary` cover them.

- [ ] **Step 7: Commit**

```bash
git add server/game/Dispatch/HandMoves.cs server/game/Dispatch/CommandDispatcher.cs \
  tests/Tabletop.GameServer.Tests/HandMovesTests.cs tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs tests/Tabletop.GameServer.Tests/ObjectivesTests.cs
git commit -m "Judge hand moves under the enforcement setting: placement of the picked unit is legal, anything else is a hand_move breach" \
  -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
