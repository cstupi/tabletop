# Task 4a: Sight lines, model facts and who can shoot in the rules

**Wave:** 2 · **Depends on:** 1 · **Model:** opus

**Files:**
- Modify: `server/game/Interpreter/Sight.cs` (`Line`; `Look` reads it)
- Modify: `server/game/Rules/IGameRules.cs` (`MaxWounds`, `Shooters` default members)
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Facts.cs`
- Modify: `server/game/Rules/IRulesEngine.cs` (`IRulesEngine.Properties` default member)
- Modify: `server/game/Engine/RulesEngine.cs` (implements `Properties`)
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences` forwards the new members)
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs` (forwards them; `MaxWoundsHook`)
- Test: `tests/Tabletop.GameServer.Tests/SightLineTests.cs`, `tests/Tabletop.GameServer.Tests/ModelFactsTests.cs` (new)

`GfRules.Facts.cs` reads `GfShooting.Groups` and `Lookup.ModelParameter`; it edits none of `GfMoves.cs`, `GfMelee.cs`,
`GfShooting.cs`, so this task and Task 3 are disjoint.

**Interfaces:**
- Consumes: `SightLine` (Task 1).
- Produces:

```csharp
// server/game/Interpreter/Sight.cs
public SightLine? Line(Guid fromModel, Guid toModel);   // null when either model is off the table
// server/game/Rules/IGameRules.cs
int MaxWounds(GameState state, Guid modelId) => 1;
IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => [];
// server/game/Rules/IRulesEngine.cs
PropertySet? Properties => null;                        // RulesEngine returns its own
// tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs
public Func<GameState, Guid, int>? MaxWoundsHook { get; set; }
// tests/Tabletop.GameServer.Tests/SightLineTests.cs
internal static (GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets, Guid Wall) Table(double wallX, params Weapon[] weapons);
```

Task 4b answers the socket from these.

`Shooters` takes the `PropertySet` that every other `IGameRules` hook reading live properties takes (`ReactionsAt`,
`LegalTurn`, `RoundBoundaryStep`): p.8's *Who can shoot* depends on weapon range, and range is a live stat
(`StatReader.WeaponRange` over a `LiveSet`), which cannot be settled without the set. The session reads the set from
`IRulesEngine.Properties`.

Spec: *Server and protocol changes → Sight question*, *Model facts*; *Sight check*.

Page 8, *Who can shoot*:

> All models in a unit with line of sight to the target, and that have a weapon that is within range of it, may fire
> at it. Note that models may always ignore friendly models from their own unit when determining line of sight.

This is `GfAttack.EligibleModels(state, live, attacker, target, weapons, AttackKind.Shooting)`, which `ValidTargets`
and `fire-groups` already use per weapon group (p.8 *Multiple weapon types*). `Shooters` is its union over the unit's
ranged groups (`GfShooting.Groups(state, props, unit, null)`), each group's live set settled the way
`GfAttack.AttackInteraction` builds it outside a frame: no charge range, no faces, the action of the unit's running
activation if it has one. Rush's "can't shoot" is not *Who can shoot* and is not applied here; the `declare_targets`
Ask already leaves a Rush's targets out.

`MaxWounds` is `Tough(X)`, else 1: `Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1`, the reading
`GfMoves.DangerousDice` and `GfHooks` already use.

`SightLine.BlockedBy` is `LineOfSightResult.ObstructedBy`: a terrain entry is a `TerrainVolume.Id` (the volume, not
the `TerrainPlacement.Id`; the client maps it through `Board.Table.Terrain[*].Volumes`), a model entry is the model's
id. It is empty on a clear line, screened or not. The client lights the terrain piece whose `TerrainPlacement.Volumes`
holds the id (Task 18).

- [ ] **Step 1: Write the failing sight-line tests**

Create `tests/Tabletop.GameServer.Tests/SightLineTests.cs`:

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Sight.Line: the pair's line as the rules measure it, with what blocks it.</summary>
public class SightLineTests
{
    /// <summary>
    /// Two models a side, observers at x -5, targets at x 5, each pair along y -18.5 and -17 (Armies.State). A 1"x1", 10" tall wall
    /// centred on (wallX, -18.5). At wallX 0 it stands across the y -18.5 line only: observer 1 to target 1 is blocked; the lines to or
    /// from y -17 cross x 0 at y -17.75 or above, clear of it. At wallX -3.5 it stands in front of observer 1, whose lines to both
    /// targets cross x -3.5 below y -18, while observer 2's cross above y -17.3.
    /// </summary>
    internal static (GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets, Guid Wall) Table(double wallX, params Weapon[] weapons)
    {
        var wallVolume = Guid.CreateVersion7();
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(wallX, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(wallVolume, new Volume(VolumeKind.Solid, Footprint.Rect(1, 1), new Point2(0, 0), new Yaw(0), 0, 10, TerrainDeclarations.BlocksLineOfSight, null))]);
        var observers = Unit("Observers", "4+", "4+", [], 2, [], weapons);
        var targets = Unit("Targets", "4+", "4+", [], 2, [P("Tough", 3)]);
        return (State([observers], [targets], x1: -5, x2: 5, terrain: [wall]), observers, targets, wallVolume);
    }

    [Fact]
    public void A_line_through_the_wall_is_blocked_by_it()
    {
        var (state, observers, targets, wall) = Table(0);
        var line = Sight.From(state, observers.Id).Line(observers.Models[0].Id, targets.Models[0].Id);
        Assert.NotNull(line);
        Assert.Equal((observers.Models[0].Id, targets.Models[0].Id, false, false), (line!.FromModelId, line.ToModelId, line.Clear, line.Screened));
        Assert.Contains(wall, line.BlockedBy);
    }

    [Fact]
    public void Every_line_agrees_with_the_rules_reading()
    {
        var (state, observers, targets, _) = Table(0);
        var sight = Sight.From(state, observers.Id);
        foreach (var from in observers.Models)
            foreach (var to in targets.Models)
            {
                var line = sight.Line(from.Id, to.Id)!;
                Assert.Equal(((bool, bool)?)(line.Clear, line.Screened), sight.Look(from.Id, to.Id));
                Assert.Equal(sight.CanSee(from.Id, to.Id), line.Clear);
                if (line.Clear) Assert.Empty(line.BlockedBy);
            }
        Assert.True(sight.Line(observers.Models[1].Id, targets.Models[1].Id)!.Clear);
    }

    [Fact]
    public void A_model_off_the_table_has_no_line()
    {
        var (state, observers, targets, _) = Table(0);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(targets.Models[0].Id, state.Projection.Models[targets.Models[0].Id].Location, new OnShelf())]), null));
        Assert.Null(Sight.From(state, observers.Id).Line(observers.Models[0].Id, targets.Models[0].Id));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter SightLineTests`
