# Phase 3: the suite

All Opus, group J. `sim/AGENTS.md` → The rules suite and → Writing a suite game that holds are binding: place by construction, script by name, pin only the dice a check reads with a `RollKey`, seeds 1–5 in the source before restoring. Games go in `sim/Suite/SuiteCatalog.Army36M.cs` (+ `Army36M2.cs` past 250 lines) with checks in `sim/Suite/Checks/Army36M.cs`, on the 3.6 book that prints the rule (`Books36`), `.In36()`, each check quoting the 3.6 text; reuse `Army36L`'s helpers (`Modifier`, `GrantedModifier`, `Rerolls`, `ExtraAttacks`, `Reposition`, `Most`, `Tough12L`). The spec's Rulings paragraph settles what each game must show.

---

### Task 3.1: the weapon rules, Brace, Parry — **Opus**, after 2.3

| Game | Book | Shows |
| --- | --- | --- |
| `…-pierce-far` / `…-pierce-near` | Knight Prime Brothers | from over half the weapon's range: AP(+1) granted and cover's +1 dropped (`Ignored` names Pierce); from under: no AP(+1), cover's +1 dropped still |
| `…-smash` | Battle Brothers (Anti-Matter-Sniper Carbine) | a defense 6 re-rolled from over half range; not from under (`Rerolls` / a `NoReroll` check) |
| `…-destructive` | Eternal Dynasty | AP(+1) against a 5-model unit at full strength; none once it is at 2 (a `DiceFix` kills three first) |
| `…-decimate` | Daemons of Plague (Spore Cannon) | defense 6s re-rolled against a Tough(6) model at 0 wounds; not at 3 |
| `…-scourge-melee` | Havoc Brothers (Heavy Chain-Cannon as Assault) or Dwarf Guilds | a melee the charger wins on counted wounds alone (defense dice fixed to 1s), the defender's morale test |
| `…-scourge-morale` | Change Disciples | a shooting that wounds nobody, two 1s fixed, a 5-model target tests at the activation's end |
| `…-brace` | Daemons of War | a charge at Spear models: Impact dice = Σ − 1 (a `RollCount` check on the `Impact` record) |
| `…-brace-fatigued` | Daemons of Lust | the same defender Fatigued: no die taken |
| `…-parry` | Daemons of Change | hits taken in melee, `Parry` dice = min(X, hits), fixed 6s ignore hits (`hits` less `parried` in `roll-to-block`) |

- [ ] Checks added to `Checks/Army36M.cs`: `NoReroll`, `RollCount(roll, expected)`, `CountedWounds(expected)`, `Parried(expected)`, `IgnoredModifier(rule, source)`; every check quotes the text.
- [ ] `dotnet run --project sim -- suite --only army:3.6:<each uid>` passes seeds 1–5; `dotnet test tests/Tabletop.Sim.Tests`.

---

### Task 3.2: picks, choices, moves, Auras, Quick Throw — **Opus**, after 2.4

| Game | Book | Shows |
| --- | --- | --- |
| `…-mind-control` | Jackals | the pick, a failed test (dice fixed), the 6" forced move answered by the caster's team; a second carrier is not offered the moved unit this round |
| `…-piercing-tag` | Human Inquisition | two carriers tag one unit (X 1 each): two tokens; the first friendly attack reads AP(+1) once and leaves one token; the second spends the last |
| `…-precision-tag` | Jackals | the same shape on the hit roll |
| `…-versatile-defense-shot` | Plague Disciples | shot from over 9": the defender's `choose_effect` scripted to +1 Defense, the block plan shows it; the next attack-sequence asks again, scripted to -1 to hit |
| `…-versatile-defense-near` | Lust Disciples | shot from under 9": no Ask (`NoAsk("choose_effect")`) |
| `…-enhanced-wolfborn` | Wolf Brothers | the Wolfborn reposition rolls two dice (`RollCount("Wolfborn", 2)`), moves the carriers only |
| `…-enhanced-rapid-blink` | Elven Jesters | the same over Rapid Blink |
| `…-ravage-aura` | Daemons of Plague | the strike rolls `Ravage` with own X + one per living model |
| `…-spell-drain` | Human Inquisition | the pick on an enemy caster model, a failed test, its tokens 2 fewer (`TokensLost(model, 2)`) |
| `…-quick-throw` | Ratmen Clans | an Advance with an Indirect weapon at a target out of sight: the attack is made and `Applied` names Quick Throw and Indirect; the same unit without the rule has no eligible shooter |

The Auras (Enhanced Rapid Blink Aura, Enhanced Wolfborn Aura, Versatile Defense Aura, Ravage Aura) are covered by pattern once their bases' games pass and fire; where the pattern rule does not reach one (a name whose last word is not the base), give it its own game on the book that carries it.

- [ ] Checks added: `NoAsk(question)`, `TokensLost`, `Chosen(condition)`, `MovedBy(rule, maxInches)`; seeds 1–5 per game.

---

### Task 3.3: the spell tables and the three fixes — **Opus**, after 2.5

Files: `sim/Suite/SuiteCatalog.Spells36.cs` (`SpellTables36`: `Setups`, `Dice`, `Checks` rows so every book's newly played spell is cast at least once: the 24 grant spells (Pierce 7, Smash 6, Decimate 4, Scourge 2, Destructive 2, Enhanced Wolfborn 2, Enhanced Rapid Blink 1), Burn the Heretic, Shifting Form ×3, Triangulation Bots; a check per new effect kind quoting a spell's text: `remove_spell_tokens` (Shifting Form), a grant of Pierce acting (one `GrantCheck36`-style constant each for a grant of Smash, Decimate, Scourge, Destructive, Enhanced Wolfborn, Enhanced Rapid Blink, Indirect)), `SuiteCatalog.Army36M.cs` (the fixes' games):

| Game | Book | Shows |
| --- | --- | --- |
| `…-thrust-in-melee-buff-ap` | Human Defense Force | the buffed charge reads +1 to hit **and** AP(+1) in one attack, the grant removed at `attack-end` |
| `…-bane-mark-regeneration` | a book with Bane Mark | the marked target's Regeneration rolls nothing on the marked attack and the 6 is re-rolled |
| `…-unstoppable-mark-both` | a book with Unstoppable Mark | Regeneration ignored and the foreign to-hit penalty dropped in one attack |
| `…-predator-asks` | Alien Hives | two 6s fixed: the `extra_attacks` Ask opens; scripted Yes adds two dice; a sibling game scripted No adds none |

- [ ] `dotnet run --project sim -- suite --only spells:3.6` passes (every 3.6 book's game casts a played spell); the fixes' games seeds 1–5.

---

### Task 3.4: the full run, the coverage test, seeds — **Opus**, after 3.1–3.3

Files: `tests/Tabletop.Sim.Tests/Suite/*Coverage*` (the 3.6 expectation: `Uncovered36` empty, not-automated 0; the 3.5 expectation unchanged at 545 with 0), `sim/Suite/SuiteCatalog.cs` (`All` reads `Army36M`), `docs/handoff.md` → The rules suite (the numbers: games per edition, coverage per set).

- [ ] `dotnet run --project sim -- suite` reads `PASSED: N of N games passed, 0 failing on known defects, 0 unexpected; … 0 not automated`; `recordings/suite-latest.md` coverage 3.5 `545 of 545 (… 0 not automated)`, 3.6 `M of M (… 0 not automated)`.
- [ ] Every game of this plan passes seeds 1–5 (each executor did; this task spot-checks ten with another seed) and the run time is noted.
- [ ] `dotnet test tests/Tabletop.Sim.Tests` passes with the new expectations.
