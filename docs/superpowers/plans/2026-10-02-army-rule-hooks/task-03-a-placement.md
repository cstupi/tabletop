# Task 3: A placement: *Teleport* and *Rapid Blink*

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`RepositionStep`'s place branch, `ApplyPlacement`, the window reopening, `activation-start` in `Hold`, `Advance`, `Rush`, `Idle`)
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs` (`activation-start` in `Charge`)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (three entries' rows)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: `RepositionStep`, `PointWindowStep`, `ReactionsAt` (Task 2); `EffectKind.Place`, `Effect.AmountDice` (Task 1).
- Produces: `GfMoves.ApplyPlacement(StepContext ctx, Guid unit, double distance, IReadOnlyList<Guid> models, IReadOnlyList<ModelRelocation> proposed) → IReadOnlyList<StateChange>`; the action sequences `[activation-terrain, activation-start, ...]`.

Spec → *A placement*. A placement crosses anything and checks only the landing: displacement at most the distance, on the table, outside impassable volumes (README → Rulings 3). The distance with `amount_dice` is rolled when the reaction is chosen: `amount` D6, each halved and rounded up (p.4: *"D3: To use these dice, simply roll a D6 and halve the result"*). *Teleport* shares the before-attacking checkpoint with casting; *Rapid Blink* is offered at `activation-start`.

- [ ] **Step 1: Write the failing tests**

Add to `ArmyRuleHookTests`:

```csharp
    static void Shift(Sim sim, Guid team, IEnumerable<Guid> models, double dx) =>
        sim.Answer(team, new Destinations(models.Select(m => { var at = ((OnTable)sim.Model(m).Location).Placement; return new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } })); }).ToList()));

    [Fact]
    public void Teleport_is_offered_before_attacking_at_three_inches_after_advance_six_after_rush_and_not_on_hold()
    {
        foreach (var (seq, action, inches) in new[] { (GfNames.AdvanceSeq, GfNames.Advance, 3.0), (GfNames.RushSeq, GfNames.Rush, 6.0) })
        {
            var a = Unit("A", "4+", "4+", [], 1, [P("Teleport")]);
            var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Core, new ScriptedDice(4));
            sim.Start(seq, new SequenceContext(a.Id, [], [], action));
            sim.Answer(Samples.Team1, new Passed());                                                    // no move
            var react = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
            sim.Answer(Samples.Team1, new Chosen(react.Choices.Single(c => c.Id.StartsWith("Teleport|")).Id));
            Assert.Equal(inches, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
            Shift(sim, Samples.Team1, a.Models.Select(m => m.Id), inches - 0.5);
            Assert.NotEqual("react", sim.AskForOrNull(Samples.Team1)?.Question);                         // once per activation: the reopened window offers nothing
        }
        var h = Unit("H", "4+", "4+", [], 1, [P("Teleport")]);
        var hold = new Sim(State([h], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Core, new ScriptedDice(4));
        hold.Start(GfNames.HoldSeq, new SequenceContext(h.Id, [], [], GfNames.Hold));
        Assert.Null(hold.AskForOrNull(Samples.Team1));
    }

    [Fact]
    public void A_placement_crosses_impassable_terrain_but_may_not_land_in_it()
    {
        var wall = new Tabletop.Content.TerrainPlacement(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("wall"), new Tabletop.Geometry.Point3(-9, -18.5, 0), new Tabletop.Geometry.Yaw(0), 1,
            [new Tabletop.Content.TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Solid, Tabletop.Geometry.Footprint.Rect(1, 6), new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Yaw(0), 0, 4, Tabletop.Geometry.TerrainDeclarations.Impassable, null))]);
        foreach (var (dx, lands) in new[] { (3.0, true), (2.0, false) })
        {
            var a = Unit("A", "4+", "4+", [], 1, [P("Teleport")]);
            var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -11, x2: 20, terrain: [wall]), GrimdarkFutureRules.Instance, Core, new ScriptedDice(4));
            sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
            sim.Answer(Samples.Team1, new Passed());
            sim.Answer(Samples.Team1, new Chosen($"Teleport|{a.Id:N}"));
            var before = ((OnTable)sim.Model(a.Models[0].Id).Location).Placement.Position.X;
            Shift(sim, Samples.Team1, [a.Models[0].Id], dx);
            Assert.Equal(lands ? before + dx : before, ((OnTable)sim.Model(a.Models[0].Id).Location).Placement.Position.X, 3);
        }
    }

    [Fact]
    public void Rapid_blink_rolls_a_d3_only_when_chosen_and_offers_only_its_holders()
    {
        var a = Unit("A", "4+", "4+", [], 3, []);
        a = a with { Models = [a.Models[0] with { Properties = [P("Rapid Blink")] }, a.Models[1], a.Models[2]] };
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5));
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen($"Rapid Blink|{a.Id:N}"));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((3.0, [a.Models[0].Id]), (region.Allowance, region.ModelIds));                   // a 5 halved and rounded up
        var declined = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5));
        declined.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        declined.Answer(Samples.Team1, new Passed());
        Assert.DoesNotContain(declined.Emitted.OfType<StepCompleted>(), s => s.Rolls.Count > 0);
    }

    [Fact]
    public void Rapid_blink_boost_rolls_two_dice()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Rapid Blink"), P("Rapid Blink Boost")]);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 2));
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen($"Rapid Blink Boost|{a.Id:N}"));
        Assert.Equal(4.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);       // 3 + 1
    }
