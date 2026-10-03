# Task 2: A move after attacking: *Hit & Run* and *Harassing*

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfHooks.cs` (`ReactionsAt` at every point)
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`PointWindowStep`, `Reposition()`, `RepositionStep`, `ApplyMove`)
- Modify: `server/game/Rules/GrimdarkFuture/GfShooting.cs` (`after-shooting` last), `GfMelee.cs` (`after-melee` last), `GfRules.Sequences.cs` (register `reposition`)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (four entries' rows)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: `ConditionKind.AtPoint`, `LiveSet.AtPoint`, `Interaction.Point`, usage scopes (Task 1); `LiveSet.Read`, `RollEffects.Consume` (library Task 6).
- Produces: `GfMoves.PointWindowStep(string point, string name)`; `GfMoves.Reposition()` (sequence `reposition`, one step `reposition`); `GfMoves.ApplyMove(StepContext ctx, Guid unit, LiveSet live, double allowance, IReadOnlyList<ModelRelocation> proposed) → (List<StateChange> Changes, int SegmentTenths, int DangerousDice)`; a reaction's option id is `"{entry}|{unit:N}"` and its sequence context is `(unit, [], [], entry)`.

Spec → *A move after attacking*. On a property, `move`'s team is the holder's and the unit the holder's own. At `after-shooting` and `after-melee`, `ReactionsAt` collects the live `move` rows at that point; each becomes a reaction pushing `reposition`, whose step asks the move and applies it with the move's own validation and dangerous terrain test.

- [ ] **Step 1: Write the failing tests**

Add to `ArmyRuleHookTests` (a `partial class`; Task 1 created it):

```csharp
    static Sim Melee(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender)
    {
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "strike_back" }) sim.Answer(Samples.Team2, new Chosen("pass"));
        return sim;
    }

    static void MoveBy(Sim sim, Guid team, Tabletop.Content.Unit unit, double dx)
    {
        var moves = unit.Models.Select(m => { var at = ((OnTable)sim.Model(m.Id).Location).Placement; return new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } })); }).ToList();
        sim.Answer(team, new Destinations(moves));
    }

    [Fact]
    public void Hit_and_run_fighter_offers_its_team_a_three_inch_move_after_the_melee_once_a_round()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [P("Hit & Run Fighter")], Melee("Spear", 1));
        var sim = Melee(charger, defender);
        var react = Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal($"Hit & Run Fighter|{defender.Id:N}", react.Choices.Single().Id);
        sim.Answer(Samples.Team2, new Chosen(react.Choices.Single().Id));
        Assert.Equal(3, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal).Allowance);
        MoveBy(sim, Samples.Team2, defender, 3);
        Assert.Empty(sim.State.Projection.Stack);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "strike_back" }) sim.Answer(Samples.Team2, new Chosen("pass"));
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                   // once per round
    }

    [Fact]
    public void Hit_and_run_shooter_moves_after_shooting_and_harassing_once_across_both_points()
    {
        foreach (var rule in new[] { "Hit & Run Shooter", "Harassing" })
        {
            var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
            var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
            var react = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
            sim.Answer(Samples.Team1, new Chosen(react.Choices.Single().Id));
            MoveBy(sim, Samples.Team1, a, -2);
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
            Assert.Null(sim.AskForOrNull(Samples.Team1));
        }
    }

    [Fact]
    public void Harassing_boost_moves_six_inches()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Harassing"), P("Harassing Boost")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(2, 2));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        var react = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal($"Harassing Boost|{a.Id:N}", react.Choices.Single().Id);
        sim.Answer(Samples.Team1, new Chosen(react.Choices.Single().Id));
        Assert.Equal(6, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_shaken_unit_is_offered_no_move()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var defender = Unit("D", "4+", "4+", [], 1, [P("Hit & Run Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 40).ToArray()));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, defender.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        if (sim.AskForOrNull(Samples.Team2) is { Question: "strike_back" }) sim.Answer(Samples.Team2, new Chosen("pass"));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~ArmyRuleHookTests`
Expected: FAIL: no react Ask opens after the melee or the shooting.

- [ ] **Step 3: The rows**

In `server/game/Content/grimdark-future-army-rules.part.json`, replace each of these entries' `"effects": []` with the array beside its name:

```json
"Hit & Run Fighter": [{"subject": "self", "ordinal": 10, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-melee"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]
"Hit & Run Shooter": [{"subject": "self", "ordinal": 10, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-shooting"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]
"Harassing": [{"subject": "self", "ordinal": 10, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-shooting"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}, {"subject": "self", "ordinal": 10, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-melee"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]
"Harassing Boost": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Harassing", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Harassing"]}]}, {"subject": "self", "ordinal": 10, "kind": "move", "amount": 6, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Harassing"]}, {"kind": "at_point", "comparison": "eq", "values": ["after-shooting"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}, {"subject": "self", "ordinal": 10, "kind": "move", "amount": 6, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Harassing"]}, {"kind": "at_point", "comparison": "eq", "values": ["after-melee"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]
```

- [ ] **Step 4: The window at a point, and the reactions**

In `GfMoves`:

```csharp
    /// <summary>A reaction window at a named point (army-rule hooks → Hook points): `after-shooting`, `after-melee`, `activation-start`.</summary>
    public sealed class PointWindowStep(string point, string name) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx) => Interpreter.Windows.Run(ctx, point) ?? new Complete([]);
    }
```

`GfShooting.Shooting()` becomes `new(GfNames.Shooting, [new DeclareTargetsStep(), new FireGroupsStep(), new GfMoves.PointWindowStep(GfNames.AfterShooting, "after-shooting")])`, and `GfMelee.Melee()` appends `new GfMoves.PointWindowStep(GfNames.AfterMelee, "after-melee")` after `ConsolidateStep`.

Replace `GfHooks.ReactionsAt`:

```csharp
    /// <summary>
    /// Who may react at a point. At before-attacking the acting unit's team may cast if a caster can afford an untried spell (p.13). At every point,
    /// each unit the point concerns (the actor; at after-melee the charger and then the defender) offers its live move and place rows at that point
    /// (army-rule hooks spec), one reaction per entry, pushing `reposition`. A Shaken unit offers nothing: 3.5.1 p.10 says being idle means it "can't
    /// take any actions, or use any active special rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc.".
    /// </summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (frame.Context.ActorUnitId is not { } actor) return [];
        var reactions = new List<Reaction>();
        if (point == GfNames.BeforeAttacking && !Shaken(state, actor) && GfCasting.SpellOptions(state, props, actor).Count > 0 && Teams.Of(state, actor) is { } own)
            reactions.Add(new Reaction(own, "cast", "Cast a spell", GfNames.CastingSeq, new SequenceContext(actor, [], [], null)));
        IEnumerable<Guid> units = point == GfNames.AfterMelee ? [actor, .. frame.Context.TargetUnitIds.Take(1)] : [actor];
        foreach (var unit in units)
        {
            if (Shaken(state, unit) || Lookup.LivingModels(state, unit).Count == 0 || Teams.Of(state, unit) is not { } team) continue;
            var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, null, frame.Context.Action, null, Battlefield.TerrainUnder(state, unit), point));
            foreach (var (source, row) in live.AtPoint(Side.Attacker, point).Where(x => x.Effect.Kind is EffectKind.Move or EffectKind.Place).DistinctBy(x => x.Source.Entry.Name))
                reactions.Add(new Reaction(team, $"{source.Entry.Name}|{unit:N}", Label(source.Entry.Name, row), GfNames.RepositionSeq, new SequenceContext(unit, [], [], source.Entry.Name)));
        }
        return reactions;
    }

    static bool Shaken(GameState state, Guid unit) => state.Projection.Units[unit].Conditions.Contains(GfNames.Shaken);
    static string Label(string entry, Effect row) => row.Kind == EffectKind.Move ? $"{entry}: move up to {row.Amount}\""
        : row.AmountDice is null ? $"{entry}: place within {row.Amount}\"" : $"{entry}: place within {(row.Amount == 1 ? "" : row.Amount)}D3\"";
```

- [ ] **Step 5: The reposition sequence**

In `GfMoves`, make `MoveStep.Apply` reusable by moving its body to a public static `ApplyMove(StepContext ctx, Guid unit, LiveSet live, double allowance, IReadOnlyList<ModelRelocation> proposed)` that computes `terrain` itself (`Battlefield.Terrain(ctx.State)`), and have `MoveStep` call it. Then add:

```csharp
    public static Sequence Reposition() => new(GfNames.RepositionSeq, [new RepositionStep()]);

    /// <summary>
    /// A move or placement a rule offered at a point (army-rule hooks spec). The context names the unit and, as its Action, the rule — which is how
    /// usage_count counts this use. The row is re-read from the live set with every condition but at_point and usage_count judged, since this
    /// frame's own start is now a use. Phases as MoveStep's: 0 ask, then apply (Repeat); 1 dangerous terrain test (Push) for a move.
    /// </summary>
    public sealed class RepositionStep : IStep
    {
        public string Name => "reposition";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value; var entry = ctx.Frame.Context.Action!;
            var live = Lone(ctx, unit, ActionOf(ctx.Frame));
            if (Offered(live, entry) is not var (source, row)) return new Finish([]);
            live.Read(source);
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                {
                    var terrain = Battlefield.Terrain(ctx.State);
                    return new OpenAsks([ctx.Ask("move", $"{entry}: move up to {row.EffectiveAmount(null)}\"", ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), row.EffectiveAmount(null), Declared(terrain, TerrainDeclarations.Impassable).Select(v => v.Id).ToList(),
                            Declared(terrain, TerrainDeclarations.Difficult).Select(v => v.Id).ToList(), GfNames.DifficultCap), allowPass: true)]);
                }
                case 0:
                {
                    var (changes, _, dice) = ApplyMove(ctx, unit, live, row.EffectiveAmount(null), GfAnswers.Moves(ctx.State, ctx.History.LastClosed("move")!));
                    changes.AddRange(RollEffects.Consume(live));
                    return new Complete(changes, new Dictionary<string, int> { ["dangerous_dice"] = dice, ["phase"] = 1 }, Repeat: dice > 0);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], entry));
                default: return new Complete([]);
            }
        }

        /// <summary>The first move or place row of the rule whose conditions other than at_point and usage_count hold.</summary>
        internal static (LiveProperty Source, Effect Row)? Offered(LiveSet live, string entry) =>
            live.On(Side.Attacker).Where(p => p.Entry.Name == entry)
                .SelectMany(p => p.Effects.Where(e => e.Kind is EffectKind.Move or EffectKind.Place).Select(e => (p, e)))
                .Where(x => x.e.Conditions.Where(c => c.Kind is not ConditionKind.AtPoint and not ConditionKind.UsageCount).All(c => Conditions.Holds(live.Interaction, x.p.Holder, c, x.p, null)))
                .Select(x => ((LiveProperty, Effect)?)(x.p, x.e)).FirstOrDefault();

        /// <summary>The action of the activation this frame is inside: its nearest ancestor action sequence's context.</summary>
        static string? ActionOf(FrameView frame)
        {
            for (var f = frame.Parent; f is not null; f = f.Parent)
                if (f.Frame.Sequence is GfNames.HoldSeq or GfNames.AdvanceSeq or GfNames.RushSeq or GfNames.ChargeSeq or GfNames.IdleSeq) return f.Frame.Context.Action;
            return null;
        }
    }
```

Register `GfMoves.Reposition()` in `GfRules.Sequences.cs`'s catalog. `DangerousTestStep` reads the parent's `dangerous_dice`, which phase 0 recorded.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. Tests that read a shooting or melee frame's steps by name are unaffected; a test that asserted the stack empty right after a shooting or melee with no hook rule still sees it empty, since a window with no reaction completes at once.

- [ ] **Step 7: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Offer Hit & Run and Harassing moves after shooting and after melee"
```
