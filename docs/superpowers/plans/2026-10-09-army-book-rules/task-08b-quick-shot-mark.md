# Task 8b: Quick Shot Mark

**Files:**
- Modify: `sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs` (`RushShotMarked`)
- Modify: `sim/Suite/SuiteCatalog.Army.Patterns.cs` (a fifth pattern game)
- Test: `tests/Tabletop.GameServer.Tests/QuickShotMarkTests.cs` (new)

**Interfaces:**
- Consumes: the generated `Quick Shot Mark` (Task 8), `GfPick.Key` (Task 4), `PatternLog.For(run).Shootings` and
  `.Attacks`, `PickWithin`, `Game(... carriers)` (Task 8), `ShootingSequence` (`SuiteCatalog.Army.Checks.cs`).
- Produces: the check `RushShotMarked(entry, quote)`; the game `pattern-mark-quick-shot` (Pattern `Mark`).

Generated: *"Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which friendly units
gets Quick Shot against once (next time the effect would apply)."* (Wormhole Daemons of Lust); Quick Shot: *"This model
may shoot after using Rush actions."*

**Quick Shot Mark** gets its own Mark game and a server test, so it cannot pass on pattern coverage alone. Quick Shot
(*"This model may shoot after using Rush actions"*) is read where a Rush unit's targets are judged,
`GfShooting.ValidTargets`, which settles one interaction per target with that target as the defender, and spent by
`SpendQuickShot` at `declare-targets` against the target shot. The Mark's subject-opponent grant sits on the marked unit
and reaches its attacker exactly there, as the marked-unit spells' grants do. `MoveStep` opens the `before-attacking`
window after a Rush move as after an Advance (`GfMoves.cs`, the window phase), so a Rush unit is offered the Mark before
it is asked to shoot. No engine change is needed; the test pins it: a marked enemy shot after Rush, Quick Shot spent by
that shooting, and no shooting offered without the Mark.

- [ ] **Step 0: Confirm the reading in the code**

Read `server/game/Rules/GrimdarkFuture/GfShooting.cs`: `ValidTargets` settles `GfAttack.AttackInteraction(ctx, unit, target, …)`
per target and returns false after Rush unless `live.Has(Side.Attacker, GfNames.QuickShot)`; `SpendQuickShot` reads the
Quick Shot instances of the same per-target interaction and returns `RollEffects.Consume(live)`. With the target as the
defender, `LiveProperties.Gather` adds the target's subject-opponent grant row for an attacker on the carrier's team.

- [ ] **Step 1: Write the server test**

Create `tests/Tabletop.GameServer.Tests/QuickShotMarkTests.cs`:

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Quick Shot Mark (generated): "which friendly units gets Quick Shot against once", Quick Shot being "This model may shoot after using Rush actions".</summary>
public class QuickShotMarkTests
{
    static Sim Rushing(Tabletop.Content.Unit a, Tabletop.Content.Unit b)
    {
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(Enumerable.Repeat(4, 60).ToArray()));
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Passed());                                          // stays put: the enemy is 12" away
        return sim;
    }

    [Fact]
    public void A_marked_enemy_may_be_shot_after_rush_and_the_shooting_spends_the_mark()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Quick Shot Mark")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Rushing(a, b);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Quick Shot Mark", a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Single(sim.Unit(b.Id).Grants);
        Assert.Equal("shoot_or_not", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Empty(sim.Unit(b.Id).Grants);                                              // once: spent by the shooting it allowed
    }

    [Fact]
    public void Without_the_mark_a_rushing_unit_is_not_asked_to_shoot()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Quick Shot Mark")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Rushing(a, b);
        sim.Answer(Samples.Team1, new Passed());                                          // the react Ask: the Mark is not used
        Assert.NotEqual("shoot_or_not", sim.AskForOrNull(Samples.Team1)?.Question);
    }
}
```

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~QuickShotMarkTests"`
Expected: PASS once Task 8 has generated the entry, if the engine reads the Mark as described above. This test pins existing behaviour the generated entry
relies on, so it is not written to fail first; if it fails, the reading above is wrong and the task stops: the Quick Shot
reading in `GfShooting.ValidTargets`/`SpendQuickShot` must then read the target's grant before this task goes on.

- [ ] **Step 2: The check**

In `sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs`, add inside `ArmyGames` after `PickWithin`:

```csharp
        /// <summary>
        /// Quick Shot through a Mark: every attack made in a shooting after Rush is at a unit that held the entry's grant as the shooting began
        /// (p.7: "Rush - Moves 12" but can't shoot", so the Mark is what let it shoot), and the grant is gone as the attack begins: that shooting
        /// spent it, once. A game with no shooting after Rush fails.
        /// </summary>
        static GameCheck RushShotMarked(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in log.Attacks
                where a.Shooting && a.Target is not null && a.Hit?.Nearest("rush") is not null && a.Hit!.Nearest(ShootingSequence) is { } shooting
                let p = PatternLog.For(run)
                let before = p.Shootings.GetValueOrDefault(shooting.FrameId)?.GetValueOrDefault(a.Target!.Value)?.Any(g => g.EntryName == entry) == true
                let after = p.Attacks.TryGetValue(a.Frame.FrameId, out var s) && s.TargetGrants.Any(g => g.EntryName == entry)
                select !before ? "shot after Rush at a unit the Mark was not on" : after ? "the Mark outlived the shooting it allowed" : null);
```

- [ ] **Step 3: The game**

In `sim/Suite/SuiteCatalog.Army.Patterns.cs`, add after the other `const string` quotes:

```csharp
        const string QuickShotQuote = "\"which friendly units gets Quick Shot against once\": Quick Shot, \"This model may shoot after using Rush actions\"";
        static (string Uid, string Army) LustDaemons => ("9qvy1oufoangt2gl", "Wormhole Daemons of Lust");
```

and append to `Patterns`, after the Debuff game:

```csharp
            // Moves: unit 1 Advances, unit 2 (the carrier's) Rushes 12" toward the one enemy unit, which puts it within 18", is offered the Mark
            // before attacking, and may then shoot the marked unit after its Rush; the script answers every shoot_or_not "shoot".
            Game(LustDaemons, "pattern-mark-quick-shot", Play.Moves, ["Quick Shot Mark"], [], [],
                [PickWithin("Quick Shot Mark", 18, friendly: false, QuickShotQuote), RushShotMarked("Quick Shot Mark", QuickShotQuote)],
                carriers: [new ScenarioModel(2, 1, [R(2, "Quick Shot Mark")])], units: 2, enemyUnits: 1) with { Pattern = "Mark" },
```

- [ ] **Step 4: Run the game and the full suite on both drivers**

Run: `dotnet build sim && dotnet run --project sim -- suite --only pattern-mark-quick-shot`
Expected: `pass`. If it fails `no occasion in the log`, seed 1 brought no shooting after Rush: give the game `seed:` the
first value of 2..20 for which this command passes, and note the seed in its comment.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite && dotnet run --project sim -- suite --driver socket`
Expected: both runs' last line starts `PASSED:`; `Quick Shot Mark` shows `covered by pattern (pattern-mark-rending, pattern-mark-quick-shot)`.

- [ ] **Step 5: Commit**

```bash
git add sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs sim/Suite/SuiteCatalog.Army.Patterns.cs tests/Tabletop.GameServer.Tests/QuickShotMarkTests.cs
git commit -m "Pin Quick Shot Mark: a marked enemy may be shot after Rush, once"
```
