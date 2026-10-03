# Task 3: Seizing, in Grimdark Future

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfObjectives.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs`
- Modify: `server/game/Rules/IGameRules.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`
- Test: `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs` (new)

**Interfaces:**
- Consumes: `ObjectiveControlSet`, `Projection.Control` (Task 1); `TeamUnits.Of`, `SchedulerLog.Of(...).SinceRound`, `Lookup.LivingModels`, `Lookup.UnitHas`, `Battlefield.Placed`, `Geometry.Distance.EdgeToMarker(PlacedModel, Point2, double)`, `GfNames.Shaken`, `GfNames.Aircraft`, `AskQuestions.DeployAmbusher`; test helpers `Armies.State`, `Armies.Unit`, `Armies.P`, `TestProperties.Core`.
- Produces:

```csharp
public static class GfObjectives
{
    public const double SeizeRange = 3;
    public static IReadOnlyList<Marker> Markers(GameState state);
    public static IReadOnlyList<StateChange> Seize(GameState state, PropertySet props);
    public static IReadOnlyList<Guid> Winners(GameState state);
    public static string Standing(GameState state);
}
```

Spec: objectives spec → *The rule*, *The seize, in Grimdark Future*. GF p.6: "At the end of each round, if a unit is within 3" of a marker whilst no enemies are, then it counts as being seized. Markers stay under the player's control even if the unit moves away, but if units from both sides contest a marker at the end of a round, then it becomes neutral again." p.10: Shaken units "can't seize or contest objectives". p.13 Aircraft: "can't seize or contest objectives". p.13 Ambush: "Units that deploy via Ambush can't seize or contest objectives on the round they deploy."

The geometry: `Armies.State` builds the board from `TableStateTests.Sample()`, whose one objective sits at (36, 24) with diameter 3 (radius 1.5). A one-model `Armies.Unit` has a `Base.Circle(1.26)` (radius 0.63). A model centred at (36, 28) is 4 − 1.5 − 0.63 = 1.87" from the marker's edge: within. At (36, 31) it is 4.87": outside.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`:

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>GF p.6 seizing, with p.10's Shaken and p.13's Aircraft and Ambush exclusions.</summary>
public class ObjectivesTests
{
    static readonly Tabletop.Content.Unit Red = Unit("Red", "4+", "4+", [], 1, []), Blue = Unit("Blue", "4+", "4+", [], 1, []);
    static readonly Tabletop.Content.Unit Flyer = Unit("Flyer", "4+", "4+", [P(GfNames.Aircraft)], 1, []);

