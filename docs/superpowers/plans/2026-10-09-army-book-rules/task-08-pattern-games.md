# Task 8: Generated part and the pattern games

**Files:**
- Modify: `server/game/Content/grimdark-future-army-rules.part.json` (Defense Debuff)
- Modify: `sim/Suite/SuiteCatalog.Army.cs` (`Game` options, `Script` extension, `Gun` range, the `Army()` list)
- Create: `sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs`, `sim/Suite/SuiteCatalog.Army.Patterns.cs`
- Create (generated): `server/game/Content/grimdark-future-army-book-rules.part.json`
- Modify: `tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs`, `tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs`

**Interfaces:**
- Consumes: the pick mechanism (Task 4), `Lookup.UnitHas` (Task 5), `generate` (Task 6b), `TestGame.Pattern` and
  pattern coverage (Task 7).
- Produces: `ArmyGames.Game(..., IReadOnlyList<ScenarioModel>? carriers = null, int gunRange = 60, double? length = null,
  Action<ScriptBuilder>? more = null)`; `ArmyGames.Gun(int attacks, IReadOnlyList<ScenarioRule> rules, int range = 60)`;
  the log readers `PatternLog.For`, `Apart`, `UnitApart`, the occasion `Within(occasion, inches, within = true)`; `PatternLog.Shootings` (each shooting frame's grants as it began, read by Task 8b); the checks `AuraModifier`, `Once`,
  `PickWithin`; `ArmyGames.Patterns`; the
  committed generated part.

Spec *Coverage*: "Four suite games, one per pattern, each with an exact check quoting the template: **Aura**: a unit whose
one model carries Stealth Aura is shot from over 9"; the attacker's hit rolls carry Stealth's -1. **Buff**: before
attacking, the Buff is offered and used on a friendly unit within 12"; that unit gets X, and X is spent at its next
application. **Mark**: a marked enemy unit; the first friendly attack against it gets X; the second does not. **Debuff**:
a debuffed enemy unit suffers the effect once."

The Debuff pattern has no template, so its game plays a hand-authored Debuff: Defense Debuff (Ratmen Clans), whose
Army Forge text is *"Once per activation, before attacking, pick one enemy unit within 18" in line of sight, which gets
-1 to defense rolls once (next time the effect would apply)."* The other nine Debuffs are batch 2's. The Mark and Debuff
games play two rule units, unit 1 activating first (the script), so the second unit's attack on the same target comes
after the grant was spent. Their table is 36" by 26", so the 12"-deep zones leave 2" between the armies and a carrier
has an enemy within 18".

- [ ] **Step 1: Defense Debuff**

In `server/game/Content/grimdark-future-army-rules.part.json`, add after the `"Crack"` entry (entries are in name order):

```json
    {"name": "Defense Debuff", "kind": "model", "aggregation": "none", "ruleText": "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which gets -1 to defense rolls once (next time the effect would apply).", "targeting": {"side": "enemy", "grain": "unit", "count": 1, "range": 18}, "effects": [{"subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": -1, "duration": "once", "conditions": [{"kind": "at_point", "comparison": "eq", "values": ["before-attacking"]}]}]},
```

- [ ] **Step 2: Game options**

In `sim/Suite/SuiteCatalog.Army.cs`:

Replace `private static partial IEnumerable<TestGame> Army() => ArmyGames.All;` with:

```csharp
    private static partial IEnumerable<TestGame> Army() => [.. ArmyGames.All, .. ArmyGames.Patterns];
```

Replace the `Game` signature's last line and its builder lines:

```csharp
            int enemyAttacks = 2, IReadOnlyList<ScenarioRule>? enemyWeapon = null, int? rounds = null, Func<ScenarioBuilder, ScenarioBuilder>? board = null, int? units = null, ulong seed = 1,
            IReadOnlyList<ScenarioModel>? carriers = null, int gunRange = 60, double? length = null, Action<ScriptBuilder>? more = null)
```

```csharp
            var builder = new ScenarioBuilder(name, seed).Table(36, length ?? (close ? 26 : 36)).RoundCap(rounds ?? 4)
                .Seeking(Closing).Scripted(Script(play, book.Army, count, enemyUnits, more))
                .Army(book.Army, count, models, modelRules: Still(rules, count, holds), armyBook: book.Uid,
                    weapons: guns ? [Gun(volley ? 4 : 1, weapon ?? [], gunRange), Blade(1)] : [Blade(1, [.. weapon ?? []])], models: carriers)
```

Replace the `Script` signature and its last line:

```csharp
        static AnswerScript Script(Play play, string army, int units, int enemyUnits, Action<ScriptBuilder>? more = null)
```

```csharp
            more?.Invoke(script);
            return script.Build();
```

Replace `Gun`:

```csharp
        static ScenarioWeapon Gun(int attacks, IReadOnlyList<ScenarioRule> rules, int range = 60) =>
            new("Rifle", new Dictionary<string, string> { ["Range"] = $"{range}\"", ["Attacks"] = $"{attacks}" }, rules);
```

Extend the `Game` summary with: *"Carriers: models with their own rules (an Aura or a pick entry on one model). GunRange:
the rule side's rifle range. Length: the table's length. More: further script answers."*

- [ ] **Step 3: The pattern log and checks**

Create `sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs`:

```csharp
using System.Runtime.CompilerServices;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The pattern games' log reading (army-book rules spec → Coverage): what each unit held and where each model stood when an attack began,
    /// and each rule_target Ask with its carrying model. Read from the effective log in order; nothing here computes a rule.
    /// </summary>
    private static partial class ArmyGames
    {
        /// <summary>What the target held and where every placed model stood as an attack-sequence began.</summary>
        sealed record AttackStart(IReadOnlyList<Grant> TargetGrants, IReadOnlyDictionary<Guid, Placement> At);
        sealed record PickAsk(Ask Ask, string Entry, Guid Carrier, IReadOnlyDictionary<Guid, Placement> At);

        /// <summary>Shootings: each shooting frame's grants per unit as it began.</summary>
        sealed record PatternLog(IReadOnlyDictionary<Guid, AttackStart> Attacks, IReadOnlyList<PickAsk> Picks,
            IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, IReadOnlyList<Grant>>> Shootings)
        {
            static readonly ConditionalWeakTable<IReadOnlyList<GameEvent>, PatternLog> Read = new();

            /// <summary>The run's pattern log, read once per run.</summary>
            public static PatternLog For(SuiteGameRun run) => Read.GetValue(run.EffectiveLog, Of);

            static PatternLog Of(IReadOnlyList<GameEvent> log)
            {
                var grants = new Dictionary<Guid, List<Grant>>();
                var at = new Dictionary<Guid, Placement>();
                var frames = new Dictionary<Guid, SequenceStarted>();
                var attacks = new Dictionary<Guid, AttackStart>();
                var picks = new List<PickAsk>();
                var shootings = new Dictionary<Guid, IReadOnlyDictionary<Guid, IReadOnlyList<Grant>>>();
                void Apply(EventPayload p)
                {
                    switch (p)
                    {
                        case GrantAdded g: (grants.TryGetValue(g.UnitId, out var held) ? held : grants[g.UnitId] = []).Add(g.Grant); break;
                        case GrantRemoved r: grants.GetValueOrDefault(r.UnitId)?.RemoveAll(x => x.Id == r.GrantId); break;
                        case ModelsMoved m:
                            foreach (var mv in m.Moves) if (mv.To is OnTable t) at[mv.ModelId] = t.Placement; else at.Remove(mv.ModelId);
                            break;
                        case StepCompleted s: foreach (var c in s.Changes) Apply(c); break;
                        case SequenceStarted s:
                            frames[s.FrameId] = s;
                            if (s.Sequence == ShootingSequence)
                                shootings[s.FrameId] = grants.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Grant>)[.. kv.Value]);
                            if (s.Sequence == AttackSequence && s.Context.TargetUnitIds is [var target, ..])
                                attacks[s.FrameId] = new AttackStart([.. grants.GetValueOrDefault(target) ?? []], new Dictionary<Guid, Placement>(at));
                            break;
                        case AskOpened { Ask: { Question: "rule_target", FrameId: { } f } ask } when frames.TryGetValue(f, out var frame)
                            && frame.Context.Action is { } key && key.LastIndexOf('|') is var cut and > 0 && Guid.TryParseExact(key[(cut + 1)..], "N", out var carrier):
                            picks.Add(new PickAsk(ask, key[..cut], carrier, new Dictionary<Guid, Placement>(at)));
                            break;
                    }
                }
                foreach (var e in EventApplier.EffectiveLog(log)) Apply(e.Payload);
                return new PatternLog(attacks, picks, shootings);
            }
        }

        /// <summary>p.5 base to base: the nearest placed model of the unit to the model, at the placements given.</summary>
        static double Apart(SuiteGameRun run, IReadOnlyDictionary<Guid, Placement> at, Guid model, Guid unit)
        {
            PlacedModel Body(Guid m) => new(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), at[m]);
            return at.Keys.Where(m => run.Final.Models[m].UnitId == unit).Select(m => Distance.EdgeToEdge(Body(model), Body(m))).DefaultIfEmpty(double.PositiveInfinity).Min();
        }

        /// <summary>The engine's range between two units (Battlefield.UnitDistance): the nearest pair of their placed models.</summary>
        static double UnitApart(SuiteGameRun run, IReadOnlyDictionary<Guid, Placement> at, Guid a, Guid b) =>
            at.Keys.Where(m => run.Final.Models[m].UnitId == a).Select(m => Apart(run, at, m, b)).DefaultIfEmpty(double.PositiveInfinity).Min();

        /// <summary>The occasion's attacks made within (or, with within false, beyond) the distance of their target as they began.</summary>
        static Occasion Within(Occasion occasion, double inches, bool within = true) => (run, a) =>
            occasion(run, a) && a.Attacker is { } u && a.Target is { } t && PatternLog.For(run).Attacks.TryGetValue(a.Frame.FrameId, out var s)
            && UnitApart(run, s.At, u, t) <= inches == within;

        /// <summary>
        /// An Aura: every roll of the kind made in an attack on a unit one of whose models carries the wrapper carries the base's modifier at the
        /// stated size while a carrier stands, and none once no carrier stands ("This model and its unit get X").
        /// </summary>
        static GameCheck AuraModifier(string wrapper, string source, string roll, int amount, string quote) =>
            Exact(wrapper, $"{quote}: {source} {roll} {amount:+0;-0}", (run, log) =>
            {
                var starts = PatternLog.For(run).Attacks;
                return from a in log.Attacks
                       where a.Hit is not null && a.Target is { } t && Holds(run, t, wrapper) && starts.ContainsKey(a.Frame.FrameId)
                       let carriers = run.Final.Units[a.Target!.Value].Definition.Models.Where(m => m.Properties.Any(p => p.Name == wrapper)).Select(m => m.Id)
                       let standing = carriers.Any(starts[a.Frame.FrameId].At.ContainsKey)
                       let rolls = a.Hit!.Rolls(roll).ToList()
                       let carrying = rolls.Count(r => r.Modifiers.Any(m => m.Source == source && m.Amount == amount))
                       select standing ? rolls.Count > 0 && carrying == rolls.Count ? null : $"{carrying} of {rolls.Count} {roll} rolls carry {source} with a carrier standing"
                           : carrying == 0 ? null : $"{carrying} {roll} rolls carry {source} with no carrier standing";
            });

        /// <summary>
        /// "Once (next time the effect would apply)": each attack where the effect could show (Shows non-null) shows it exactly when its target
        /// held the entry's grant as the attack began; the log holds an attack under the grant and a later one on that unit without it.
        /// </summary>
        static GameCheck Once(string entry, string quote, Func<LoggedAttack, bool?> shows) =>
            Exact(entry, quote, (run, log) =>
            {
                var starts = PatternLog.For(run).Attacks;
                var verdicts = new List<string?>();
                var granted = new HashSet<Guid>();
                var spent = false;
                foreach (var a in log.Attacks)
                {
                    if (a.Target is not { } t || !starts.TryGetValue(a.Frame.FrameId, out var s) || shows(a) is not { } shown) continue;
                    var held = s.TargetGrants.Any(g => g.EntryName == entry);
                    if (held) granted.Add(t); else if (granted.Contains(t)) spent = true;
                    verdicts.Add(held == shown ? null : held ? "under the grant without the effect" : "the effect without the grant");
                }
                if (granted.Count == 0) verdicts.Add("no attack under the grant");
                else if (!spent) verdicts.Add("no attack on the same unit after the grant");
                return verdicts;
            });

        /// <summary>The pick's reach: every unit a rule_target Ask of the entry offered is the carrier's side's (friendly) or the other's (enemy)
        /// and has a model within the distance of the carrying model as the Ask opened.</summary>
        static GameCheck PickWithin(string entry, double inches, bool friendly, string quote) =>
            Exact(entry, $"{quote}: {(friendly ? "friendly" : "enemy")} units within {inches}\"", (run, _) =>
                from p in PatternLog.For(run).Picks
                where p.Entry == entry && p.Ask.Legal is PieceSelection
                from unit in ((PieceSelection)p.Ask.Legal).Eligible
                let ours = run.Final.Units[unit].ParticipantId == run.Final.Units[run.Final.Models[p.Carrier].UnitId].ParticipantId
                let apart = Apart(run, p.At, p.Carrier, unit)
                select ours != friendly ? $"offered {(ours ? "a friendly" : "an enemy")} unit" : apart > inches ? $"offered a unit {apart:0.##}\" away" : null);
    }
}
```

- [ ] **Step 4: The four games**

Create `sim/Suite/SuiteCatalog.Army.Patterns.cs`:

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>One game per Army Forge pattern (army-book rules spec → Coverage); each covers its pattern's generated wrappers by its Pattern.</summary>
    private static partial class ArmyGames
    {
        const string BuffQuote = "\"pick one friendly unit within 12\", which gets Stealth once (next time the effect would apply)\"";
        const string MarkQuote = "\"pick one enemy unit within 18\" in line of sight, which friendly units gets Rending against once\"";
        const string DebuffQuote = "\"pick one enemy unit within 18\" in line of sight, which gets -1 to defense rolls once\"";

        public static IReadOnlyList<TestGame> Patterns =>
        [
            // Shot: the opponents shoot from their zone, at least 12" away, so Stealth's "shot from over 9\"" holds on every shot.
            Game(Custodians, "pattern-aura-stealth", Play.Shot, ["Stealth Aura"], [], [],
                [AuraModifier("Stealth Aura", "Stealth", "hit", -1, "\"This model and its unit get Stealth.\"")],
                carriers: [Carrier("Stealth Aura")]) with { Pattern = "Aura" },
            // The one rule unit is the only friendly unit within 12": it buffs itself, then the first opponent's shooting spends Stealth.
            Game(Hives, "pattern-buff-stealth", Play.Shot, ["Stealth Buff"], [], [],
                [PickWithin("Stealth Buff", 12, friendly: true, BuffQuote),
                 Once("Stealth Buff", $"{BuffQuote}: Stealth -1 to hit", a => a.Hit?.Rolls("hit").ToList() is { Count: > 0 } rolls
                     ? rolls.Any(r => r.Modifiers.Any(m => m.Source == "Stealth" && m.Amount == -1)) : null)],
                carriers: [Carrier("Stealth Buff")]) with { Pattern = "Buff" },
            // Unit 1 marks the one enemy unit and shoots it with four fixed 6s; unit 2's shooting after it is unmarked.
            Game(Orcs, "pattern-mark-rending", Play.Shoots, ["Rending Mark"], [], [Fix("hit", 6, 6, 6, 6)],
                [PickWithin("Rending Mark", 18, friendly: false, MarkQuote),
                 Once("Rending Mark", $"{MarkQuote}: p.13 Rending, AP(+4) on unmodified 6s to hit", a => a.Hit is null || Count(a.HitDice, [6]) == 0 ? null : a.Scored("granted_AP_4") > 0)],
                carriers: [Carrier("Rending Mark")], units: 2, enemyUnits: 1, length: 26, more: UnitOneFirst(Orcs.Army)) with { Pattern = "Mark" },
            Game(Ratmen, "pattern-debuff-defense", Play.Shoots, ["Defense Debuff"], [], [],
                [PickWithin("Defense Debuff", 18, friendly: false, DebuffQuote),
                 Once("Defense Debuff", $"{DebuffQuote}: defense -1", a => a.Blocks.SelectMany(b => b.Rolls("defense")).ToList() is { Count: > 0 } rolls
                     ? rolls.Any(r => r.Modifiers.Any(m => m.Source == "Defense Debuff" && m.Amount == -1)) : null)],
                carriers: [Carrier("Defense Debuff")], units: 2, enemyUnits: 1, length: 26, more: UnitOneFirst(Ratmen.Army)) with { Pattern = "Debuff" },
        ];

        /// <summary>The first model of unit 1 carries the rule.</summary>
        static ScenarioModel Carrier(string rule) => new(1, 1, [R(1, rule)]);

        /// <summary>Unit 1 of the army is activated whenever it may be (its only action, holding, is "Hold").</summary>
        static Action<ScriptBuilder> UnitOneFirst(string army) => s => s.Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, $"{army} unit 1", "Hold"));
    }
}
```

- [ ] **Step 5: The army tests read carriers and generated wrappers**

In `tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs`, replace the `Rules` extension:

```csharp
    public static IEnumerable<string> Rules(this ScenarioArmy army) =>
        army.ModelRules.Select(r => r.Name).Concat(army.Models.SelectMany(m => m.Rules ?? []).Select(r => r.Name))
            .Concat(army.Weapons.SelectMany(w => w.Rules).Select(r => r.Name)).Distinct();
