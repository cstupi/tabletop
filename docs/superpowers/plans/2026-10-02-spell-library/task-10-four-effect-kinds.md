# Task 10: Four effect kinds and the idle activation

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfCasting.cs` (`EffectsStep` pushes `spell-test`; `SpellTest()` sequence; `ConsequenceStep`)
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`count_as_terrain` in `MoveStep` and `ActivationTerrainStep`; `ApplyForcedMove`; `Idle()` sequence)
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `spell-test`, `idle`; `ActionSequence("Idle")`)
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Scheduling.cs` (`ReadAnswer`: Idle runs a sequence)
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (`IdleSeq`, `SpellTestSeq`)
- Test: `tests/Tabletop.GameServer.Tests/GrammarAdditionTests.cs`, `tests/Tabletop.GameServer.Tests/GrimdarkFutureRulesTests.cs`

**Interfaces:**
- Consumes: `EffectKind.CountAsTerrain/TakeTest/SetCondition/Move`, `TargetTerrain`, `Effect.TargetCondition` (Task 3); `RollEffects.Consume`, `LiveSet.Read` (Task 6).
- Produces: `GfNames.IdleSeq = "idle"`, `GfNames.SpellTestSeq = "spell-test"`; `GfCasting.SpellTest()`; `GfMoves.Idle()`; `GfMoves.ApplyForcedMove(StepContext, Guid unit, double allowance, IReadOnlyList<ModelRelocation> proposed) → IReadOnlyList<StateChange>`.

Spec → *Grammar additions*, kinds 13 to 16, and *An Idle activation is an activation*. `count_as_terrain`: difficult caps the next move at 6" (p.11), dangerous runs a dangerous terrain test at the next activation's start (p.11, *"or is activated in it"*). `take_test`: the target takes a morale test now, with p.10's ordinary consequence; the children apply when it fails. `set_condition` sets a condition; `move` lets the caster's team move the target up to its amount.

- [ ] **Step 1: Write the failing tests**

Add to `GrammarAdditionTests` (it has `Props`, `Rule`, `HitMod` from Task 9):

```csharp
    static EffectDocument Kind(EffectKind kind, EffectSubject subject, EffectDuration duration, int? amount = null, TargetTerrain? terrain = null, string? condition = null, string? roll = null, params EffectDocument[] children) =>
        new(subject, 10, kind, roll, null, null, null, null, null, null, amount, false, null, null, null, duration, [], children, terrain, condition);
    static EntryDocument EnemySpell(string name, params EffectDocument[] rows) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24), rows);
    static readonly PropertySet Kinds = TestProperties.WithSpells(
        EnemySpell("Mire", Kind(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once, terrain: TargetTerrain.Difficult)),
        EnemySpell("Spikes", Kind(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once, terrain: TargetTerrain.Dangerous)),
        EnemySpell("Fury", Kind(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: Kind(EffectKind.SetCondition, EffectSubject.Self, EffectDuration.Interaction, condition: "fatigued"))),
        EnemySpell("Hypnosis", Kind(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: Kind(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, amount: 6))));

    static void GrantOn(GameState s, Guid unit, string spell, Guid caster) =>
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(unit, new Grant(Guid.CreateVersion7(), Kinds.Entry(spell)!.Effects[0].Id, spell, caster, 1)), null));

    [Fact]
    public void Counting_as_in_difficult_terrain_caps_the_next_move_at_six_and_is_spent_by_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(4));
        GrantOn(sim.State, a.Id, "Mire", a.Id);
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        Assert.Equal(6, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 5 } }))]));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void Counting_as_in_dangerous_terrain_tests_every_model_at_the_next_activation()
    {
        var a = Unit("A", "4+", "4+", [], 3, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(1, 6, 6));
        GrantOn(sim.State, a.Id, "Spikes", a.Id);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Equal(3, sim.Steps("test").Single().Rolls.Single().Dice.Count);
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_failed_forced_test_leaves_the_target_shaken_and_fatigued_and_a_passed_one_neither()
    {
        foreach (var (die, failed) in new[] { (1, true), (6, false) })
        {
            var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
            var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6, die));
            sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
            sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
            sim.Answer(Samples.Team1, new Chosen($"Fury|{wizard.Models[0].Id}"));
            sim.Answer(Samples.Team1, new Passed());
            sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
            Assert.Equal((failed, failed), (sim.Unit(t.Id).Conditions.Contains(GfNames.Shaken), sim.Unit(t.Id).Conditions.Contains(GfNames.Fatigued)));
        }
    }

    [Fact]
    public void A_forced_test_at_a_shaken_unit_fails_without_a_roll_and_its_children_apply()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6), new ConditionSet(new Subject(SubjectKind.Unit, t.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fury|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Contains(GfNames.Fatigued, sim.Unit(t.Id).Conditions);
    }

    [Fact]
    public void A_failed_hypnosis_lets_the_casters_team_move_the_target_up_to_six_inches()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 1, []);
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(6, 1));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Hypnosis|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((6.0, [t.Models[0].Id]), (region.Allowance, region.ModelIds));
        var m = t.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 5 } }))]));
        Assert.Equal(at.Position.X + 5, ((OnTable)sim.Model(m).Location).Placement.Position.X, 3);
    }
```

