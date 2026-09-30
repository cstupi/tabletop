# Task 2: `IGameRules` gains the scheduler's hooks; `GrimdarkFutureRules` implements them

**Files:**
- Modify: `server/game/Rules/IGameRules.cs` (add the scheduler's members beside the interpreter plan's)
- Read, do not modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` — the interpreter plan's Task 11 declares every name this plan uses; land it first
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Scheduling.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Deployment.cs`, `server/game/Scheduler/TeamUnits.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestArmies.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/TestGames.cs`, `tests/Tabletop.GameServer.Tests/GrimdarkFutureRulesTests.cs`

**Interfaces:**
- Consumes: Task 1's types, `SchedulerLog`, `ActivationOption`; `UnitInPlay`, `ModelInPlay`, `Location`, `OnShelf`, `PropertyInstance`, `Grant` (Protocol/Content); the skeleton's `GrimdarkFutureRules` (`sealed partial`, `Rules/GrimdarkFuture/GfRules.cs`); the interpreter plan's `Effects.Lookup.UnitHas(GameState, PropertySet, Guid unitId, string entry)` (Task 6 — own properties, aggregated model properties, and the grants store), `PropertySet`, `TestProperties.Core`, and its three hooks `OnRoundStarted`/`EndOfTurnSequences`/`OnTurnEnded` with their `GfHooks` bodies (Task 11) — this plan implements none of those three.
- Produces: `IGameRules` members `IReadOnlyList<string> PhasesBeforeRounds { get; }`, `IReadOnlyList<string> PhasesInTurn { get; }`, `FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams)`, `TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId)`, `Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer)`, `bool ActivationComplete(GameState state, Activation activation)`, `HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order)`, `IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams)` — and no others: what this plan first drafted as `RoundStartChanges`, `EndOfTurn` and `TurnEndChanges` is the interpreter plan's `OnRoundStarted(GameState, PropertySet, int round)`, `EndOfTurnSequences(GameState, PropertySet, TurnContext)` and `OnTurnEnded(GameState, PropertySet, TurnContext)`, one body each in `GfHooks` (the merged interface is written in full in the skeleton plan → Interfaces → Produces); `GrimdarkFutureRules` partials `GfRules.Scheduling.cs` and `GfRules.Deployment.cs` (no `RulesId`, `Instance` or `Id` — the skeleton's `GfRules.cs` has them) with `internal static IReadOnlyList<string> ActionsFor(GameState, PropertySet, UnitInPlay, IReadOnlyList<string> rows)` and `internal static IReadOnlyList<Guid> Ambushers(GameState, PropertySet, Guid teamId)` (this task adds nothing to `GfNames`: `Deployment`, `Scouts`, `Rounds`, `Deploy`, `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `SetAside` are declared there by the interpreter plan's Task 11, beside `Hold`, `Advance`, `Rush`, `Charge`, `Shaken`, `Fatigued`, `MoraleTest`, `Melee`, `RollOffSeq`, `TokenCap` — redeclaring any of them is a duplicate, not a gap); `static class TeamUnits { Guid? TeamOf(GameState, Guid participantId); IReadOnlyList<UnitInPlay> Of(GameState, Guid teamId); bool AllShelved(GameState, UnitInPlay); bool AnyOnTable(GameState, UnitInPlay); IReadOnlyList<ModelInPlay> ModelsOf(GameState, UnitInPlay); int IndexOf(IReadOnlyList<Guid> order, Guid team); }`. Test infrastructure `TestArmies.Build(string name, int units, int modelsPerUnit, (int Unit, string Rule, int? Parameter)[]? unitRules = null, (int Unit, string Rule, int? Parameter)[]? modelRules = null)`; `TestGames.Build(IReadOnlyList<Army?> armyPerTeam, SchedulerShape shape = AlternatingActivation, Enforcement enforcement = Warn, bool waits = false, int spectators = 0, IGameRules? rules = null)` returning `TestGames.Built(GameState State, GameInfo Info, IReadOnlyList<Guid> Teams, IReadOnlyList<Guid> Participants)`; `TestGames.Apply(Built, params EventPayload[])`; `TestGames.Relocations(GameState, Guid unitId)` and, over it, `TestGames.Place` (the `ModelsMoved` event, for `Apply`/`Append`) and `TestGames.Move` (the identical `MoveModels` command, for `Dispatch`) putting every model of the unit on the table.

Spec: scheduler → The line (the right-hand column is this interface), The turn is an Ask (legal set = activatable units × actions; `Immobile` means Hold only; Shaken means idle), Deployment (shelf minus set-aside; Scouts after every other unit), The round boundary (fatigue, spell tokens, Ambushers "starting with the player that activates next", nothing written to the record), What the interface must not assume 1 (first team is a policy: finished-first for two teams, roll-off every round for free-for-all — page 50 "including the first"), 6 (reaction ordering replaceable). Action sequences → End-of-activation hook (half or less of starting size or Tough; melee units compare instead), Fatigue (cleared at the round boundary), The token economy (+X, capped at 6, even off the table). Rulebook page 10: Shaken units "must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation"; page 13 `Aircraft` "May only use Advance actions", `Ambush`; page 14 `Immobile`, `Scout`. Set-aside is a condition on the shelved unit (scheduler → Deployment), written from the menu with `SetCondition` like any other property (game flow → Nothing is an invariant); no phase Asks for it.

- [ ] **Step 1: Write the test infrastructure `TestArmies.cs` and `TestGames.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestArmies
{
    /// <summary>
    /// units × modelsPerUnit, fresh ids. unitRules go on the unit; modelRules on every model of that unit (1-based unit numbers).
    /// Put a rule on the side its catalog entry is declared for, or Lookup.UnitHas silently answers false: in TestProperties.Core
    /// Aircraft, Scout and Ambush are kind unit (no aggregation, so the model path can never match), Immobile is kind model with
    /// aggregation all, and Caster and Tough are read per model by Lookup.ModelParameter — so the first four go in unitRules and the
    /// last two in modelRules, which is what every fixture below does.
    /// </summary>
    public static Army Build(string name, int units, int modelsPerUnit,
        (int Unit, string Rule, int? Parameter)[]? unitRules = null, (int Unit, string Rule, int? Parameter)[]? modelRules = null)
    {
        var list = new List<Unit>();
        for (var u = 1; u <= units; u++)
        {
            var mr = (modelRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList();
            var models = Enumerable.Range(1, modelsPerUnit).Select(m => new ModelSlot(Guid.CreateVersion7(), m, $"{name} {u}.{m}",
                new Dictionary<string, string>(), mr, Base.Circle(1.26), 1.5, null, [])).ToList();
            list.Add(new Unit(Guid.CreateVersion7(), u, $"{name} unit {u}", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" },
                (unitRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList(), models));
        }
        return new Army(Guid.CreateVersion7(), name, "grimdark-future", "test", list);
    }
}
```

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestGames
{
    public sealed record Built(GameState State, GameInfo Info, IReadOnlyList<Guid> Teams, IReadOnlyList<Guid> Participants);

    /// <summary>One team per entry, one participant each; a null army is a team that brought nothing. Spectators are participants with no team.</summary>
    public static Built Build(IReadOnlyList<Army?> armyPerTeam, SchedulerShape shape = SchedulerShape.AlternatingActivation,
        Enforcement enforcement = Enforcement.Warn, bool waits = false, int spectators = 0, IGameRules? rules = null, long rngSeed = 7)
    {
        var teams = armyPerTeam.Select((_, i) => new Team(Guid.CreateVersion7(), i + 1)).ToList();
        var participants = teams.Select((t, i) => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Player {i + 1}", t.Id, armyPerTeam[i]?.Id)).ToList();
        participants.AddRange(Enumerable.Range(0, spectators).Select(i => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Watcher {i}", null, null)));
        var info = new GameInfo(Guid.CreateVersion7(), "Test", GameStatus.Playing, participants[0].AccountId, GrimdarkFutureRules.RulesId, "sha256:test",
            shape, enforcement, waits, rngSeed, teams, participants);                        // RngSeed is positional (contracts plan): a build that forgot it would not compile
        var armies = participants.Zip(armyPerTeam, (p, a) => (p, a)).Where(x => x.a is not null)
            .Select(x => new ArmyInPlay(x.p.Id, ArmySnapshot.Of(x.a!, Guid.CreateVersion7()))).ToList();
        var board = new Board(TableStateTests.Sample(), armies);
        var state = new GameState(info, board, new EventApplier(board, info), rules ?? GrimdarkFutureRules.Instance);
        return new Built(state, info, teams.Select(t => t.Id).ToList(), participants.Select(p => p.Id).ToList());
    }

    public static void Apply(Built game, params EventPayload[] payloads)
    {
        foreach (var p in payloads) game.State.Applier.Apply(new GameEvent(game.State.Projection.LastSeq + 1, null, p, null));
    }

    /// <summary>Every model of the unit onto the table, in a row. Place is the event (for Apply/Append); Move is the identical command (for Dispatch).</summary>
    public static IReadOnlyList<ModelRelocation> Relocations(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(6 + i * 2, 6, 0), new Yaw(0))))).ToList();

    public static ModelsMoved Place(GameState state, Guid unitId) => new(Relocations(state, unitId));
    public static MoveModels Move(GameState state, Guid unitId) => new(Relocations(state, unitId));
}
```

- [ ] **Step 2: Write the failing tests `GrimdarkFutureRulesTests.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class GrimdarkFutureRulesTests
{
    static readonly GrimdarkFutureRules Rules = GrimdarkFutureRules.Instance;
    static readonly PropertySet Core = TestProperties.Core;                                    // its action rows: Hold, Advance, Rush, Charge
    static readonly string[] Rows = ["Hold", "Advance", "Rush", "Charge"];
    static Subject UnitSubject(Guid id) => new(SubjectKind.Unit, id);

    /// <summary>Team 1: three units of three (unit 2 is a Scout, unit 3 an Ambusher); team 2: two units, the second Immobile with one Tough(6) Caster(2) model.</summary>
    static TestGames.Built Game(SchedulerShape shape = SchedulerShape.AlternatingActivation) => TestGames.Build([
        TestArmies.Build("A", 3, 3, unitRules: [(2, Scout, null), (3, Ambush, null)]),
        TestArmies.Build("B", 2, 1, unitRules: [(2, Immobile, null)], modelRules: [(2, Tough, 6), (2, Caster, 2)])], shape);
    static IReadOnlyList<Guid> Units(TestGames.Built g, int team) => TeamUnits.Of(g.State, g.Teams[team]).Select(u => u.Id).ToList();

    [Fact]
    public void Deployment_offers_the_shelf_minus_set_aside_and_scouts_offers_the_set_aside_scouts()
    {
        var g = Game(); var a = Units(g, 0);
        TestGames.Apply(g, new PhaseStarted(Deployment), new ConditionSet(UnitSubject(a[1]), SetAside, true), new ConditionSet(UnitSubject(a[2]), SetAside, true));
        var ask = Rules.LegalTurn(g.State, Core, g.Teams[0])!;
        Assert.Equal((AskQuestions.Deploy, false), (ask.Question, ask.AllowPass));
        Assert.Equal([a[0]], Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        TestGames.Apply(g, TestGames.Place(g.State, a[0]));
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[0]));                               // nothing left but set-aside units
        TestGames.Apply(g, new PhaseStarted(Scouts));
        var scouts = Rules.LegalTurn(g.State, Core, g.Teams[0])!;
        Assert.Equal(AskQuestions.DeployScouts, scouts.Question);
        Assert.Equal([a[1]], Assert.IsType<PieceSelection>(scouts.Legal).Eligible);            // the Ambusher waits for the round boundary
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[1]));                               // team 2 has no scouts
    }

    [Fact]
    public void Deploy_answers_read_a_unit_and_complete_when_every_model_is_placed()
    {
        var g = Game(); var a = Units(g, 0);
        TestGames.Apply(g, new PhaseStarted(Deployment));
        var act = Rules.ReadAnswer(g.State, g.Teams[0], new SelectedPieces([a[0]]))!;
        Assert.Equal((a[0], Deploy, false, 0), (act.UnitId, act.Action, act.RunsSequence, act.Changes.Count));
        Assert.False(Rules.ActivationComplete(g.State, act));
        var models = g.State.Projection.Units[a[0]].Definition.Models;
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(models[0].Id, new OnShelf(), new OnTable(new(new(1, 1, 0), new(0))))]));
        Assert.False(Rules.ActivationComplete(g.State, act));
        TestGames.Apply(g, TestGames.Place(g.State, a[0]));
        Assert.True(Rules.ActivationComplete(g.State, act));
        Assert.Null(Rules.ReadAnswer(g.State, g.Teams[0], new SelectedPieces([Guid.CreateVersion7()])));
    }

    [Fact]
    public void Activation_offers_units_on_the_table_with_no_record_and_the_actions_their_rules_allow()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        TestGames.Apply(g, new PhaseStarted(Rounds), TestGames.Place(g.State, a[0]), TestGames.Place(g.State, a[1]), TestGames.Place(g.State, b[0]), TestGames.Place(g.State, b[1]),
            new ActionRecorded(a[1], "Advance"), new ConditionSet(UnitSubject(b[0]), Shaken, true));
        var teamA = Assert.IsType<Options>(Rules.LegalTurn(g.State, Core, g.Teams[0])!.Legal).Choices;
        Assert.Equal(Rows.Select(r => ActivationOption.Id(a[0], r)), teamA.Select(c => c.Id));                 // a[1] activated, a[2] shelved
        var teamB = Assert.IsType<Options>(Rules.LegalTurn(g.State, Core, g.Teams[1])!.Legal).Choices;
        Assert.Equal([ActivationOption.Id(b[0], ActivationOption.Idle), ActivationOption.Id(b[1], "Hold")], teamB.Select(c => c.Id));  // Shaken idles; Immobile holds
        TestGames.Apply(g, new ActionRecorded(a[0], "Hold"));
        Assert.Null(Rules.LegalTurn(g.State, Core, g.Teams[0]));
        var act = Rules.ReadAnswer(g.State, g.Teams[1], new Chosen(ActivationOption.Id(b[1], "Hold")))!;
        Assert.Equal((b[1], "Hold", true), (act.UnitId, act.Action, act.RunsSequence));
        Assert.Equal(new ActionRecorded(b[1], "Hold"), act.Changes.Single());
        var idle = Rules.ReadAnswer(g.State, g.Teams[1], new Chosen(ActivationOption.Id(b[0], ActivationOption.Idle)))!;
        Assert.False(idle.RunsSequence); Assert.True(Rules.ActivationComplete(g.State, idle));
        Assert.Null(Rules.ReadAnswer(g.State, g.Teams[1], new Chosen("garbage")));
    }

    [Fact]
    public void Aircraft_may_only_advance_even_when_shaken()
    {
        var g = TestGames.Build([TestArmies.Build("A", 1, 1, unitRules: [(1, Aircraft, null)])]);
        var unit = g.State.Projection.Units.Values.Single();
        Assert.Equal(["Advance"], GrimdarkFutureRules.ActionsFor(g.State, Core, unit, Rows));
        TestGames.Apply(g, new ConditionSet(UnitSubject(unit.Id), Shaken, true));
        Assert.Equal(["Advance"], GrimdarkFutureRules.ActionsFor(g.State, Core, unit, Rows));      // p.13: "even if Shaken"
    }

    [Fact]
    public void First_team_is_a_roll_off_before_round_one_then_the_winner_then_whoever_finished_first()
    {
        var g = Game(); var teams = g.Teams;
        Assert.Equal(RollOffSeq, Assert.IsType<RollOffNeeded>(Rules.FirstTeam(g.State, 0, teams)).Sequence);
        TestGames.Apply(g, new RoundStarted(0, [teams[1], teams[0]], []));
        Assert.Equal([teams[1], teams[0]], Assert.IsType<OrderedTeams>(Rules.FirstTeam(g.State, 1, teams)).Order);
        TestGames.Apply(g, new RoundStarted(1, [teams[1], teams[0]], []),
            new TurnStarted(teams[1]), new TurnEnded(teams[1], false, []), new TurnStarted(teams[0]), new TurnEnded(teams[0], false, []),
            new TurnStarted(teams[1]), new TurnEnded(teams[1], false, []));                       // team 1 activated last, so team 0 finished first
        Assert.Equal([teams[0], teams[1]], Assert.IsType<OrderedTeams>(Rules.FirstTeam(g.State, 2, teams)).Order);
        var three = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)]);
        TestGames.Apply(three, new RoundStarted(0, three.Teams, []));
        Assert.IsType<RollOffNeeded>(Rules.FirstTeam(three.State, 0, three.Teams));                // p.6 and p.50: deployment order is a roll-off
        Assert.IsType<OrderedTeams>(Rules.FirstTeam(three.State, 1, three.Teams));                 // p.50: three players order like two, no round roll-off
        Assert.IsType<OrderedTeams>(Rules.FirstTeam(three.State, 2, three.Teams));
    }

    [Fact]
    public void Round_start_clears_fatigue_and_grants_capped_spell_tokens()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        var caster = g.State.Projection.Units[b[1]].Definition.Models[0].Id;
        TestGames.Apply(g, new ConditionSet(UnitSubject(a[0]), Fatigued, true), new SpellTokensSet(caster, 5));
        Assert.Empty(Rules.OnRoundStarted(g.State, Core, 0));                                           // the interpreter plan's hook, called by SchedulerBase.BeginRound
        var changes = Rules.OnRoundStarted(g.State, Core, 2);
        Assert.Contains(new ConditionSet(UnitSubject(a[0]), Fatigued, false), changes);
        Assert.Contains(new SpellTokensSet(caster, 6), changes);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void End_of_turn_tests_morale_at_half_strength_except_after_melee_and_turn_end_resets_the_counter()
    {
        var g = Game(); var a = Units(g, 0); var b = Units(g, 1);
        var models = g.State.Projection.Units[a[0]].Definition.Models;
        TestGames.Apply(g, new TurnStarted(g.Teams[1]), TestGames.Place(g.State, a[0]), new WoundsThisActivationSet(a[0], 1),
            new ModelsMoved([new ModelRelocation(models[0].Id, new OnTable(new(new(1, 1, 0), new(0))), new OnShelf())]));
        var turn = new TurnContext(g.Teams[1], new Activation(b[0], "Hold", true, []), 1, 1);
        Assert.Empty(Rules.EndOfTurnSequences(g.State, Core, turn));                                    // 2 of 3 left: over half (the interpreter plan's hook)
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(models[1].Id, new OnTable(new(new(3, 1, 0), new(0))), new OnShelf())]));
        var started = Rules.EndOfTurnSequences(g.State, Core, turn);
        Assert.Equal((MoraleTest, a[0]), (started.Single().Sequence, started.Single().Context.ActorUnitId));
        TestGames.Apply(g, new SequenceStarted(Guid.CreateVersion7(), Melee, new SequenceContext(b[0], [a[0]], [], null)));
        Assert.Empty(Rules.EndOfTurnSequences(g.State, Core, turn));                                    // fought this melee: the melee compares wounds instead
        var tough = g.State.Projection.Units[b[1]].Definition.Models[0].Id;
        TestGames.Apply(g, new TurnStarted(g.Teams[0]), TestGames.Place(g.State, b[1]), new WoundsThisActivationSet(b[1], 3), new WoundsSet(tough, 3));
        Assert.Single(Rules.EndOfTurnSequences(g.State, Core, new TurnContext(g.Teams[0], null, 1, 1)));   // Tough(6) at 3 wounds: half
        var reset = Rules.OnTurnEnded(g.State, Core, new TurnContext(g.Teams[0], null, 1, 1));
        Assert.Equal([new WoundsThisActivationSet(a[0], 0), new WoundsThisActivationSet(b[1], 0)], reset.OrderBy(c => ((WoundsThisActivationSet)c).UnitId == b[1]).ToList());
        TestGames.Apply(g, new ConditionSet(UnitSubject(b[0]), Shaken, true));
        var idle = new TurnContext(g.Teams[1], new Activation(b[0], ActivationOption.Idle, false, []), 1, 1);
        Assert.Contains(new ConditionSet(UnitSubject(b[0]), Shaken, false), Rules.OnTurnEnded(g.State, Core, idle));
    }

    [Fact]
    public void Ambushers_are_asked_from_round_two_starting_with_the_first_team_alternating_until_all_pass()
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 1, unitRules: [(1, Ambush, null), (2, Ambush, null)]), TestArmies.Build("B", 1, 1, unitRules: [(1, Ambush, null)])]);
        var a = Units(g, 0); var b = Units(g, 1); var order = new[] { g.Teams[1], g.Teams[0] };
        TestGames.Apply(g, new ConditionSet(UnitSubject(a[0]), SetAside, true), new ConditionSet(UnitSubject(a[1]), SetAside, true), new ConditionSet(UnitSubject(b[0]), SetAside, true));
        Assert.IsType<HookDone>(Rules.RoundBoundaryStep(g.State, Core, 1, order));
        TestGames.Apply(g, new RoundStarted(2, order, []));
        var first = Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order));
        var ask = Assert.IsType<AskOpened>(first.Events.Single()).Ask;
        Assert.Equal((g.Teams[1], AskQuestions.DeployAmbusher, true), (ask.Teams.Single(), ask.Question, ask.AllowPass));
        Assert.Equal([b[0]], Assert.IsType<PieceSelection>(ask.Legal).Eligible);
        TestGames.Apply(g, first.Events.Single());
        Assert.IsType<HookWaiting>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                    // open
        TestGames.Apply(g, new AskClosed(ask.Id, [new TeamAnswer(g.Teams[1], new SelectedPieces([b[0]]))]));
        Assert.IsType<HookWaiting>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                    // models still on the shelf
        TestGames.Apply(g, TestGames.Place(g.State, b[0]));
        var second = Assert.IsType<AskOpened>(Assert.IsType<HookEmit>(Rules.RoundBoundaryStep(g.State, Core, 2, order)).Events.Single()).Ask;
        Assert.Equal([g.Teams[0]], second.Teams);
        TestGames.Apply(g, new AskOpened(second), new AskClosed(second.Id, [new TeamAnswer(g.Teams[0], new Passed())]));
        Assert.IsType<HookDone>(Rules.RoundBoundaryStep(g.State, Core, 2, order));                       // team 1 has none left, team 0 passed
        Assert.Empty(g.State.Projection.Units[b[0]].Actions);                                      // deploying wrote nothing to the record
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GrimdarkFutureRules`; expect compile errors — `IGameRules` has none of these members. Extend `IGameRules.cs`** — add `using Tabletop.GameServer.Scheduler;` and these members to the interface body (the interpreter plan's Task 8 adds its block the same way; the skeleton's `Id`, `UnknownGameRulesException` and `GameRulesRegistry` stay as they are):

```csharp
    // Added by the scheduler plan. Every hook reads state.Projection and the log and returns; none holds state.

    /// <summary>Phases before round one, in order; each alternates one unit a turn. OPR: deployment, scouts. Empty: rounds start at once.</summary>
    IReadOnlyList<string> PhasesBeforeRounds { get; }
    /// <summary>Phases inside one team's turn under the player-turn shape, in order. Empty means one unnamed phase (OPR).</summary>
    IReadOnlyList<string> PhasesInTurn { get; }
    /// <summary>Who goes first in <paramref name="round"/> (0 is the phase before round one). Teams are in ordinal order. RollOffNeeded names the ruleset sequence that decides.</summary>
    FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams);
    /// <summary>The team's turn Ask now, read from the phase in state.Projection.Scheduler.Phase; the action rows are properties.Actions; null when its legal set is empty.</summary>
    TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId);
    /// <summary>What a turn answer means; null when it names nothing (treated as a pass).</summary>
    Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer);
    /// <summary>For an activation with no sequence: is its work done (deploy: every model placed).</summary>
    bool ActivationComplete(GameState state, Activation activation);
    /// <summary>The interactive round-boundary step, called each Advance after round_started until HookDone (OPR: Ambushers).</summary>
    HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order);
    /// <summary>Order for simultaneous reactions, or null for Windows.ReactionOrder's default: turn order starting after the actor, passed teams dropped.</summary>
    IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams);
```

What rides on `round_started` and `turn_ended`, and what runs at the end of an activation, are the interpreter plan's `OnRoundStarted(state, properties, round)`, `OnTurnEnded(state, properties, turn)` and `EndOfTurnSequences(state, properties, turn)` — the scheduler calls those three and adds nothing of its own beside them.

- [ ] **Step 4: Check `GfNames.cs` already carries the scheduler's names; write `TeamUnits.cs`**

Nothing is added to `GfNames`. The interpreter plan's Task 11 declares the whole block this plan reads — confirm these lines are in `server/game/Rules/GrimdarkFuture/GfNames.cs` before writing anything that uses them, and if that plan's Task 11 has not landed, land it first rather than declaring a second names class:

```csharp
    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts", Rounds = Scheduler.SchedulerBase.RoundsPhase;   // phases
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough";
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Which units are a team's: through the participant that brought them. A participant with no team owns units no team may use.</summary>
public static class TeamUnits
{
    public static Guid? TeamOf(GameState state, Guid participantId) => state.Info.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
    public static IReadOnlyList<UnitInPlay> Of(GameState state, Guid teamId) =>
        state.Projection.Units.Values.Where(u => TeamOf(state, u.ParticipantId) == teamId).OrderBy(u => u.Definition.Ordinal).ToList();
    public static IReadOnlyList<ModelInPlay> ModelsOf(GameState state, UnitInPlay unit) => unit.Definition.Models.Select(m => state.Projection.Models[m.Id]).ToList();
    public static bool AllShelved(GameState state, UnitInPlay unit) => ModelsOf(state, unit).All(m => m.Location is OnShelf);
    public static bool AnyOnTable(GameState state, UnitInPlay unit) => ModelsOf(state, unit).Any(m => m.Location is not OnShelf);
    public static int IndexOf(IReadOnlyList<Guid> order, Guid team) { for (var i = 0; i < order.Count; i++) if (order[i] == team) return i; return -1; }
}
```

`GfNames.Rounds` is `SchedulerBase.RoundsPhase` so the two spellings cannot drift, which makes `GfNames.cs` depend on this plan's Task 5: land Task 5's `SchedulerBase.cs` constant first (it has no other dependency), or leave `GfNames.Rounds` as the literal `"rounds"` until it does. `GrimdarkFutureRules` itself is the skeleton's `sealed partial class` in `GfRules.cs`; the skeleton's tests already import `Tabletop.GameServer.Rules.GrimdarkFuture`.

- [ ] **Step 5: Write `GfRules.Scheduling.cs`**

Name checks go through the interpreter plan's `Lookup.UnitHas(state, properties, unitId, entry)` (own properties, aggregated model properties, the grants store — whole-design review → name checks read the grants store) rather than a second reading of the unit here, so `LegalTurn` and `RoundBoundaryStep` take the pinned `PropertySet` as the interpreter plan's hooks do; the action rows are `properties.Actions` (`ActionDefinition.Name`, catalog order: `Hold`, `Advance`, `Rush`, `Charge`).

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The scheduler plan's partial: phases, first team, the turn's legal set and how an answer is read. Every member is a pure reading of GameState.</summary>
public sealed partial class GrimdarkFutureRules
{
    public IReadOnlyList<string> PhasesBeforeRounds { get; } = [Deployment, Scouts];
    public IReadOnlyList<string> PhasesInTurn { get; } = [];
    /// <summary>No OPR rule orders simultaneous reactions; Windows.ReactionOrder's default stands (scheduler → Windows).</summary>
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams) => null;

    /// <summary>
    /// Page 6: deployment order is a roll-off, and page 50 keeps one for a free-for-all's deploy order. Turn order is not a roll-off
    /// at any team count: page 7 gives round one to the team that deployed first and every later round to whoever finished activating
    /// first, and page 50 says the same for 3 and 4 players. One rule for two teams and for many, so nothing branches on the team count.
    /// </summary>
    public FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams)
    {
        if (round == 0) return new RollOffNeeded(RollOffSeq);
        var previous = state.Projection.Scheduler.Order.Count > 0 ? state.Projection.Scheduler.Order : teams;
        if (round == 1) return new OrderedTeams(previous);
        var log = SchedulerLog.Of(state);
        var ended = log.LastTurnEndedByTeam(log.SinceRound);           // the round just finished: its round_started is still the last
        return new OrderedTeams(previous.OrderBy(t => ended.GetValueOrDefault(t, 0L)).ThenBy(t => TeamUnits.IndexOf(previous, t)).ToList());
    }

    public TurnAsk? LegalTurn(GameState state, PropertySet props, Guid teamId)
    {
        var phase = state.Projection.Scheduler.Phase;
        if (phase is Deployment or Scouts) return DeployAsk(state, props, teamId, phase);
        var actionRows = props.Actions.Select(a => a.Name).ToList();
        var options = TeamUnits.Of(state, teamId)
            .Where(u => u.Actions.Count == 0 && TeamUnits.AnyOnTable(state, u))
            .SelectMany(u => ActionsFor(state, props, u, actionRows).Select(a => ActivationOption.For(u, a))).ToList();
        return options.Count == 0 ? null : new TurnAsk(AskQuestions.Activate, "Activate a unit", new Options(options), AllowPass: true);
    }

    /// <summary>
    /// Page 13: Aircraft "may only use Advance actions ... (even if Shaken)", and "their mandatory move doesn't break the staying idle
    /// requirement to stop being Shaken" — so Aircraft is read before Shaken, and a Shaken Aircraft is offered Advance, not Idle.
    /// Page 10: every other Shaken unit "must spend their activation being idle". Page 14: Immobile means Hold only.
    /// </summary>
    internal static IReadOnlyList<string> ActionsFor(GameState state, PropertySet props, UnitInPlay unit, IReadOnlyList<string> actionRows)
    {
        if (Lookup.UnitHas(state, props, unit.Id, Aircraft)) return actionRows.Where(a => a == Advance).ToList();
        if (unit.Conditions.Contains(Shaken)) return [ActivationOption.Idle];
        if (Lookup.UnitHas(state, props, unit.Id, Immobile)) return actionRows.Where(a => a == Hold).ToList();
        return actionRows;
    }

    public Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => answer switch
    {
        SelectedPieces { PieceIds: [var unit] } when state.Projection.Units.ContainsKey(unit) => new Activation(unit, Deploy, false, []),
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit)
            => new Activation(unit, action, action != ActivationOption.Idle, [new ActionRecorded(unit, action)]),
        _ => null,
    };

    /// <summary>Deploying ends when the last model of the unit has a placement (scheduler → Deployment). Idle is complete at once.</summary>
    public bool ActivationComplete(GameState state, Activation activation) =>
        activation.Action != Deploy || !TeamUnits.ModelsOf(state, state.Projection.Units[activation.UnitId]).Any(m => m.Location is OnShelf);
}
```

Fatigue clearing and spell tokens at `round_started`, the wounds-this-activation reset and Idle-clears-Shaken at `turn_ended`, and the end-of-activation morale tests are the interpreter plan's `GfHooks.OnRoundStarted`, `GfHooks.OnTurnEnded(state, turn)` and `GfHooks.EndOfTurnSequences` (its Task 11), reached through `IGameRules.OnRoundStarted`, `OnTurnEnded` and `EndOfTurnSequences`; this plan writes no body for them.

- [ ] **Step 6: Write `GfRules.Deployment.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>Deployment: the shelf minus set-aside units. Scouts: the set-aside Scouts, after every other unit (page 14). One unit per Ask; no pass.</summary>
    private static TurnAsk? DeployAsk(GameState state, PropertySet props, Guid teamId, string phase)
    {
        var units = TeamUnits.Of(state, teamId).Where(u => TeamUnits.AllShelved(state, u));
        units = phase == Deployment
            ? units.Where(u => !u.Conditions.Contains(SetAside))
            : units.Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Scout));
        var ids = units.Select(u => u.Id).ToList();
        if (ids.Count == 0) return null;
        return phase == Deployment
            ? new TurnAsk(AskQuestions.Deploy, "Deploy one unit", new PieceSelection(ids, 1, 1), AllowPass: false)
            : new TurnAsk(AskQuestions.DeployScouts, "Deploy a Scout", new PieceSelection(ids, 1, 1), AllowPass: false);
    }

    internal static IReadOnlyList<Guid> Ambushers(GameState state, PropertySet props, Guid teamId) =>
        TeamUnits.Of(state, teamId).Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Ambush) && TeamUnits.AllShelved(state, u)).Select(u => u.Id).ToList();

    /// <summary>
    /// Page 13: from round two, Ambushers deploy "starting with the player that activates next", alternating, until every team has passed
    /// or has none left. Read from the deploy_ambusher Asks since round_started; a pass stands for this boundary; nothing is written to the record.
    /// </summary>
    public HookResult RoundBoundaryStep(GameState state, PropertySet props, int round, IReadOnlyList<Guid> order)
    {
        if (round < 2) return new HookDone();
        var log = SchedulerLog.Of(state);
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployAmbusher).ToList();
        var passed = new HashSet<Guid>();
        foreach (var a in asks)
        {
            if (state.Projection.OpenAsks.ContainsKey(a.Ask.Id)) return new HookWaiting();
            var answer = since.OfType<AskClosed>().First(c => c.AskId == a.Ask.Id).Answers.FirstOrDefault()?.Answer;
            if (answer is SelectedPieces { PieceIds: [var unit] } && state.Projection.Units.TryGetValue(unit, out var u) && TeamUnits.AllShelved(state, u))
                return new HookWaiting();                                            // models still coming off the shelf
            if (answer is not SelectedPieces) passed.Add(a.Ask.Teams[0]);
        }
        var eligible = order.Where(t => !passed.Contains(t) && Ambushers(state, props, t).Count > 0).ToList();
        if (eligible.Count == 0) return new HookDone();
        var next = eligible[0];
        if (asks.Count > 0)
        {
            var lastIndex = TeamUnits.IndexOf(order, asks[^1].Ask.Teams[0]);
            next = eligible.FirstOrDefault(t => TeamUnits.IndexOf(order, t) > lastIndex, eligible[0]);
        }
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [next],
            new PieceSelection(Ambushers(state, props, next), 1, 1), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);
        return new HookEmit([new AskOpened(ask)]);
    }
}
```

- [ ] **Step 7: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect every earlier test still green and 8 new passed. Commit: `Give GameRules the scheduler's hooks and implement them for Grimdark Future`.**