```

and in `The_army_games_cover_every_army_book_rule_once_each_under_a_shipped_book`, replace the second and third lines:

```csharp
        var covered = Army.SelectMany(g => g.Covers).ToList();
        var generated = PropertiesSeeder.EmbeddedPart(PropertiesSeeder.GeneratedPart)!.Entries.Where(e => e.Effects.Count > 0).Select(e => e.Name).ToHashSet();
        Assert.Equal(part.Entries.Select(e => e.Name).Order(), covered.Where(c => !generated.Contains(c)).Order());
        Assert.All(covered.Where(generated.Contains), c => Assert.Contains(Army, g => g.Pattern is not null && g.Covers.Contains(c)));   // a wrapper only by its pattern's game
```

In `tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs`, rename `The_part_holds_seventy_three_rules_and_composes_valid`
to `The_part_composes_valid_and_every_entry_is_in_the_set` and delete its line `Assert.Equal(73, part.Entries.Count);`
(every batch task adds entries; the coverage test above pins them to games).

- [ ] **Step 6: Generate the part**

Run: `./scripts/army-books generate`
Expected: exit 0 and a report of this shape (the counts are the dump's; record the printed ones in the commit message):

```
wrote 48 books, … spells
wrote …/server/game/Content/grimdark-future-army-book-rules.part.json: 60 generated, 173 text-only
  Aura, template text, base in catalog: 45
  Aura, template text, base missing: 23
  Aura, effect written out: 21
  Buff, template text, base in catalog: 6
  Buff, template text, base missing: 4
  Buff, effect written out: 8
  Mark, template text, base in catalog: 9
  Mark, template text, base missing: 0
  Mark, effect written out: 6
  Debuff, effect written out: 9
  Standalone, own text: 102
  no description: Banner, Musician