Expected: compile error: `Sight` has no `Line`.

- [ ] **Step 3: Implement `Line`**

In `server/game/Interpreter/Sight.cs`, replace `Look` with:

```csharp
    /// <summary>
    /// One sight line measured: `Clear` is `LineOfSight.Check`, and `Screened` is `Cover.SightBlockersBetween` non-empty along a
    /// clear line, so it is false whenever `Clear` is. The target's own body is never its own blocker. Null when either model is off the table.
    /// </summary>
    public (bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel) => Line(fromModel, toModel) is { } line ? (line.Clear, line.Screened) : null;

    /// <summary>
    /// The same measurement as a wire SightLine (in-game UX spec → Sight question): BlockedBy is LineOfSightResult.ObstructedBy, the
    /// terrain volume ids and model ids in the way, empty on a clear line. Null when either model is off the table.
    /// </summary>
    public SightLine? Line(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        if (from is null || to is null) return null;
        var volumes = bodies.Contains(toModel) ? blockers.Where(v => v.Id != toModel).ToList() : blockers;
        var check = LineOfSight.Check(from.Value, to.Value, volumes);
        if (!check.Clear) return new SightLine(fromModel, toModel, false, false, check.ObstructedBy);
        return new SightLine(fromModel, toModel, true, Cover.SightBlockersBetween(from.Value, to.Value, volumes).Count > 0, []);
    }
```

