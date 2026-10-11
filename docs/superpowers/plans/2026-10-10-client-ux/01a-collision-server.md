# Physical collision, Tasks 4–7: server, wire, breaches, hill meshes

Part of [`01-collision.md`](01-collision.md); its Global Constraints, Rulings and Review Focus apply. Tasks 1–3 have landed.

### Task 4: The server rests every placement it stores

**Model:** sonnet

**Files:** create `tests/Tabletop.GameServer.Tests/RestingTests.cs`; modify `server/game/Interpreter/Battlefield.cs`,
`server/game/Rules/GrimdarkFuture/GfMoves.cs` (lines 110, 320, 461), `GfMoves.Terrain.cs` (75), `GfMelee.cs` (93),
`server/game/Dispatch/MenuTranslation.cs`, `server/game/Setup/DraftEditor.cs`.

**Interfaces:**
- Consumes: `Collision.RestHeight(PlacedModel, …)`, `Collision.RestHeight(IReadOnlyList<WorldVolume> piece, …)`, `TerrainPlacement.WorldVolumes()`.
- Produces: `public static Placement Battlefield.Settle(GameState state, Guid modelId, Placement client)` (replaces `Settle(state, client)`; Z ignored).
  `MenuTranslation` settles every `OnTable` destination of a `MoveModels`; `DraftEditor.Apply` rests the piece an `AddTerrain`,
  `MoveTerrain`, `ScaleTerrain` or `OverrideTerrainVolumes` names.