    static GameState Game(params Tabletop.Content.Unit[] blue) => State([Red, Flyer], blue.Length == 0 ? [Blue] : blue);
    static Guid Objective(GameState s) => s.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
    static void Apply(GameState s, params EventPayload[] events) { foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null)); }
    static void Put(GameState s, Tabletop.Content.Unit unit, double x, double y) => Apply(s, new ModelsMoved(unit.Models.Select(m =>
        new ModelRelocation(m.Id, s.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))).ToList()));
    static IReadOnlyList<StateChange> Seize(GameState s) => GfObjectives.Seize(s, TestProperties.Core);
    static void Seized(GameState s) => Apply(s, Seize(s).ToArray());

    [Fact]
    public void One_team_alone_within_three_inches_seizes_and_keeps_it_when_it_leaves()
    {
        var s = Game();
        Put(s, Red, 36, 28);
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team1)], Seize(s));
        Seized(s);
        Assert.Empty(Seize(s));                                          // already held: no change to write
        Put(s, Red, 36, 31);
        Assert.Empty(Seize(s));                                          // "stay under the player's control even if the unit moves away"
        Assert.Equal(Samples.Team1, s.Projection.Control[Objective(s)]);
    }

    [Fact]
    public void A_unit_just_out_of_range_seizes_nothing()
    {
        var s = Game();
        Put(s, Red, 36, 31);
        Assert.Empty(Seize(s));
    }

    [Fact]
    public void Both_sides_within_range_make_a_held_marker_neutral_and_leave_a_neutral_one_neutral()
    {
        var s = Game();
        Put(s, Blue, 36, 20); Put(s, Red, 36, 28);
        Assert.Empty(Seize(s));                                          // neutral and contested: still neutral
        Apply(s, new ObjectiveControlSet(Objective(s), Samples.Team1));
        Assert.Equal([new ObjectiveControlSet(Objective(s), null)], Seize(s));
    }

    [Fact]
    public void Shaken_and_Aircraft_neither_seize_nor_contest()
    {
        var s = Game();
        Put(s, Red, 36, 28); Put(s, Flyer, 37, 28); Put(s, Blue, 36, 20);
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, Red.Id), GfNames.Shaken, true));
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));   // Red is Shaken and Flyer is an Aircraft: Blue is alone
    }

    [Fact]
    public void An_Ambusher_neither_seizes_nor_contests_in_the_round_it_arrives_but_does_the_next()
    {
        var s = Game();
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [Samples.Team1],
            new PieceSelection([Red.Id], 1, 1), true, false, null, null);
        Apply(s, new RoundStarted(2, [Samples.Team1, Samples.Team2], []), new AskOpened(ask), new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new SelectedPieces([Red.Id]))]));
        Put(s, Red, 36, 28); Put(s, Blue, 36, 20);
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));   // Red arrived this round
        Apply(s, new RoundStarted(3, [Samples.Team1, Samples.Team2], []));
        Assert.Empty(Seize(s));                                          // round 3: Red counts again, both teams contest a marker nobody holds
    }

    [Fact]
    public void A_model_inside_a_transport_is_nowhere_near_a_marker()
    {
        var s = Game();
        Put(s, Blue, 36, 28);
        Apply(s, new ModelsMoved([new ModelRelocation(Red.Models[0].Id, s.Projection.Models[Red.Models[0].Id].Location, new Inside(Blue.Models[0].Id))]));
        Assert.Equal([new ObjectiveControlSet(Objective(s), Samples.Team2)], Seize(s));
    }

    [Fact]
    public void An_objective_placed_in_play_is_seized_like_one_from_setup_and_a_token_is_not()
    {
        var s = Game();
        var placed = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(10, 10, 0), new Yaw(0), 2, "B", VisualRef.OfBuiltin("objective-ring"));
        Apply(s, new MarkerPlaced(placed), new MarkerPlaced(Samples.Token with { Position = new Point3(10, 14, 0) }));
        Put(s, Red, 10, 13);
        Assert.Equal([new ObjectiveControlSet(placed.Id, Samples.Team1)], Seize(s));
        Assert.Equal(2, GfObjectives.Markers(s).Count);
    }

    [Fact]
    public void Most_markers_wins_and_an_equal_count_is_a_tie()
    {
        var s = Game();
        var placed = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(10, 10, 0), new Yaw(0), 2, "B", VisualRef.OfBuiltin("objective-ring"));
        Apply(s, new MarkerPlaced(placed));
        Assert.Equal([Samples.Team1, Samples.Team2], GfObjectives.Winners(s));               // nobody holds anything: every team at none
        Assert.Equal("Markers held: team 1: 0, team 2: 0.", GfObjectives.Standing(s));
        Apply(s, new ObjectiveControlSet(Objective(s), Samples.Team2));
        Assert.Equal([Samples.Team2], GfObjectives.Winners(s));
        Apply(s, new ObjectiveControlSet(placed.Id, Samples.Team1));
        Assert.Equal([Samples.Team1, Samples.Team2], GfObjectives.Winners(s));
        Assert.Equal("Markers held: team 1: 1, team 2: 1.", GfObjectives.Standing(s));
    }
}
```

In the Ambush test, round 3 finds Red and Blue both within range of a marker nobody holds, so it stays neutral and nothing is written: the point is that Red now counts, which the round-2 case (Blue seized alone) shows it did not.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter ObjectivesTests`
Expected: compile error, `GfObjectives` does not exist.

- [ ] **Step 3: Write `GfObjectives`**

Create `server/game/Rules/GrimdarkFuture/GfObjectives.cs`:

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.6's mission: "At the end of each round, if a unit is within 3" of a marker whilst no enemies are, then it counts as being seized.
/// Markers stay under the player's control even if the unit moves away, but if units from both sides contest a marker at the end of a
/// round, then it becomes neutral again. After 4 rounds have been played, the game ends, and the player that controls most markers wins."
/// Shaken units (p.10), Aircraft (p.13) and units that deployed via Ambush this round (p.13) "can't seize or contest objectives".
/// "Both sides" is more than one team: p.50 plays a free-for-all by the regular rules, and every other team is an enemy.
/// </summary>
public static class GfObjectives
{
    public const double SeizeRange = 3;
    /// <summary>Tabletop.Geometry's own distance tolerance, which that library keeps internal.</summary>
    const double Epsilon = 1e-6;