Add to `GrimdarkFutureRulesTests`:

```csharp
    [Fact]
    public void An_idle_answer_runs_the_idle_sequence()
    {
        var u = Armies.Unit("U", "4+", "4+", [], 1, []);
        var state = Armies.State([u], [Armies.Unit("E", "4+", "4+", [], 1, [])]);
        var idle = Tabletop.GameServer.Scheduler.ActivationOption.Idle;
        Assert.Equal(GfNames.IdleSeq, GrimdarkFutureRules.Instance.ActionSequence(idle));
        var act = GrimdarkFutureRules.Instance.ReadAnswer(state, Tabletop.Protocol.Tests.Samples.Team1, new Chosen(Tabletop.GameServer.Scheduler.ActivationOption.Id(u.Id, idle)));
        Assert.True(act!.RunsSequence);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GrammarAdditionTests|FullyQualifiedName~GrimdarkFutureRulesTests"`
Expected: FAIL: the move allowance is 12, no dangerous test, no morale test is pushed, `ActionSequence("Idle")` is null.

- [ ] **Step 3: `count_as_terrain`**

In `GfMoves`, add:

```csharp
    /// <summary>A count_as_terrain row of this type live on the holder (spell library, kind 13), recorded as read so the step that acts spends it.</summary>
    static bool CountsAsIn(LiveSet live, TargetTerrain terrain)
    {
        var rows = live.Effects(Side.Attacker, EffectKind.CountAsTerrain).Where(x => x.Effect.TargetTerrain == terrain).ToList();
        foreach (var x in rows) live.Read(x.Source);
        return rows.Count > 0;
    }
```

In `MoveStep.Run`, right after `var allowance = StatReader.ActionDistance(live, Side.Attacker, action);`:

```csharp
            // p.11: "all models in the unit may not move more than 6" for that movement", for a unit that counts as being in difficult terrain.
            var striderOrFlying = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");
            if (!striderOrFlying && CountsAsIn(live, TargetTerrain.Difficult)) allowance = Math.Min(allowance, GfNames.DifficultCap);
```

(Task 6 already spends the read grants in `case 0` when the move moved anything.)

In `ActivationTerrainStep.Run`, replace the body after the Flying check:

```csharp
            var live = Lone(ctx, unit, ctx.Frame.Context.Action);
            // p.11 "or is activated in it": a unit that counts as being in dangerous terrain tests every model, as if all stood in it.
            var everywhere = CountsAsIn(live, TargetTerrain.Dangerous);
            var standing = everywhere ? Lookup.LivingModels(ctx.State, unit) : StandingInDangerous(ctx.State, unit);
            if (standing.Count == 0) return new Complete([]);
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete(RollEffects.Consume(live), new Dictionary<string, int> { ["dangerous_dice"] = DangerousDice(ctx.State, standing) }, Repeat: true);
```

- [ ] **Step 4: `take_test`, `set_condition` and `move`**

In `GfNames`: `public const string IdleSeq = "idle", SpellTestSeq = "spell-test";`.

In `GfCasting.EffectsStep.Run`, after the hits push:

```csharp
            if (holding.Any(e => e.Kind == EffectKind.TakeTest) && ctx.History.Children.Count == 0)
                return new Push(GfNames.SpellTestSeq, new SequenceContext(casterUnit, [targetUnit], [], null));
```

Add the sequence and its step to `GfCasting`:

```csharp
    /// <summary>take_test (spell library, kind 14): the target's morale test with p.10's ordinary consequence, then the row's children when it failed.</summary>
    public static Sequence SpellTest() => new(GfNames.SpellTestSeq,
        [new GfAttack.PushStep("test", GfNames.MoraleTest, ctx => new SequenceContext(ctx.Frame.Context.TargetUnitIds[0], [], [], "spell")), new ConsequenceStep()]);

    sealed class ConsequenceStep : IStep
    {
        public string Name => "consequence";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            if (!Failed(ctx.State, ctx.Frame.Id)) return new Complete([]);
            var spell = ctx.Properties.Entry(Picked(ctx.Frame.Parent!).Spell)!;
            var children = spell.Effects.Where(e => e.Kind == EffectKind.TakeTest).SelectMany(e => e.Children).ToList();
            var changes = children.Where(c => c.Kind == EffectKind.SetCondition)
                .Select(c => (StateChange)new ConditionSet(new Subject(SubjectKind.Unit, target), c.TargetCondition!, true)).ToList();
            if (children.FirstOrDefault(c => c.Kind == EffectKind.Move) is not { } move) return new Complete(changes);
            if (ctx.History.LastClosed("move") is { } moved)
                return new Complete([.. changes, .. GfMoves.ApplyForcedMove(ctx, target, move.EffectiveAmount(null), GfAnswers.Moves(ctx.State, moved))]);
            var terrain = Battlefield.Terrain(ctx.State);
            return new OpenAsks([ctx.Ask("move", $"Move up to {move.EffectiveAmount(null)}\" in a straight line", ctx.ActorTeam!.Value,
                new MoveRegion(Lookup.AliveModels(ctx.State, target), move.EffectiveAmount(null),
                    terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList(),
                    terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Difficult)).Select(v => v.Id).ToList(), GfNames.DifficultCap), allowPass: true)]);
        }

        /// <summary>The morale test this frame pushed failed: its test step recorded passed = 0, or it finished with no model to test.</summary>
        static bool Failed(GameState state, Guid frameId)
        {
            var log = EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).ToList();
            var mine = log.FindIndex(p => p is SequenceStarted s && s.FrameId == frameId);
            var child = log.Skip(mine + 1).OfType<SequenceStarted>().FirstOrDefault(s => s.Sequence == GfNames.MoraleTest);
            if (child is null) return false;
            var done = log.OfType<StepCompleted>().LastOrDefault(s => s.FrameId == child.FrameId && s.Step == "test");
            return done?.Values is { } v && v.GetValueOrDefault("passed", 1) == 0;
        }
    }
```

In `GfMoves`, expose the move validation the forced move needs:

```csharp
    /// <summary>A move another team makes for this unit (a spell's move kind): the same settling, impassable and difficult checks as an action's
    /// move, measured against the allowance; dangerous terrain crossed is not tested, since no rule asks it of a forced move.</summary>
    public static IReadOnlyList<StateChange> ApplyForcedMove(StepContext ctx, Guid unit, double allowance, IReadOnlyList<ModelRelocation> proposed)
    {
        var terrain = Battlefield.Terrain(ctx.State);
        var impassable = Declared(terrain, TerrainDeclarations.Impassable); var difficult = Declared(terrain, TerrainDeclarations.Difficult);
        var moves = new List<ModelRelocation>();
        foreach (var m in proposed.Where(m => m.To is OnTable && ctx.Projection.Models.TryGetValue(m.ModelId, out var mm) && mm.UnitId == unit))
        {
            if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
            var settled = Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement);
            var check = Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, GfNames.DifficultCap, ctx.State.Board.Table.Size);
            if (check.WithinAllowance && !check.CrossesImpassable && !check.LeavesTable) moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
        }
        return moves.Count == 0 ? [] : [new ModelsMoved(moves)];
    }
```

- [ ] **Step 5: The idle activation and the registry**

In `GfMoves`: `public static Sequence Idle() => new(GfNames.IdleSeq, [new ActivationTerrainStep()]);` with a summary citing p.11's *"or is activated in it"* and p.10's idle activation.

In `GfRules.Sequences.cs`, add `GfMoves.Idle()` and `GfCasting.SpellTest()` to the catalog array, and `Scheduler.ActivationOption.Idle => GfNames.IdleSeq,` to `ActionSequence`'s switch.

In `GfRules.Scheduling.cs`, `ReadAnswer`'s `Chosen` arm:

```csharp
        Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out var action) && state.Projection.Units.ContainsKey(unit)
            => new Activation(unit, action, true, [new ActionRecorded(unit, action)]),
```

`TurnCycle.Read` finds the idle frame by `SequenceStarted.Context.Action == "Idle"`, as it does for any action. `GfHooks.FoughtMeleeThisActivation` and `GfCasting.TriedPicks` anchor on the four action sequences; an idle activation fights nothing and casts nothing, so neither needs `idle`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. The scheduler suites (`TurnCycleTests`, `AlternatingSchedulerTests`, `PlayerTurnSchedulerTests`, `SchedulerEndToEndTests`) still pass: a Shaken unit's activation now starts and finishes an `idle` frame, which ends the activation exactly as the empty one did. A test that counts the events of an idle activation now sees three more (`sequence_started`, the `activation-terrain` `step_completed`, `sequence_finished`); raise its expected count by three where one fails.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests
git commit -m "Add count_as_terrain, take_test, set_condition and move, and run a test on an idle activation in dangerous terrain"
```