- [ ] **Step 1: Failing test** — `tests/Tabletop.GameServer.Tests/RestingTests.cs`:

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Setup;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Client UX spec → Physical collision → Server: a stored position is never inside terrain, whatever the client sent.</summary>
public class RestingTests
{
    static TerrainPlacement Piece(double x, double y, VolumeKind kind, Footprint f, double top) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, f, new Point2(0, 0), new Yaw(0), 0, top, TerrainDeclarations.None, null))]);
    static TerrainPlacement Hill(double x, double y) => Piece(x, y, VolumeKind.Solid, Footprint.Rect(6, 6), 2);

    // Armies.State: team 1's first model at (-12, -18.5). The hill spans x -9..-3; a centre at -9.2 puts the base half over it.
    static Sim Game(out Unit unit) { unit = Unit("A", "4+", "4+", [], 2, []); return Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x2: 20, enforcement: Enforcement.Silent, terrain: [Hill(-6, -18.5)])); }
    static ModelRelocation To(Sim sim, Guid m, double x) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, -18.5, 0), new Yaw(0))));
    static double Z(Sim sim, Guid m) => ((OnTable)sim.Model(m).Location).Placement.Position.Z;

    [Fact]
    public void A_hand_move_sent_at_z0_over_a_hill_is_stored_on_it()
    {
        var sim = Game(out var u);
        Assert.IsType<Applied>(sim.Dispatch(new MoveModels([To(sim, u.Models[0].Id, -9.2)]), Samples.P1, Samples.Team1));
        Assert.Equal(2, Z(sim, u.Models[0].Id), 6);
    }

    [Fact]
    public void A_deployment_placement_is_stored_on_the_hill()
    {
        var sim = Game(out var u); var m = u.Models[0].Id;
        sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnShelf())])]);
        sim.Dispatch(new MoveModels([new ModelRelocation(m, new OnShelf(), To(sim, m, -6).To)]), Samples.P1, Samples.Team1);
        Assert.Equal(2, Z(sim, m), 6);
    }

    [Fact]
    public void A_move_answer_sent_at_z0_is_stored_on_the_hill()
    {
        var sim = Game(out var u);
        sim.Start(GfNames.RushSeq, new SequenceContext(u.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([To(sim, u.Models[0].Id, -6)]));
        Assert.Equal(2, Z(sim, u.Models[0].Id), 6);
    }

    [Fact]
    public void A_wood_added_or_moved_onto_a_hill_rests_on_it_and_the_hill_ignores_itself()
    {
        var hill = Hill(12, 12); var wood = Piece(30, 30, VolumeKind.Area, Footprint.Circle(3), 6);
        var d = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [hill], [], []);
        d = DraftEditor.Apply(d, new AddTerrain(wood), out _)!;
        Assert.Equal(0, d.Terrain.Single(t => t.Id == wood.Id).Position.Z);
        d = DraftEditor.Apply(d, new MoveTerrain(wood.Id, new Point3(13, 12, 0), new Yaw(0)), out _)!;
        Assert.Equal(2, d.Terrain.Single(t => t.Id == wood.Id).Position.Z, 6);
        d = DraftEditor.Apply(d, new MoveTerrain(hill.Id, new Point3(12.5, 12, 0), new Yaw(0)), out _)!;
        Assert.Equal(0, d.Terrain.Single(t => t.Id == hill.Id).Position.Z);           // an Area is never a floor for a piece
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.GameServer.Tests --filter RestingTests` — the hand-move, deployment and draft tests
  fail (Z 0); the move-answer test fails on 0 (`Settling` from Z 0 never climbs).
- [ ] **Step 3: Implement.** `Battlefield.cs` — replace `Settle`:

```csharp
    /// <summary>Client UX spec → Physical collision: rests the model over its whole base (Collision.RestHeight). The client's Z is not read.</summary>
    public static Placement Settle(GameState state, Guid modelId, Placement client) =>
        new(client.Position with { Z = Collision.RestHeight(At(state, modelId, client), Terrain(state)) }, client.Yaw);
```

  Callers pass the model: `GfMoves.cs:110` `Battlefield.Settle(state, modelId, proposed)`; `:320` and `:461`
  `Battlefield.Settle(ctx.State, m.ModelId, …)`; `GfMoves.Terrain.cs:75` and `GfMelee.cs:93` likewise with `m.ModelId`.
  `MenuTranslation.cs` (add `using Tabletop.GameServer.Interpreter;`):

```csharp
            MoveModels m when m.Moves.All(x => HasModel(x.ModelId)) => new ModelsMoved(m.Moves.Select(x => x.To is OnTable t
                ? new ModelRelocation(x.ModelId, x.From, new OnTable(Battlefield.Settle(state, x.ModelId, t.Placement))) : x).ToList()),
```

  `DraftEditor.cs` — wrap the four cases: `case AddTerrain a: … : Rest(d with { Terrain = [.. d.Terrain, a.Placement] }, a.Placement.Id);`,
  `case MoveTerrain m: return Rest(Terrain(d, m.PlacementId, …, out missing), m.PlacementId);` and the same for `ScaleTerrain`
  and `OverrideTerrainVolumes`; add:

```csharp
    /// <summary>Client UX spec → Physical collision: the edited piece rests on the Solid tops under its footprints, its own volumes ignored.</summary>
    static TableState? Rest(TableState? d, Guid id)
    {
        if (d?.Terrain.FirstOrDefault(t => t.Id == id) is not { } piece) return d;
        var z = Collision.RestHeight(piece.WorldVolumes(), d.Terrain.Where(t => t.Id != id).SelectMany(t => t.WorldVolumes()).ToList());
        return d with { Terrain = d.Terrain.Select(t => t.Id == id ? t with { Position = t.Position with { Z = z } } : t).ToList() };
    }
```

- [ ] **Step 4: Run** `dotnet test tests/Tabletop.GameServer.Tests` (stack up). A test that asserted a hand move's or a draft
  piece's Z as sent now reads the rested Z: change the expectation, not the rest.
- [ ] **Step 5: Commit** `Rest every stored placement on what is under its base`, with the two trailer lines.

### Task 5: The move region states its separation and whether it passes models

**Model:** sonnet

**Files:** modify `libs/Tabletop.Protocol/Asks.cs`, `tests/Tabletop.Protocol.Tests/AskTests.cs`; in `server/game/Rules/GrimdarkFuture/`:
`GfMoves.MoveStep.cs:56`, `GfMoves.Terrain.cs:52`, `GfMoves.cs:401,427`, `GfContainers.cs:60`; create `tests/Tabletop.GameServer.Tests/GfContactTests.cs`;
regenerate `viewer/src/lib/protocol/wire.generated.ts`.

**Interfaces:** Produces `MoveRegion(…, bool Stoppable = false, double? Separation = null, bool PassesModels = false)`:
`Separation` is how far the moving models must stay from models of other units (null: no limit); `PassesModels` is true when
the unit may move through models. Set: the move step `Separation: ignoreSeparation || aircraft ? null : GfNames.Separation,
PassesModels: rules.PassesImpassable`; `ForcedMoveRegion` `GfNames.Separation` and `rules.PassesImpassable`; the two
`RepositionStep` Asks and `PlaceSurvivorsStep` `GfNames.Separation`. The close-in (`GfMelee.cs:163`) and consolidation (`:323`) keep null.

- [ ] **Step 1: Failing tests.** `AskTests`:

```csharp
    [Fact]
    public void A_move_region_carries_its_separation_and_whether_it_passes_models()
    {
        var plain = new MoveRegion([Guid.CreateVersion7()], 6, [], [], 6);
        Assert.Equal(((double?)null, false), (plain.Separation, plain.PassesModels));
        var back = Assert.IsType<MoveRegion>(WireJson.Deserialize<LegalSet>(WireJson.Serialize<LegalSet>(plain with { Separation = 1, PassesModels = true })));
        Assert.Equal(((double?)1, true), (back.Separation, back.PassesModels));
    }
```

  `tests/Tabletop.GameServer.Tests/GfContactTests.cs`:

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>p.7: "Models may never be within 1” of models from other units, unless they are taking a Charge action, and may never move through other models".</summary>
public class GfContactTests
{
    // A's one model at (0, -18.5), B's at (x2, -18.5); bases 1.26".
    static Sim Game(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    {
        a = Unit("A", "4+", "4+", rules, 1, [], Melee("Claws", 1)); b = Unit("B", "4+", "6+", [], 1, []);
        return Sim.Gf(State([a], [b], x1: 0, x2: x2, enforcement: e), 6, 6, 6, 6, 6, 6);
    }
    static Sim Advancing(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    { var sim = Game(e, out a, out b, x2, rules); sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance)); return sim; }
    static MoveRegion Region(Sim sim) => Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
    static ModelRelocation To(Sim sim, Guid m, double x) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, -18.5, 0), new Yaw(0))));

    [Fact]
    public void An_advance_keeps_one_inch_a_charge_does_not_and_a_flyer_passes_models()
    {
        var advance = Advancing(Enforcement.Strict, out _, out _);
        Assert.Equal(((double?)1, false), (Region(advance).Separation, Region(advance).PassesModels));
        var charge = Game(Enforcement.Strict, out var c, out var target, x2: 12);
        charge.Start(GfNames.ChargeSeq, new SequenceContext(c.Id, [], [], GfNames.Charge));
        charge.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Null(Region(charge).Separation);
        Assert.True(Region(Advancing(Enforcement.Strict, out _, out _, 3, P("Flying"))).PassesModels);
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Protocol.Tests --filter A_move_region_carries` and
  `dotnet test tests/Tabletop.GameServer.Tests --filter GfContactTests` — fail to compile.
- [ ] **Step 3: Implement** the two parameters on `MoveRegion` (with a `<summary>` line each) and the named arguments at the five
  sites above, e.g. `GfMoves.MoveStep.cs:56`:
  `new MoveRegion(Lookup.AliveModels(ctx.State, unit), remaining, impassable, difficult, cap, containment, aircraft ? GfNames.AircraftMove : null, stoppable, ignoreSeparation || aircraft ? null : GfNames.Separation, rules.PassesImpassable)`.
- [ ] **Step 4: Run** both filters; `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`; `cd viewer && npm run check && npm test`.
- [ ] **Step 5: Commit** `State a move's separation and pass-through on its region`, with the two trailer lines.

### Task 6: p.7's contact breaches

**Model:** opus

**Files:** create `server/game/Rules/GrimdarkFuture/GfMoves.Contact.cs`; modify `GfNames.cs` (the `Move*Rule` constants),
`GfRules.Moves.cs`, `server/game/Rules/IGameRules.cs`, `server/game/Dispatch/HandMoves.cs`,
`tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences`), `GfContactTests.cs`, `HandMovesTests.cs`.

