# Task 13: Batch 1: written-out Auras that grant a rule

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (twelve entries)
- Modify: `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs` (`Carrying`, `GrantedModifier`)
- Create: `sim/Suite/SuiteCatalog.Army.Auras.cs` (`ArmyGames.GrantAuras`)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (the `Army()` list)
- Test: `tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs` (new); the suite games

**Interfaces:**
- Consumes: `Game(... carriers)`, `Rerolls`, `PerDieAp`, `ExtraWounds`, `ApGrant`, `Made`, `When`, `PatternLog.For`,
  `NoCover`, `NoPenalty`, `EnemyRuins`, `Each`, `ChangeDaemons` (Tasks 8, 9), `Guerrillas` (Task 12).
- Produces: twelve Aura entries; the occasion `Carrying(entry, when)`, the check `GrantedModifier(entry, occasion,
  source, roll, amount, quote)`, `Carriers(rule, units)`; `ArmyGames.GrantAuras`; `Batch1AurasTests.Holds` (Task 14
  adds to the test class).

The spec's *"21 written-out Auras"* are the books' names ending " Aura" whose description is not the template *"This
model and its unit get X."* with X the name less " Aura". Twelve of them grant a rule in one attack kind; each is the
Aura entry (`model`, aggregation `any`, *"this model and its unit"*) with the row `Bane in Melee`'s shape: a subject-self
`grant_property` of X under an `attack_kind` condition. Each game puts the Aura on model 1 of each rule unit and reads
only the attacks made while a carrier stands (`Carrying`): the unit holds X through the carrier.

| Entry | Army Forge text | Grants | Book |
| --- | --- | --- | --- |
| Bane in Melee Aura | "This model and its unit get Bane in melee." | Bane, melee | Battle Brothers |
| Bane when Shooting Aura | "This model and its unit get Bane when shooting." | Bane, shooting | Battle Brothers |
| Ignores Cover Aura | "This model and its unit get Ignores Cover when shooting." | Ignores Cover, shooting | Eternal Dynasty |
| Ignores Cover when Shooting Aura | "This model and its unit get Ignores Cover when shooting." | Ignores Cover, shooting | Dwarf Guilds |
| Indirect when Shooting Aura | "This model and its unit get Indirect when shooting." | Indirect, shooting | Wormhole Daemons of Change |
| Rending in Melee Aura | "This model and its unit get Rending in melee." | Rending, melee | Rebel Guerrillas |
| Rending when Shooting Aura | "This model and its unit get Rending when shooting." | Rending, shooting | Machine Cults |
| Shred in Melee Aura | "This model and its unit get Shred in melee." | Shred, melee | Blessed Sisters |
| Shred when Shooting Aura | "This model and its unit get Shred when shooting." | Shred, shooting | Custodian Brothers |
| Thrust in Melee Aura | "This model and its unit get Thrust in melee." | Thrust, melee | Infected Colonies |
| Unstoppable in Melee Aura | "This model and its unit get Unstoppable in melee." | Unstoppable, melee | Custodian Brothers |
| Unstoppable when Shooting Aura | "This model and its unit get Unstoppable when shooting." | Unstoppable, shooting | Robot Legions |

- [ ] **Step 1: Write the failing content test**