```

Run: `git diff --stat server/game/Content/army-books`
Expected: no change (the books are regenerated byte for byte).

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.ArmyBooks.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS (`docker compose up -d` and the management restart of Task 3 done, so Postgres has `008` and `009`).

- [ ] **Step 8: Run the pattern games and the suite in process**

Run: `dotnet run --project sim -- suite --only pattern-aura-stealth,pattern-buff-stealth,pattern-mark-rending,pattern-debuff-defense`
Expected: four `pass` lines. If the Mark or Debuff game fails `no attack under the grant`, seed 1's deployment put no enemy within 18" of the carrier: give that game `seed:` the first value of 2..20 for which this command passes, and
note the seed in the game's comment.

Run: `dotnet run --project sim -- suite`
Expected: the last line starts `PASSED:`; the coverage header shows `… by pattern` with 60 wrappers and `173 not
automated`; no `UNCOVERED` or `NOT FIRED` line.

- [ ] **Step 9: Run the suite over the socket**

Run: `docker compose restart game && sleep 30 && dotnet run --project sim -- suite --driver socket`
Expected: the last line starts `PASSED:`, with the same coverage counts.

- [ ] **Step 10: Commit**

```bash
git add server/game/Content/grimdark-future-army-rules.part.json server/game/Content/grimdark-future-army-book-rules.part.json \
  sim/Suite/SuiteCatalog.Army.cs sim/Suite/SuiteCatalog.Army.Patterns.Checks.cs sim/Suite/SuiteCatalog.Army.Patterns.cs \
  tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs tests/Tabletop.GameServer.Tests/ArmyRulesContentTests.cs
git commit -m "Generate the army-book rules part and play one game per pattern" -m "$(./scripts/army-books generate | tail -n +3)"
```

`generate` rewrites the same bytes, so the second message is Step 6's report.