**Interfaces:**
- Consumes: Task 4's `Battlefield.Settle(state, modelId, placement)`, Task 5's `MoveRegion.Separation/PassesModels`,
  `GfAnswers.Moves(GameState, MoveRegion, Destinations)`, `Overlap.Bases`, `Bodies.AsBlockingVolume`, `Movement.Check`, `Distance.EdgeToEdge`.
- Produces: `GfNames.MoveOverlapRule = "move_overlap"`, `MoveThroughModelsRule = "move_through_models"`, `MoveSeparationRule = "move_separation"`;
  `public static AnswerBreachFound? GfMoves.ContactBreach(GameState, MoveRegion, Destinations)`, `GfMoves.OverlapBreach(GameState, IReadOnlyList<ModelRelocation>)`;
  `IGameRules.PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => null` (wrappers forward it). `AnswerBreach`
  chains `?? GfMoves.ContactBreach(state, region, moves)` last; `HandMoves.Judge` returns `move_overlap` for a legal placement that
  overlaps, and appends the overlap's detail to a `hand_move` breach.

- [ ] **Step 1: Failing tests.** Append to `GfContactTests`:

```csharp
    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) => sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);
    static string? Breach(Sim sim, params ModelRelocation[] moves) => GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations(moves))?.Rule;

    [Fact]
    public void Under_strict_landing_on_crossing_or_within_an_inch_of_another_unit_is_refused()
    {
        var sim = Advancing(Enforcement.Strict, out var a, out _);                       // B at x 3
        var m = a.Models[0].Id;
        foreach (var (x, rule) in new[] { (3.5, GfNames.MoveOverlapRule), (5.5, GfNames.MoveThroughModelsRule), (1.5, GfNames.MoveSeparationRule) })
        {
            Assert.Equal(rule, Breach(sim, To(sim, m, x)));
            Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, To(sim, m, x))).Reason);
        }
        Assert.IsType<Applied>(Move(sim, To(sim, m, 0.5)));                              // 1.24" edge to edge
    }

    [Fact]
    public void Under_warn_an_overlap_is_applied_with_a_warning()
    {
        var sim = Advancing(Enforcement.Warn, out var a, out _);
        Assert.NotNull(Assert.IsType<Applied>(Move(sim, To(sim, a.Models[0].Id, 3.5))).Warning);
    }

    [Fact]
    public void A_charge_ends_in_base_contact_but_not_on_the_base()
    {
        var sim = Game(Enforcement.Strict, out var a, out var b, x2: 12);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Equal(GfNames.MoveOverlapRule, Breach(sim, To(sim, a.Models[0].Id, 11)));
        Assert.IsType<Applied>(Move(sim, To(sim, a.Models[0].Id, 10.74)));             // touching: within 1" is the Charge's
    }

    [Fact]
    public void A_flyer_moves_through_a_model_but_not_onto_it()
    {
        var sim = Advancing(Enforcement.Strict, out var a, out _, 3, P("Flying"));
        Assert.Null(Breach(sim, To(sim, a.Models[0].Id, 5.5)));
        Assert.Equal(GfNames.MoveOverlapRule, Breach(sim, To(sim, a.Models[0].Id, 3.5)));
    }

    [Fact]
    public void Leaving_a_model_it_overlaps_is_not_moving_through_it()
    {
        var sim = Game(Enforcement.Strict, out var a, out _);
        sim.Append([new ModelsMoved([To(sim, a.Models[0].Id, 2.5)])]);                   // left on B's base by a silent hand move
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.Null(Breach(sim, To(sim, a.Models[0].Id, -2)));
    }

    [Fact]
    public void A_wrapped_Grimdark_Future_still_judges_a_placement_overlap()
    {
        var sim = Game(Enforcement.Strict, out var a, out _);
        Assert.Equal(GfNames.MoveOverlapRule, new WithSequences(GrimdarkFutureRules.Instance).PlacementBreach(sim.State, [To(sim, a.Models[0].Id, 3)])!.Rule);
    }
```

  Append to `HandMovesTests`:

```csharp
    [Fact]
    public void Strict_refuses_placing_the_picked_unit_on_its_own_model()
    {
        var h = AtDeployment(Enforcement.Strict);
        var a = h.Units(0);
        h.Answer(0, new SelectedPieces([a[0]]));
        var all = TestGames.Relocations(h.State, a[0]);
        Assert.Equal(MoveOverlapRule, HandMoves.Judge(h.State, new MoveModels([all[0], all[1] with { To = all[0].To }]), TeamOf(h, 0))!.Rule);
    }
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.GameServer.Tests --filter "GfContactTests|HandMovesTests"` — fails to compile.
- [ ] **Step 3: Implement** `server/game/Rules/GrimdarkFuture/GfMoves.Contact.cs`:

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.7's contact clauses for a move answer, read as the steps apply it and judged at the rested landing. "Or be placed in physically
    /// impossible locations": move_overlap, a landing on any other model's base, the moving unit's own landings included. "May never move
    /// through other models or units (friendly or enemy), even if they are taking a Charge action": move_through_models, the base swept from
    /// its start across a model the answer does not move, less one it already overlaps; not for a region that PassesModels (p.13 Flying,
    /// Aircraft). "May never be within 1” of models from other units, unless they are taking a Charge action": move_separation, the region's
    /// Separation, which a Charge's regions leave null.
    /// </summary>
    public static AnswerBreachFound? ContactBreach(GameState state, MoveRegion region, Destinations answer) =>
        Contact(state, GfAnswers.Moves(state, region, answer), region.Separation, region.PassesModels);

    /// <summary>A hand placement (IGameRules.PlacementBreach) is held to "physically impossible locations" only.</summary>
    public static AnswerBreachFound? OverlapBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => Contact(state, moves, null, passesModels: true);

    static AnswerBreachFound? Contact(GameState state, IReadOnlyList<ModelRelocation> answered, double? separation, bool passesModels)
    {
        var p = state.Projection;
        var moves = answered.Where(m => m.To is OnTable && p.Models.ContainsKey(m.ModelId)).ToList();
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        var landed = moves.ToDictionary(m => m.ModelId, m => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement)));
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable)
            .Select(x => (x.Id, x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var everyone = standing.Concat(landed.Select(kv => (Id: kv.Key, p.Models[kv.Key].UnitId, At: kv.Value))).ToList();
        foreach (var m in moves.Where(m => m.From is not OnTable f || f.Placement.Position.Horizontal.DistanceTo(landed[m.ModelId].Placement.Position.Horizontal) > 1e-6))
        {                                                                                  // a model answered where it stands has not moved
            var (name, unit, at) = (p.Models[m.ModelId].Slot.Name, p.Models[m.ModelId].UnitId, landed[m.ModelId]);
            if (everyone.Any(o => o.Id != m.ModelId && Overlap.Bases(at, o.At)))
                return new(GfNames.MoveOverlapRule, $"{name} would be placed on another model's base: models may never be placed in physically impossible locations (p.7)");
            if (!passesModels && m.From is OnTable && Battlefield.Placed(state, m.ModelId) is { } start)
            {
                var crossed = standing.Where(o => !Overlap.Bases(start, o.At)).Select(o => Bodies.AsBlockingVolume(o.Id, o.At)).ToList();
                if (Movement.Check(start, at.Placement.Position.Horizontal, 0, crossed, [], null, null).CrossesImpassable)
                    return new(GfNames.MoveThroughModelsRule, $"{name} would move through another model: models may never move through other models or units (p.7)");
            }
            if (separation is { } gap && everyone.Any(o => o.UnitId != unit && Tabletop.Geometry.Distance.EdgeToEdge(at, o.At) < gap - 1e-6))
                return new(GfNames.MoveSeparationRule, $"{name} would end within {gap:0.#}\" of another unit's model: models may never be within 1\" of models from other units (p.7)");
        }
        return null;
    }
}
```

  `GfRules.Moves.cs`: append `?? GfMoves.ContactBreach(state, region, moves)` to `AnswerBreach`, and add
  `public AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => GfMoves.OverlapBreach(state, moves);`.
  `IGameRules.cs`, under *Asked by dispatch's hand-move judge*: `/// <summary>A rule a hand placement breaks wherever it is legal to make (OPR p.7: a model on another's base). Null when nothing is broken. Wrapping rulesets must forward it.</summary>`
  `AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => null;`.
  `WithSequences`: `public AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => inner.PlacementBreach(state, moves);`.
  `HandMoves.Judge` — the loop records the first outside-deployment relocation instead of returning, then:

```csharp
        var overlap = state.Rules.PlacementBreach(state, command.Moves);
        if (outside is { } b) return overlap is null ? b : b with { Detail = $"{b.Detail}; {overlap.Detail}" };
        return overlap is null ? null : new Breach(overlap.Rule, overlap.Detail, new StrictRefuses(RefusalReason.Illegal));
```

  Strike *"The 1" separation and coherency stay client-flagged"* and *"p.7's 1" is client-flagged"* from `GfMoves.cs`'s
  `PathBreach` and `ApplyPlacement` summaries and the comment at line 304: the separation is `ContactBreach`'s.
- [ ] **Step 4: Run** `dotnet test tests/Tabletop.GameServer.Tests`. A test whose fixture ends a move on, through or within 1"
  of another unit now breaches: move the fixture's numbers apart, never weaken the check.
- [ ] **Step 5: Commit** `Judge p.7's overlap, pass-through and 1-inch separation under enforcement`, with the two trailer lines.

### Task 7: Flat-topped hill meshes

**Model:** sonnet

**Files:** create `scripts/TerrainMeshes/{Tabletop.TerrainMeshes.csproj,Plateau.cs,Glb.cs,Program.cs}`, `scripts/terrain-meshes`,
`tests/Tabletop.TerrainMeshes.Tests/{Tabletop.TerrainMeshes.Tests.csproj,PlateauTests.cs}`; modify `Tabletop.sln`
(`dotnet sln add` both under the `scripts` and `tests` folders), `client/src/Core/BuiltinContent.cs`,
`tests/Tabletop.Client.Tests/BuiltinContentTests.cs`; regenerate `client/content/builtin/terrain/hill.glb`, add `hill-rect.glb`.