```

`Armies.State` places one model per unit at `(x, unitIndex·12 + ordinal·1.5 − 20)`, so the single model stands at (−11, −18.5), inside the wall's 6" span along y. The wall spans x −9.5 to −8.5: a 3" shift puts the centre at −8, beyond it; a 2" shift puts it at −9, inside. A landing is judged by its centre, as `Containment.VolumesContaining` judges standing in dangerous terrain.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~ArmyRuleHookTests`
Expected: FAIL: no `Teleport` option, no `Rapid Blink` react Ask.

- [ ] **Step 3: The rows**

In the part file, replace each of these entries' `"effects": []` with the array beside its name:

```json
"Teleport": [{"subject": "self", "ordinal": 10, "kind": "place", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["before-attacking"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Advance", "Charge"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["activation"]}]}, {"subject": "self", "ordinal": 10, "kind": "place", "amount": 6, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["before-attacking"]}, {"kind": "action_taken", "subject": "self", "comparison": "eq", "values": ["Rush"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["activation"]}]}]
"Rapid Blink": [{"subject": "self", "ordinal": 10, "kind": "place", "amount": 1, "amountDice": "d3", "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["activation-start"]}]}]
"Rapid Blink Boost": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Rapid Blink", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Rapid Blink"]}]}, {"subject": "self", "ordinal": 10, "kind": "place", "amount": 2, "amountDice": "d3", "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["activation-start"]}, {"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Rapid Blink"]}]}]
```

- [ ] **Step 4: The placement branch**

In `RepositionStep.Run`, before the `switch`, handle a `place` row:

```csharp
            if (row.Kind == EffectKind.Place) return Place(ctx, unit, entry, live, source, row);
```

and add to the class:

```csharp
        /// <summary>Phase 0: roll the distance when the row has dice (p.4's D3: a D6 halved, rounded up), recorded in tenths; then ask; then apply.</summary>
        static StepOutcome Place(StepContext ctx, Guid unit, string entry, LiveSet live, LiveProperty source, Effect row)
        {
            if (row.AmountDice is not null && !ctx.Frame.Values.ContainsKey("distance_tenths"))
            {
                var record = ctx.Roll(new Dice.RollPlan(entry, null, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), row.EffectiveAmount(null));
                var inches = record.Dice.Sum(d => (d.Raw + 1) / 2);
                return new Complete([], new Dictionary<string, int> { ["distance_tenths"] = inches * 10 }, Repeat: true);
            }
            var distance = row.AmountDice is null ? row.EffectiveAmount(null) : ctx.Frame.Value("distance_tenths") / 10.0;
            var holders = Lookup.AliveModels(ctx.State, unit).Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == entry)).ToList();
            var models = holders.Count > 0 ? holders : Lookup.AliveModels(ctx.State, unit).ToList();   // held by a grant: the whole unit
            if (ctx.History.LastClosed("move") is { } placed)
                return new Complete([.. ApplyPlacement(ctx, unit, distance, models, GfAnswers.Moves(ctx.State, placed)), .. RollEffects.Consume(live)]);
            return new OpenAsks([ctx.Ask("move", $"{entry}: place anywhere fully within {distance:0.#}\"", ctx.ActorTeam!.Value,
                new MoveRegion(models, distance, [], [], null), allowPass: true)]);
        }
```

An empty impassable and difficult list on the `MoveRegion` is how the client knows nothing is swept (README → Rulings 3). Add `ApplyPlacement` to `GfMoves`:

```csharp
    /// <summary>A placement (army-rule hooks → A placement): each listed model's landing, settled on the surface, is at most `distance` from where it
    /// stood and its base touches no impassable volume (LandsInImpassable). Nothing between is swept. A landing that fails stays where it was. The table edge
    /// is client-flagged (MoveStep reports Movement.Check's LeavesTable and does not refuse it); p.7's 1" is judged by GfMoves.ContactBreach (move_separation), as for every move.</summary>
    public static IReadOnlyList<StateChange> ApplyPlacement(StepContext ctx, Guid unit, double distance, IReadOnlyList<Guid> models, IReadOnlyList<ModelRelocation> proposed)
    {
        var impassable = Declared(Battlefield.Terrain(ctx.State), TerrainDeclarations.Impassable);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => models.Contains(m.ModelId) && m.To is OnTable && ctx.Projection.Models[m.ModelId].Location is OnTable))
        {
            var from = ((OnTable)ctx.Projection.Models[m.ModelId].Location).Placement.Position;
            var settled = Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement);
            var to = settled.Position;
            var displaced = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (displaced > distance + 1e-6) continue;
            if (Containment.VolumesContaining(Battlefield.At(ctx.State, m.ModelId, settled), impassable).Count > 0) continue;
            moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return moves.Count == 0 ? [] : [new ModelsMoved(moves)];
    }
```

- [ ] **Step 5: The window reopens after any child, and the activation-start step**

In `MoveStep.Run`'s default phase, replace `var cast = ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq);` with `var reacted = ctx.History.Children.Count > 0;` and use `Repeat: reacted`. In `CastingWindowStep`, `Repeat: ctx.History.Children.Count > 0`.

Add the activation-start window as the second step of `Hold()`, `Advance()`, `Rush()` and `Idle()` in `GfMoves`, and of `Charge()` in `GfMelee`:

```csharp
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new CastingWindowStep(), new ShootOrNotStep()]);
```

and likewise for the other four.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. A unit with no point rule completes `activation-start` at once, so scheduler and sequence tests see one more `step_completed` per activation and nothing else; raise any exact event count that fails by one per activation.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Offer Teleport before attacking and Rapid Blink at activation, as placements"
```