`Sight.cs` already has `using Tabletop.Protocol;`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "SightLineTests|GfCoverTests|GfShootingTests"`
Expected: PASS: `Look` and `CanSee` answer as before.

- [ ] **Step 5: Write the failing model-facts and shooters tests**

Create `tests/Tabletop.GameServer.Tests/ModelFactsTests.cs`:

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The ruleset's answers the client is told rather than reading a rule (in-game UX spec → Model facts, Sight question).</summary>
public class ModelFactsTests
{
    static readonly Weapon Rifle = Gun("Rifle", "12\"", 1), Pistol = Gun("Pistol", "6\"", 1);

    [Fact]
    public void Max_wounds_is_Tough_else_one()
    {
        var (state, observers, targets, _) = SightLineTests.Table(0);
        Assert.Equal(3, GrimdarkFutureRules.Instance.MaxWounds(state, targets.Models[0].Id));
        Assert.Equal(1, GrimdarkFutureRules.Instance.MaxWounds(state, observers.Models[0].Id));
    }

    [Fact]
    public void Who_can_shoot_is_line_of_sight_and_a_weapon_in_range()
    {
        var (state, observers, targets, _) = SightLineTests.Table(-3.5, Rifle);                         // the wall hides every target from observer 1
        Assert.Equal([observers.Models[1].Id], GrimdarkFutureRules.Instance.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
    }

    [Fact]
    public void Nobody_shoots_out_of_range_or_without_a_ranged_weapon()
    {
        var (near, nearObservers, nearTargets, _) = SightLineTests.Table(0, Pistol);                    // 8.74" edge to edge: past 6"
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(near, TestProperties.Core, nearObservers.Id, nearTargets.Id));
        var (bare, bareObservers, bareTargets, _) = SightLineTests.Table(0, Melee("Blade", 2));
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(bare, TestProperties.Core, bareObservers.Id, bareTargets.Id));
    }

    [Fact]
    public void An_unknown_unit_has_no_shooters()
    {
        var (state, observers, _, _) = SightLineTests.Table(0, Rifle);
        Assert.Empty(GrimdarkFutureRules.Instance.Shooters(state, TestProperties.Core, observers.Id, Guid.CreateVersion7()));
    }

    [Fact]
    public void A_ruleset_that_says_nothing_answers_one_wound_and_no_shooters()
    {
        var (state, observers, targets, _) = SightLineTests.Table(0, Rifle);
        IGameRules stub = new StubRules();
        Assert.Equal(1, stub.MaxWounds(state, targets.Models[0].Id));
        Assert.Empty(stub.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
    }

    [Fact]
    public void A_wrapped_Grimdark_Future_still_answers_max_wounds_and_shooters()
    {
        var (state, observers, targets, _) = SightLineTests.Table(-3.5, Rifle);
        foreach (IGameRules wrapped in new IGameRules[] { new WithSequences(GrimdarkFutureRules.Instance), FakeRules.Gf() })
        {
            Assert.Equal(3, wrapped.MaxWounds(state, targets.Models[0].Id));
            Assert.Equal([observers.Models[1].Id], wrapped.Shooters(state, TestProperties.Core, observers.Id, targets.Id));
        }
    }
}
```

The last test is the README's Review Focus 4: a wrapper that forgets to forward falls back to the interface's
defaults (1, nobody) and fails it.

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter ModelFactsTests`
Expected: compile errors: `MaxWounds` and `Shooters` are not members of `IGameRules` or `GrimdarkFutureRules`.

- [ ] **Step 7: Add the members and Grimdark Future's answers**

In `server/game/Rules/IGameRules.cs`, after `OnRoundEnded`:

```csharp

    // Facts the client is told rather than reading a rule name.
    /// <summary>Wounds the model takes before it is killed (OPR: Tough(X), else 1). Wrapping rulesets must forward it.</summary>
    int MaxWounds(GameState state, Guid modelId) => 1;
    /// <summary>The unit's models that may fire at the target unit now (OPR p.8, Who can shoot). Empty when either unit is unknown or off the table. Wrapping rulesets must forward it.</summary>
    IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => [];