**Interfaces:** The built-in meshes stay in the unit box `MeshLoader.LoadBuiltin` fits (glTF Y-up, x and z in [-0.5, 0.5], y in
[0, 1], one node, one mesh, one primitive, interleaved POSITION+NORMAL with stride 24, one material, as the current `hill.glb`).
`hill` is round (hill, large-hill, steep-hill, beast-lair); `hill-rect` is square (small-dunes, hill-small), so a rect footprint draws
its corners. Produces `Plateau.Round(int segments = 48)`, `Plateau.Square()`, `Plateau.Rim = 0.15f`, `Glb.Of(string name, IReadOnlyList<(Vector3 Position, Vector3 Normal)>, float[] colour)`.

- [ ] **Step 1: Failing tests.** The two csproj files copy `scripts/ArmyBooks/Tabletop.ArmyBooks.csproj` (no project reference;
  `RootNamespace`/`AssemblyName` `Tabletop.TerrainMeshes`) and `tests/Tabletop.ArmyBooks.Tests/Tabletop.ArmyBooks.Tests.csproj`
  (referencing `../../scripts/TerrainMeshes/Tabletop.TerrainMeshes.csproj`, no fixtures). `PlateauTests.cs`:

```csharp
using System.Numerics;
using System.Text.Json;

namespace Tabletop.TerrainMeshes.Tests;

public class PlateauTests
{
    public static TheoryData<string> Shapes => new() { "round", "square" };
    static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Of(string shape) => shape == "round" ? Plateau.Round() : Plateau.Square();

    [Theory, MemberData(nameof(Shapes))]
    public void Fills_the_unit_box_with_a_plateau_inside_the_rim(string shape)
    {
        var v = Of(shape);
        Assert.All(v, x => Assert.InRange(x.Position.Y, 0f, 1f));
        Assert.Equal((0.5f, -0.5f), (MathF.Round(v.Max(x => x.Position.X), 4), MathF.Round(v.Min(x => x.Position.Z), 4)));
        Assert.All(v.Where(x => x.Normal == Vector3.UnitY), x => Assert.Equal(1f, x.Position.Y));
        Assert.All(v.Where(x => x.Position.Y == 1f), x => Assert.True(MathF.Max(MathF.Abs(x.Position.X), MathF.Abs(x.Position.Z)) <= 0.5f - Plateau.Rim + 1e-4f));
    }

    [Theory, MemberData(nameof(Shapes))]
    public void Every_triangle_faces_its_normal_and_up(string shape)
    {
        var v = Of(shape);
        Assert.Equal(0, v.Count % 3);
        for (var i = 0; i < v.Count; i += 3)
        {
            var (a, b, c) = (v[i].Position, v[i + 1].Position, v[i + 2].Position);
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), v[i].Normal) > 0);   // counter-clockwise from the front: glTF's front face
            Assert.True(v[i].Normal.Y > 0);
        }
    }

    [Fact]
    public void Writes_one_binary_gltf_with_an_interleaved_buffer()
    {
        var glb = Glb.Of("hill", Plateau.Round(), [0.4f, 0.5f, 0.3f, 1]);
        Assert.Equal((0x46546C67u, (uint)glb.Length), (BitConverter.ToUInt32(glb, 0), BitConverter.ToUInt32(glb, 8)));
        var json = JsonDocument.Parse(glb.AsMemory(20, (int)BitConverter.ToUInt32(glb, 12)));
        var position = json.RootElement.GetProperty("accessors")[0];
        Assert.Equal((Plateau.Round().Count, 1f), (position.GetProperty("count").GetInt32(), position.GetProperty("max")[1].GetSingle()));
    }
}
```

  `BuiltinContentTests`: `[Fact] public void Rect_hills_draw_the_square_plateau() { Assert.EndsWith("hill-rect.glb", BuiltinContent.TerrainMeshFile("hill-small")); Assert.EndsWith("hill-rect.glb", BuiltinContent.TerrainMeshFile("small-dunes")); Assert.Contains("hill-rect", BuiltinContent.TerrainMeshGroups); }`
