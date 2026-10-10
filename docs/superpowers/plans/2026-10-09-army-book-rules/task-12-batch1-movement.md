# Task 12: Batch 1: movement and range

**Files:**
- Modify: `server/game/Effects/StatReader.cs` (`ActionDistance` reads a removed row)
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (six entries)
- Modify: `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs` (`RangeBonus`)
- Create: `sim/Suite/SuiteCatalog.Army.Movement.cs` (`ArmyGames.Movement`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Modify: `tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs` (`Staging`)
- Test: `tests/Tabletop.GameServer.Tests/Batch1MovementTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `Game(... gunRange)`, `Allowance`, `Reposition`, `By`, `Of`, `Exact`, `UnitApart`, `PatternLog.For`
  (Task 8), `Each` (Task 9).
- Produces: entries Agile, Rapid Charge, Increased Shooting Range, Swift, Guerrilla, Guerrilla Boost; the check
  `RangeBonus(entry, occasion, printed, bonus, quote)`; `ArmyGames.Movement`; `ArmyGamesTests.Staging` holds `Slow`.

| Entry | Army Forge text | Rows (the shape it copies) | Book |
| --- | --- | --- | --- |
| Agile | "Moves +1" when using Advance, and +2” when using Rush/Charge." | modify Advance +1, Rush +2, Charge +2 (Fast) | Dark Elf Raiders |
| Rapid Charge | "This model moves +4" when using Charge actions." | modify Charge +4 (Rapid Rush) | Alien Hives |
| Increased Shooting Range | "This model gets +6" range when shooting." | weapon Range +6, shooting (Lustbound) | Alien Hives |
| Swift | "This model may ignore the Slow rule." | `suppress_property Slow` | Dwarf Guilds |
| Guerrilla | "Once per round, units where all models have this rule may move by up to 3" after shooting or being in melee." | Harassing's rows | Rebel Guerrillas |
| Guerrilla Boost | "If most models in this unit have Guerrilla, they may move by up to 6" from Guerrilla (instead of only 3")." | Harassing Boost's rows | Rebel Guerrillas (Guerrilla Boost Aura) |

Agile's text mixes a straight and a curly inch mark; the `ruleText` keeps both as Army Forge writes them. Guerrilla is
in no spec batch; it is authored here because Guerrilla Boost, a base a template wrapper names, reads it. *"May ignore
the Slow rule"* is read as always ignoring it: ignoring Slow only ever lengthens a move.

`StatReader.ActionDistance` reads the rows acting on a distance but not one a suppression removed, so a step that
moves a Swift unit would never list Swift. `RollEffects.Plan` reads removed rows with `LiveSet.ReadRemoved`; the
distance reader does the same.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/Batch1MovementTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1MovementTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static (LiveSet Live, AppliedEntries Applied, Tabletop.Content.Unit Unit, GameState State) Lone(string? point, params string[] rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [.. rules.Select(r => P(r))], Gun("Gun", "24\"", 1));
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var applied = new AppliedEntries();
        return (LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), null, null, null, null, [], point), applied), applied, a, s);
    }

    [Fact]
    public void Agile_and_rapid_charge_lengthen_their_moves()
    {
        var (agile, _, _, _) = Lone(null, "Agile");
        Assert.Equal((7.0, 14.0, 14.0), (StatReader.ActionDistance(agile, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(agile, Side.Attacker, GfNames.Rush), StatReader.ActionDistance(agile, Side.Attacker, GfNames.Charge)));
        var (charge, _, _, _) = Lone(null, "Rapid Charge");
        Assert.Equal((6.0, 16.0), (StatReader.ActionDistance(charge, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(charge, Side.Attacker, GfNames.Charge)));
    }

    [Fact]
    public void Swift_ignores_slow_and_is_listed_where_the_distance_is_read()
    {
        var (live, applied, _, _) = Lone(null, "Slow", "Swift");
        Assert.Equal(6.0, StatReader.ActionDistance(live, Side.Attacker, GfNames.Advance));
        Assert.Contains("Swift", applied.Names!);
    }

    [Fact]
    public void Increased_shooting_range_adds_six_inches_when_shooting()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Increased Shooting Range")], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var gun = s.Projection.Models.Values.Single(m => m.UnitId == a.Id).Slot.Weapons.Single().Id;
        var live = LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), AttackKind.Shooting, null, 20, []));
        Assert.Equal(30.0, StatReader.WeaponRange(live, Side.Attacker, gun));
    }

    [Fact]
    public void Guerrilla_moves_three_after_shooting_and_its_boost_six()
    {
        Assert.Equal(new int?[] { 3 }, Lone(GfNames.AfterShooting, "Guerrilla").Live.AtPoint(Side.Attacker, GfNames.AfterShooting).Select(x => x.Effect.Amount));
        Assert.Equal(new int?[] { 6 }, Lone(GfNames.AfterMelee, "Guerrilla", "Guerrilla Boost").Live.AtPoint(Side.Attacker, GfNames.AfterMelee).Select(x => x.Effect.Amount));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1MovementTests"`
Expected: FAIL in every test (text-only entries; `Swift_…` also fails for the missing read).

- [ ] **Step 3: Read a removed distance row**

In `server/game/Effects/StatReader.cs`, `ActionDistance`, add after the `var distance = …` line:

```csharp
        // A suppressed distance row (Swift's Slow) was read here: the suppressor acted on this distance (LiveSet.ReadRemoved, as RollEffects.Plan reads one).
        live.ReadRemoved(side, EffectKind.ModifyStat, e => e.TargetAction == action);
```

- [ ] **Step 4: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order:

```json
    {"name": "Agile", "kind": "model", "aggregation": "all", "ruleText": "Moves +1\" when using Advance, and +2” when using Rush/Charge.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 1}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 2}, {"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 2}]},
    {"name": "Guerrilla", "kind": "model", "aggregation": "all", "ruleText": "Once per round, units where all models have this rule may move by up to 3\" after shooting or being in melee.", "effects": [{"subject": "self", "ordinal": 10, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-shooting"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}, {"subject": "self", "ordinal": 20, "kind": "move", "amount": 3, "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["after-melee"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]},
    {"name": "Guerrilla Boost", "kind": "model", "aggregation": "all", "ruleText": "If most models in this unit have Guerrilla, they may move by up to 6\" from Guerrilla (instead of only 3\").", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Guerrilla", "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Guerrilla"]}]}, {"subject": "self", "ordinal": 10, "kind": "move", "amount": 6, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Guerrilla"]}, {"kind": "at_point", "comparison": "eq", "values": ["after-shooting"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}, {"subject": "self", "ordinal": 20, "kind": "move", "amount": 6, "conditions": [{"kind": "holds_property", "subject": "self", "comparison": "eq", "values": ["Guerrilla"]}, {"kind": "at_point", "comparison": "eq", "values": ["after-melee"]}, {"kind": "usage_count", "comparison": "lt", "number": 1, "values": ["round"]}]}]},
    {"name": "Increased Shooting Range", "kind": "model", "aggregation": "all", "ruleText": "This model gets +6\" range when shooting.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetStat": {"level": "weapon", "name": "Range"}, "amount": 6, "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Rapid Charge", "kind": "model", "aggregation": "all", "ruleText": "This model moves +4\" when using Charge actions.", "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4}]},
    {"name": "Swift", "kind": "model", "aggregation": "all", "ruleText": "This model may ignore the Slow rule.", "effects": [{"subject": "self", "ordinal": 1, "kind": "suppress_property", "targetEntry": "Slow"}]},
```

- [ ] **Step 5: Regenerate and run the tests**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1MovementTests|FullyQualifiedName~ArmyRulesContentTests|FullyQualifiedName~GfMovesTests"`
Expected: PASS. Swift Aura, Swift Buff and Guerrilla Boost Aura are now generated wrappers.

- [ ] **Step 6: The range check**

In `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs`, add inside `ArmyGames`:

```csharp
        /// <summary>
        /// A range bonus: the occasion's attacks made at a target beyond the weapon's printed range (as the attack began) are within the printed
        /// range plus the bonus. An attack beyond the printed range is the bonus's only evidence, so a game with none fails.
        /// </summary>
        static GameCheck RangeBonus(string entry, Occasion occasion, double printed, double bonus, string quote) =>
            Exact(entry, $"{quote}: {printed}\" + {bonus}\"", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Attacker is not null && a.Target is not null && PatternLog.For(run).Attacks.ContainsKey(a.Frame.FrameId)
                let apart = UnitApart(run, PatternLog.For(run).Attacks[a.Frame.FrameId].At, a.Attacker!.Value, a.Target!.Value)
                where apart > printed
                select apart <= printed + bonus ? null : $"shot a unit {apart:0.##}\" away");
```

- [ ] **Step 7: The games**

Create `sim/Suite/SuiteCatalog.Army.Movement.cs`:

```csharp
namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: move bonuses (p.7: "Advance - Moves 6", "Rush - Moves 12", "Charge - Moves 12"), a range bonus, Swift, and Guerrilla's move.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) Guerrillas => ("5wbcv465hacdwvkb", "Rebel Guerrillas");

        public static IReadOnlyList<TestGame> Movement =>
        [
            Game(DarkElves, "dark-elf-raiders-agile", Play.Moves, ["Agile"], Each(4, "Agile"), [],
                [Allowance("Agile", null, "\"Moves +1\" when using Advance, and +2” when using Rush/Charge\"", ("advance", 7), ("rush", 14), ("charge", 14))]),
            Game(Hives, "alien-hives-rapid-charge", Play.Charges, ["Rapid Charge"], Each(4, "Rapid Charge"), [],
                [Allowance("Rapid Charge", null, "\"This model moves +4\" when using Charge actions\"", ("charge", 16))]),
            // A 12" rifle: the armies stand at least 12" apart, so every shot that lands is one the +6" reached.
            Game(Hives, "alien-hives-increased-shooting-range", Play.Shoots, ["Increased Shooting Range"], Each(2, "Increased Shooting Range"), [],
                [RangeBonus("Increased Shooting Range", By("Increased Shooting Range", when: When.Shooting), 12, 6, "\"This model gets +6\" range when shooting.\"")], gunRange: 12),
            // p.14 Slow, "move -2" when using Advance, and -4" when using Rush/Charge", on every unit; Swift leaves p.7's distances.
            Game(Dwarves, "dwarf-guilds-swift", Play.Moves, ["Swift"], Each(4, "Slow", "Swift"), [],
                [Allowance("Swift", null, "\"This model may ignore the Slow rule.\"", ("advance", 6), ("rush", 12), ("charge", 12))]),
            Game(Guerrillas, "rebel-guerrillas-guerrilla", Play.Shoots, ["Guerrilla", "Guerrilla Boost"], [R(1, "Guerrilla"), R(2, "Guerrilla"), R(2, "Guerrilla Boost")], [],
                [Reposition("Guerrilla", 3, "\"may move by up to 3\" after shooting or being in melee\""), Reposition("Guerrilla Boost", 6, "\"they may move by up to 6\" from Guerrilla\"")]),
        ];
    }
}
```

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.Movement`.

In `tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs`, replace `Staging` and its summary:

```csharp
    /// <summary>Core rules a game gives a side only to stage the play (p.14 Immobile: "may only use Hold actions"; p.14 Slow, which Swift ignores); they are not the army group's.</summary>
    static readonly string[] Staging = ["Immobile", "Slow"];
```

- [ ] **Step 8: Run the games, the army tests and the full suite**

Run: `dotnet build sim && dotnet run --project sim -- suite --only dark-elf-raiders-agile,alien-hives-rapid-charge,alien-hives-increased-shooting-range,dwarf-guilds-swift,rebel-guerrillas-guerrilla`
Expected: five `pass` lines.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 9: Commit**

```bash
git add server/game/Effects/StatReader.cs server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs sim/Suite/SuiteCatalog.Army.Movement.cs \
  tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs tests/Tabletop.GameServer.Tests/Batch1MovementTests.cs
git commit -m "Play the army-book movement and range rules: Agile, Rapid Charge, Increased Shooting Range, Swift, Guerrilla"
```