```

Create `server/game/Rules/GrimdarkFuture/GfRules.Facts.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The in-game UX plan's partial: the facts the session tells the client (in-game UX spec → Model facts, Sight question).</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>Tough(X): "must take X wounds to be killed"; every other model is killed by one.</summary>
    public int MaxWounds(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Tough) ?? 1;

    /// <summary>
    /// p.8, Who can shoot: "All models in a unit with line of sight to the target, and that have a weapon that is within range of it, may fire
    /// at it" — GfAttack.EligibleModels per ranged weapon group (p.8, Multiple weapon types), united. Each group's live set is settled as
    /// GfAttack.AttackInteraction settles it with no frame: no charge range, no faces, under the action of the unit's running activation.
    /// </summary>
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId)
    {
        var p = state.Projection;
        if (!p.Units.ContainsKey(unitId) || !p.Units.ContainsKey(targetUnitId)) return [];
        if (Lookup.AliveModels(state, unitId).Count == 0 || Lookup.LivingModels(state, targetUnitId).Count == 0) return [];
        var action = p.Stack.FirstOrDefault(f => f.Context.ActorUnitId == unitId)?.Context.Action;
        return GfShooting.Groups(state, properties, unitId, null).SelectMany(group =>
        {
            var models = p.Models.Values.Where(m => m.Slot.Weapons.Any(w => group.WeaponIds.Contains(w.Id))).Select(m => m.Id).ToList();
            var live = LiveProperties.Settle(new Interaction(state, properties, Party.Of(state, unitId, models, group.WeaponIds), Party.Whole(state, targetUnitId),
                AttackKind.Shooting, action, Battlefield.UnitDistance(state, unitId, targetUnitId), Battlefield.TerrainUnder(state, targetUnitId)));
            return GfAttack.EligibleModels(state, live, unitId, targetUnitId, group.WeaponIds, AttackKind.Shooting);
        }).Distinct().ToList();
    }
}
```

`AliveModels` is the observer's test, not `LivingModels`: `EligibleModels` answers for a unit inside a container
through `Battlefield.InsideReach`. `AttackKind`, `Interaction`, `Party`, `LiveProperties` and `Lookup` are in
`Tabletop.GameServer.Effects`; `Battlefield` is in `Tabletop.GameServer.Interpreter`.

- [ ] **Step 8: Forward the members through the test wrappers**

In `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`, add to `WithSequences` after `OnRoundEnded`:

```csharp
    public int MaxWounds(GameState state, Guid modelId) => inner.MaxWounds(state, modelId);
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => inner.Shooters(state, properties, unitId, targetUnitId);
```

In `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, add a hook with the other scheduler-plan hooks:

```csharp
    /// <summary>Replaces Grimdark Future's MaxWounds: how a session test makes the facts change between batches.</summary>
    public Func<GameState, Guid, int>? MaxWoundsHook { get; set; }
```

and, after `OnRoundEnded`:

```csharp
    public int MaxWounds(GameState state, Guid modelId) => MaxWoundsHook?.Invoke(state, modelId) ?? _inner.MaxWounds(state, modelId);
    public IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => _inner.Shooters(state, properties, unitId, targetUnitId);
```

`StubRules` keeps the defaults: it is a ruleset of its own, not a wrapper. A search for any other hand-forwarding
implementation must come back with these two only:

Run: `grep -rn ": IGameRules" --include='*.cs' server tests sim | grep -v /obj/`
Expected: `GrimdarkFutureRules`, `FakeRules`, `StubRules`, `WithSequences`.

- [ ] **Step 9: Run them to see them pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "ModelFactsTests|SightLineTests"`
Expected: PASS.

- [ ] **Step 10: Give the engine's property set to a question asked outside Advance**

In `server/game/Rules/IRulesEngine.cs`, add `using Tabletop.GameServer.Properties;` and, after `RoundStartChanges`:

```csharp
    /// <summary>The game's pinned property set, for a question asked outside Advance (the sight check's who-can-shoot). Null for an engine with none.</summary>
    PropertySet? Properties => null;
```

In `server/game/Engine/RulesEngine.cs`, after the `Interpreter` property:

```csharp
    public PropertySet Properties => properties;
```

- [ ] **Step 11: Run the whole suite**

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. Nothing outside this task calls the new members yet; `Look` and `CanSee` answer as before.

- [ ] **Step 12: Commit**

```bash
git add server/game/Interpreter/Sight.cs server/game/Rules/IGameRules.cs server/game/Rules/GrimdarkFuture/GfRules.Facts.cs \
  server/game/Rules/IRulesEngine.cs server/game/Engine/RulesEngine.cs \
  tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs \
  tests/Tabletop.GameServer.Tests/SightLineTests.cs tests/Tabletop.GameServer.Tests/ModelFactsTests.cs
git commit -m "Measure a sight line with its blockers, and answer max wounds and who can shoot from the ruleset" \
  -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