- [ ] **Step 2: Run** `dotnet test tests/Tabletop.TerrainMeshes.Tests` and `dotnet test tests/Tabletop.Client.Tests --filter Rect_hills` — fail.
- [ ] **Step 3: Implement.** `Plateau.cs`:

```csharp
using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>A flat-topped hill in the built-ins' unit box: a plateau at y = 1 and a sloped rim inside the footprint (client UX spec → Physical collision). No bottom face.</summary>
public static class Plateau
{
    public const float Rim = 0.15f;

    public static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Round(int segments = 48) =>
        Build(Enumerable.Range(0, segments).Select(i => 2 * MathF.PI * i / segments).Select(t => new Vector2(MathF.Cos(t), MathF.Sin(t)) * 0.5f).ToList());

    public static IReadOnlyList<(Vector3 Position, Vector3 Normal)> Square() => Build([new(0.5f, 0.5f), new(-0.5f, 0.5f), new(-0.5f, -0.5f), new(0.5f, -0.5f)]);

    static List<(Vector3, Vector3)> Build(IReadOnlyList<Vector2> ring)
    {
        var tris = new List<(Vector3, Vector3)>();
        static Vector3 Ground(Vector2 p) => new(p.X, 0, p.Y);
        static Vector3 Top(Vector2 p) => new(p.X * (1 - 2 * Rim), 1, p.Y * (1 - 2 * Rim));
        for (var i = 0; i < ring.Count; i++)
        {
            var (a, b) = (ring[i], ring[(i + 1) % ring.Count]); var mid = (a + b) / 2;
            var n = Vector3.Normalize(Vector3.Cross(Ground(b) - Ground(a), Top(a) - Ground(a)));
            if (n.X * mid.X + n.Z * mid.Y < 0) n = -n;                                   // the slope faces away from the axis
            Tri(tris, Ground(a), Ground(b), Top(b), n);
            Tri(tris, Ground(a), Top(b), Top(a), n);
            Tri(tris, Vector3.UnitY, Top(a), Top(b), Vector3.UnitY);
        }
        return tris;
    }

    /// <summary>One flat-shaded triangle, wound counter-clockwise seen from the side its normal points to.</summary>
    static void Tri(List<(Vector3, Vector3)> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) (b, c) = (c, b);
        tris.AddRange([(a, n), (b, n), (c, n)]);
    }
}
```

  `Glb.cs` builds the JSON with `JsonSerializer.Serialize` of an anonymous object: `asset {generator "scripts/TerrainMeshes", version "2.0"}`,
  `scene 0`, one scene, one node `{name, mesh 0}`, one mesh with one primitive `{attributes {POSITION 0, NORMAL 1}, mode 4, material 0}`,
  one material `{name, pbrMetallicRoughness {baseColorFactor colour, metallicFactor 0}}`, `buffers [{byteLength}]`, `bufferViews [{buffer 0,
  byteOffset 0, byteLength, byteStride 24, target 34962}]`, accessors `{bufferView 0, byteOffset 0, componentType 5126, count, type "VEC3", min, max}`
  and `{bufferView 0, byteOffset 12, componentType 5126, count, type "VEC3"}`; the binary is six floats per vertex (`Buffer.BlockCopy`). Then:

```csharp
        var (text, data) = (Pad(Encoding.UTF8.GetBytes(json), (byte)' '), Pad(bin, 0)); using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + text.Length + 8 + data.Length));
        w.Write((uint)text.Length); w.Write(0x4E4F534Au); w.Write(text);
        w.Write((uint)data.Length); w.Write(0x004E4942u); w.Write(data);
        return s.ToArray();
    }
    static byte[] Pad(byte[] b, byte with) => b.Concat(Enumerable.Repeat(with, (4 - b.Length % 4) % 4)).ToArray();
```

  `Program.cs` writes `hill.glb` (`Plateau.Round()`) and `hill-rect.glb` (`Plateau.Square()`) into `args[0]`, default
  `client/content/builtin/terrain`, colour `[0.4f, 0.5f, 0.3f, 1]` (the current hill's). `scripts/terrain-meshes` is `scripts/army-books`
  with `scripts/TerrainMeshes`. `BuiltinContent.cs`: `small-dunes` and `hill-small` take group `"hill-rect"`; `TerrainMeshGroups` gains
  `"hill-rect"`; the comment's "ten meshes" becomes "eleven".
- [ ] **Step 4: Run** `./scripts/terrain-meshes`, then both test commands — pass. The editor writes `hill-rect.glb.import` the next
  time the owner opens the project; never hand-write it.
- [ ] **Step 5: Commit** the generator, the tests, both meshes and `BuiltinContent`: `Draw built-in hills flat-topped`, with the two trailer lines.