    /// <summary>The objective markers: the setup rows, then those placed in play, the projection's entry winning on a shared id.</summary>
    public static IReadOnlyList<Marker> Markers(GameState state)
    {
        var play = state.Projection.Markers;
        return state.Board.Table.Markers.Where(m => !play.ContainsKey(m.Id)).Concat(play.Values)
            .Where(m => m.Kind == MarkerKind.Objective).ToList();
    }

    /// <summary>One ObjectiveControlSet per marker whose controller changes at this round end; none when nothing changes.</summary>
    public static IReadOnlyList<StateChange> Seize(GameState state, PropertySet props)
    {
        var arrived = ArrivedByAmbushThisRound(state);
        var placedByTeam = state.Info.Teams.ToDictionary(t => t.Id, t => TeamUnits.Of(state, t.Id)
            .Where(u => !u.Conditions.Contains(GfNames.Shaken) && !arrived.Contains(u.Id) && !Lookup.UnitHas(state, props, u.Id, GfNames.Aircraft))
            .SelectMany(u => Lookup.LivingModels(state, u.Id))
            .Select(m => Battlefield.Placed(state, m)).Where(p => p.HasValue).Select(p => p!.Value).ToList());
        var changes = new List<StateChange>();
        foreach (var marker in Markers(state))
        {
            var centre = marker.Position.Horizontal;
            var near = placedByTeam.Where(kv => kv.Value.Any(m => Distance.EdgeToMarker(m, centre, marker.Diameter) <= SeizeRange + Epsilon))
                .Select(kv => kv.Key).ToList();
            Guid? now = state.Projection.Control.TryGetValue(marker.Id, out var held) ? held : null;
            var next = near.Count switch { 0 => now, 1 => near[0], _ => (Guid?)null };
            if (next != now) changes.Add(new ObjectiveControlSet(marker.Id, next));
        }
        return changes;
    }

    /// <summary>The teams with the most controlled markers, in ordinal order: one is the winner, several are a tie.</summary>
    public static IReadOnlyList<Guid> Winners(GameState state)
    {
        var held = Held(state);
        if (held.Count == 0) return [];
        var most = held.Max(h => h.Count);
        return held.Where(h => h.Count == most).Select(h => h.Team.Id).ToList();
    }

    /// <summary>"Markers held: team 1: 2, team 2: 1." Teams by ordinal: the server holds no team names.</summary>
    public static string Standing(GameState state) =>
        $"Markers held: {string.Join(", ", Held(state).Select(h => $"team {h.Team.Ordinal}: {h.Count}"))}.";

    static IReadOnlyList<(Team Team, int Count)> Held(GameState state)
    {
        var markers = Markers(state).Select(m => m.Id).ToHashSet();
        return state.Info.Teams.OrderBy(t => t.Ordinal)
            .Select(t => (t, state.Projection.Control.Count(kv => kv.Value == t.Id && markers.Contains(kv.Key)))).ToList();
    }

    /// <summary>Units named by a deploy_ambusher Ask closed since this round's round_started (the Asks RoundBoundaryStep reads).</summary>
    static HashSet<Guid> ArrivedByAmbushThisRound(GameState state)
    {
        var since = SchedulerLog.Of(state).SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployAmbusher).Select(o => o.Ask.Id).ToHashSet();
        return since.OfType<AskClosed>().Where(c => asks.Contains(c.AskId))
            .SelectMany(c => c.Answers).Select(a => a.Answer).OfType<SelectedPieces>().SelectMany(s => s.PieceIds).ToHashSet();
    }
}
```

`server/game/Rules/IGameRules.cs`, at the end of the interface body, after the enforcement plan's `JudgeAdvance`:

```csharp
    // Added by the objectives plan.
    /// <summary>The ruleset's changes at the end of a round (round 1 on), after a player ended it and before the next round starts. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => [];
```

Create `server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>p.6: "At the end of each round" objectives are seized, contested and kept (GfObjectives).</summary>
public sealed partial class GrimdarkFutureRules
{
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) =>
        round < 1 ? [] : GfObjectives.Seize(state, properties);
}
```

In `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, with the scheduler plan's members:

```csharp
    public Func<GameState, PropertySet, int, IReadOnlyList<StateChange>>? RoundEndedHook { get; set; }
    public IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => RoundEndedHook?.Invoke(state, properties, round) ?? [];
```

`FakeRules.Gf()` leaves the hook unset: the scheduler tests' boundaries stay exactly as they are.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter ObjectivesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add server/game/Rules tests/Tabletop.GameServer.Tests
git commit -m "Seize, contest and keep Grimdark Future objective markers, and count who holds most"
```
