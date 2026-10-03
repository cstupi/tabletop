# Task 4: Shooting after Rush: *Quick Shot*

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`Rush()` ends with `ShootOrNotStep`)
- Modify: `server/game/Rules/GrimdarkFuture/GfShooting.cs` (`ValidTargets` after a Rush)
- Modify: `server/game/Rules/GrimdarkFuture/GfNames.cs` (`QuickShot`)
- Test: `tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs`

**Interfaces:**
- Consumes: the part's name-only *Quick Shot* entry (library Task 11); `LiveSet.Has`; the library's grant-as-row reading (library Task 5).
- Produces: `GfNames.QuickShot = "Quick Shot"`; `rush` = `[activation-terrain, activation-start, move, shoot-or-not]`.

Spec → *Shooting after Rush*. p.7: *"Rush - Moves 12" but can't shoot"*; *Quick Shot*: *"This model may shoot after using Rush actions."* A name check: a target is valid after a Rush only when *Quick Shot* is live on the attacking side of that target's interaction, so an innate rule opens every target and *Combat Ecstasy*'s *against* grant opens the marked unit alone.

- [ ] **Step 1: Write the failing tests**

Add to `ArmyRuleHookTests`:

```csharp
    static Sim Rush(Tabletop.Content.Unit a, IReadOnlyList<Tabletop.Content.Unit> enemies, PropertySet props)
    {
        var sim = new Sim(State([a], enemies, x1: -6, x2: 6), GrimdarkFutureRules.Instance, props, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Passed());                                                         // no move
        return sim;
    }

    [Fact]
    public void Without_quick_shot_a_rush_does_not_shoot_and_with_it_every_target_is_offered()
    {
        var b = Unit("B", "4+", "4+", [], 1, []);
        Assert.Null(Rush(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), [b], Core).AskForOrNull(Samples.Team1));
        var quick = Rush(Unit("Q", "4+", "4+", [], 1, [P("Quick Shot")], Gun("Rifle", "24\"", 1)), [b], Core);
        Assert.Equal("shoot_or_not", quick.AskFor(Samples.Team1).Question);
    }

    [Fact]
    public void An_against_grant_of_quick_shot_opens_only_the_marked_unit()
    {
        var ecstasy = new EntryDocument("Test Ecstasy", CatalogKind.Spell, false, null, 1, null, "Friendly units get Quick Shot against.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18),
            [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.GrantProperty, null, null, "Quick Shot", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);
        var props = TestProperties.WithSpells(ecstasy);
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var marked = Unit("M", "4+", "4+", [], 1, []); var other = Unit("O", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [marked, other], x1: -6, x2: 6), GrimdarkFutureRules.Instance, props, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
        sim.Append([new GrantAdded(marked.Id, new Grant(Guid.CreateVersion7(), props.Entry("Test Ecstasy")!.Effects[0].Id, "Test Ecstasy", a.Id, 1))]);
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        var assignment = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([marked.Id], assignment.Targets);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~ArmyRuleHookTests`
Expected: FAIL: the quick unit is never asked to shoot.

- [ ] **Step 3: Shoot after Rush, at the targets Quick Shot opens**

In `GfNames`: `public const string QuickShot = "Quick Shot";   // army-rule hooks: a name check on the rush sequence's shooting`.

In `GfMoves`: `public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new PointWindowStep(GfNames.ActivationStart, "activation-start"), new MoveStep(GfNames.Rush, false), new ShootOrNotStep()]);`, and add to `ShootOrNotStep`'s summary that a Rush reaches it and offers only what `ValidTargets` admits.

In `GfShooting.ValidTargets`, inside the per-target lambda after the live set is settled:

```csharp
            // p.7: "Rush - Moves 12" but can't shoot", unless Quick Shot is live for this target ("This model may shoot after using Rush actions"):
            // innate, or granted against this target (army-rule hooks → Shooting after Rush).
            if (ctx.Frame.Context.Action == GfNames.Rush && !live.Has(Side.Attacker, GfNames.QuickShot)) return false;
```

`ShootOrNotStep` asks only when some group has a valid target, so a rushing unit without the rule is asked nothing, as the first test requires.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. A Rush test that asserted the stack empty after the move still passes for a unit without *Quick Shot*: `shoot-or-not` completes at once.

- [ ] **Step 5: Commit**

```bash
git add server/game tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
git commit -m "Let a unit with Quick Shot shoot after Rush, at the targets the rule opens"
```