Create `tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs`:

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class Batch1AurasTests
{
    static readonly PropertySet Core = TestProperties.Core;

    /// <summary>A three-model unit whose first model carries the Aura, attacking with every model or with the two that do not carry it.</summary>
    internal static LiveSet Holds(string aura, AttackKind kind, bool carrierAttacks = true, string? action = null)
    {
        var u = Unit("A", "4+", "4+", [], 3, [], Gun("Gun", "24\"", 1));
        var a = u with { Models = [u.Models[0] with { Properties = [P(aura)] }, .. u.Models.Skip(1)] };
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var models = carrierAttacks ? a.Models.Select(m => m.Id) : a.Models.Skip(1).Select(m => m.Id);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Of(s, a.Id, models, []), Party.Whole(s, b.Id), kind, action, 12, []));
    }

    [Theory]
    [InlineData("Bane in Melee Aura", "Bane", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Bane when Shooting Aura", "Bane", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Cover Aura", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Ignores Cover when Shooting Aura", "Ignores Cover", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Indirect when Shooting Aura", "Indirect", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Rending in Melee Aura", "Rending", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Rending when Shooting Aura", "Rending", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Shred in Melee Aura", "Shred", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Shred when Shooting Aura", "Shred", AttackKind.Shooting, AttackKind.Melee)]
    [InlineData("Thrust in Melee Aura", "Thrust", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Unstoppable in Melee Aura", "Unstoppable", AttackKind.Melee, AttackKind.Shooting)]
    [InlineData("Unstoppable when Shooting Aura", "Unstoppable", AttackKind.Shooting, AttackKind.Melee)]
    public void An_aura_gives_the_unit_its_rule_in_its_attack_kind_while_its_carrier_is_in_the_attack(string aura, string granted, AttackKind yes, AttackKind no)
    {
        Assert.True(Holds(aura, yes).Has(Side.Attacker, granted));
        Assert.False(Holds(aura, no).Has(Side.Attacker, granted));
        Assert.False(Holds(aura, yes, carrierAttacks: false).Has(Side.Attacker, granted));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1AurasTests"`
Expected: FAIL: every first `Assert.True` fails (the Auras are text-only).

- [ ] **Step 3: The entries**

In `server/game/Content/grimdark-future-army-rules.part.json`, add in name order (each `"aggregation": "any"`, the
grant at ordinal 5 under its attack kind):

```json
    {"name": "Bane in Melee Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Bane in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Bane", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Bane when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Bane when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Bane", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Ignores Cover Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Ignores Cover when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Ignores Cover", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Ignores Cover when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Ignores Cover when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Ignores Cover", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Indirect when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Indirect when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Indirect", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Rending in Melee Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Rending in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Rending", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Rending when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Rending when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Rending", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Shred in Melee Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Shred in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Shred", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Shred when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Shred when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Shred", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
    {"name": "Thrust in Melee Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Thrust in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Thrust", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Unstoppable in Melee Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Unstoppable in melee.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Unstoppable", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["melee"]}]}]},
    {"name": "Unstoppable when Shooting Aura", "kind": "model", "aggregation": "any", "ruleText": "This model and its unit get Unstoppable when shooting.", "effects": [{"subject": "self", "ordinal": 5, "kind": "grant_property", "targetEntry": "Unstoppable", "conditions": [{"kind": "attack_kind", "comparison": "eq", "values": ["shooting"]}]}]},
```

- [ ] **Step 4: Regenerate and run the content test**

Run: `./scripts/army-books generate && dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~Batch1AurasTests|FullyQualifiedName~ArmyRulesContentTests"`
Expected: PASS; the report's "Aura, effect written out" falls by twelve.

- [ ] **Step 5: The occasion and the check**

In `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs`, add inside `ArmyGames`:

```csharp
        /// <summary>The attacks of a unit while a model carrying the entry stands (an Aura: "this model and its unit"), as the attack began.</summary>
        static Occasion Carrying(string entry, When when = When.Attacking) => (run, a) =>
            Made(a, when) && a.Attacker is { } u && PatternLog.For(run).Attacks.TryGetValue(a.Frame.FrameId, out var s)
            && run.Final.Units[u].Definition.Models.Any(m => m.Properties.Any(p => p.Name == entry) && s.At.ContainsKey(m.Id));

        /// <summary>A granted rule's modifier: every roll of the kind in the occasion's attacks carries the granted rule's modifier at the stated
        /// size (its source is the granted rule's name); a fatigued unit's melee roll (p.9, unmodified 6s only) is left out.</summary>
        static GameCheck GrantedModifier(string entry, Occasion occasion, string source, string roll, int amount, string quote) =>
            Exact(entry, $"{quote}: {source} {roll} {amount:+0;-0}", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                from r in a.Hit!.Rolls(roll)
                where !(r.Threshold == 6 && r.Modifiers.Count == 0)
                select r.Modifiers.Any(m => m.Source == source && m.Amount == amount) ? null : $"a {roll} roll without {source} {amount:+0;-0}");

        /// <summary>Model 1 of each of the first units carries the rule.</summary>
        static IReadOnlyList<ScenarioModel> Carriers(string rule, int units) => [.. Enumerable.Range(1, units).Select(u => new ScenarioModel(u, 1, [R(u, rule)]))];
```

- [ ] **Step 6: The games**

Create `sim/Suite/SuiteCatalog.Army.Auras.cs`:

```csharp
namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: the written-out Auras ("This model and its unit get …"). Model 1 of each rule unit carries the Aura; the checks read the
    /// attacks made while a carrier stands.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BattleBrothers => ("78qp9l5alslt6yj8", "Battle Brothers");
        static TestGame Melee((string Uid, string Army) book, string name, string aura, GameCheck check, IReadOnlyList<ScenarioRule>? enemy = null) =>
            Game(book, name, Play.Charges, [aura], [], [], [check], carriers: Carriers(aura, 3), units: 3, enemy: enemy);
        static TestGame Shooting((string Uid, string Army) book, string name, string aura, GameCheck check, IReadOnlyList<ScenarioRule>? enemy = null,
            Func<Tabletop.Sim.Scenarios.ScenarioBuilder, Tabletop.Sim.Scenarios.ScenarioBuilder>? board = null, Play play = Play.Shoots) =>
            Game(book, name, play, [aura], [], [], [check], carriers: Carriers(aura, 2), units: 2, enemy: enemy, board: board);

        public static IReadOnlyList<TestGame> GrantAuras =>
        [
            Melee(BattleBrothers, "battle-brothers-bane-in-melee-aura", "Bane in Melee Aura",
                Rerolls("Bane in Melee Aura", Carrying("Bane in Melee Aura", When.Charging), [6], successOnly: false, "\"This model and its unit get Bane in melee.\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")),
            Shooting(BattleBrothers, "battle-brothers-bane-when-shooting-aura", "Bane when Shooting Aura",
                Rerolls("Bane when Shooting Aura", Carrying("Bane when Shooting Aura", When.Shooting), [6], successOnly: false, "\"This model and its unit get Bane when shooting.\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")),
            Shooting(Dynasty, "eternal-dynasty-ignores-cover-aura", "Ignores Cover Aura",
                NoCover("Ignores Cover Aura", Carrying("Ignores Cover Aura", When.Shooting), "\"This model and its unit get Ignores Cover when shooting.\""), board: EnemyRuins),
            Shooting(Dwarves, "dwarf-guilds-ignores-cover-when-shooting-aura", "Ignores Cover when Shooting Aura",
                NoCover("Ignores Cover when Shooting Aura", Carrying("Ignores Cover when Shooting Aura", When.Shooting), "\"This model and its unit get Ignores Cover when shooting.\""), board: EnemyRuins),
            // Moves: the first half of the units Advance and shoot, so p.14 Indirect's "-1 to hit rolls when shooting after moving" is read.
            Shooting(ChangeDaemons, "wormhole-daemons-of-change-indirect-when-shooting-aura", "Indirect when Shooting Aura",
                GrantedModifier("Indirect when Shooting Aura", (run, a) => Carrying("Indirect when Shooting Aura", When.Shooting)(run, a) && a.Hit?.Nearest("advance") is not null,
                    "Indirect", "hit", -1, "\"This model and its unit get Indirect when shooting.\": p.14 Indirect, \"Gets -1 to hit rolls when shooting after moving\""), play: Play.Moves),
            Melee(Guerrillas, "rebel-guerrillas-rending-in-melee-aura", "Rending in Melee Aura",
                PerDieAp("Rending in Melee Aura", Carrying("Rending in Melee Aura", When.Charging), 4, "\"This model and its unit get Rending in melee.\": p.14 Rending, \"those hits get AP(+4)\"")),
            Shooting(MachineCults, "machine-cults-rending-when-shooting-aura", "Rending when Shooting Aura",
                PerDieAp("Rending when Shooting Aura", Carrying("Rending when Shooting Aura", When.Shooting), 4, "\"This model and its unit get Rending when shooting.\": p.14 Rending, \"those hits get AP(+4)\"")),
            Melee(Sisters, "blessed-sisters-shred-in-melee-aura", "Shred in Melee Aura",
                ExtraWounds("Shred in Melee Aura", Carrying("Shred in Melee Aura", When.Charging), [1], "\"This model and its unit get Shred in melee.\": Shred, \"this weapon deals 1 extra wound\"")),
            Shooting(Custodians, "custodian-brothers-shred-when-shooting-aura", "Shred when Shooting Aura",
                ExtraWounds("Shred when Shooting Aura", Carrying("Shred when Shooting Aura", When.Shooting), [1], "\"This model and its unit get Shred when shooting.\": Shred, \"this weapon deals 1 extra wound\"")),
            Game(Infected, "infected-colonies-thrust-in-melee-aura", Play.Charges, ["Thrust in Melee Aura"], [], [],
                [GrantedModifier("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), "Thrust", "hit", 1, "\"This model and its unit get Thrust in melee.\": p.14 Thrust, \"When charging, gets +1 to hit rolls\""),
                 ApGrant("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), 1, "\"This model and its unit get Thrust in melee.\": p.14 Thrust, \"and AP(+1) in melee\"")],
                carriers: Carriers("Thrust in Melee Aura", 3), units: 3),
            Melee(Custodians, "custodian-brothers-unstoppable-in-melee-aura", "Unstoppable in Melee Aura",
                NoPenalty("Unstoppable in Melee Aura", Carrying("Unstoppable in Melee Aura", When.Charging), "\"This model and its unit get Unstoppable in melee.\": p.15 Unstoppable"),
                enemy: Each(2, "Melee Evasion")),
            // Stealth on the opponents (shot from over 9") is the negative modifier Unstoppable ignores; Tough(3) keeps them standing.
            Shooting(Robots, "robot-legions-unstoppable-when-shooting-aura", "Unstoppable when Shooting Aura",
                NoPenalty("Unstoppable when Shooting Aura", Carrying("Unstoppable when Shooting Aura", When.Shooting), "\"This model and its unit get Unstoppable when shooting.\": p.15 Unstoppable"),
                enemy: [.. Each(2, "Stealth"), R(1, "Tough", 3), R(2, "Tough", 3)]),
        ];
    }
}
```

Add `using Tabletop.Sim.Scenarios;` at the top of `SuiteCatalog.Army.Auras.cs` (for `ScenarioRule`) and of
`SuiteCatalog.Army.Batch1.Checks.cs` if it is not there (for `ScenarioModel`).

In `sim/Suite/SuiteCatalog.Army.cs`, extend the list with `.. ArmyGames.GrantAuras`.

- [ ] **Step 7: Run the games, the army tests and the full suite**

Run: `dotnet build sim && dotnet run --project sim -- suite --only battle-brothers-bane-in-melee-aura,battle-brothers-bane-when-shooting-aura,eternal-dynasty-ignores-cover-aura,dwarf-guilds-ignores-cover-when-shooting-aura,wormhole-daemons-of-change-indirect-when-shooting-aura,rebel-guerrillas-rending-in-melee-aura,machine-cults-rending-when-shooting-aura,blessed-sisters-shred-in-melee-aura,custodian-brothers-shred-when-shooting-aura,infected-colonies-thrust-in-melee-aura,custodian-brothers-unstoppable-in-melee-aura,robot-legions-unstoppable-when-shooting-aura`
Expected: every listed game `pass`.

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~ArmyGamesTests" && dotnet run --project sim -- suite`
Expected: PASS; the suite's last line starts `PASSED:`.

- [ ] **Step 8: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs sim/Suite/SuiteCatalog.Army.Auras.cs \
  tests/Tabletop.GameServer.Tests/Batch1AurasTests.cs
git commit -m "Play the written-out Auras that grant a rule in melee or when shooting"
```
