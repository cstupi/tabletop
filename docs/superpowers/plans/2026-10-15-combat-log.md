# Combat log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The combat log draws one line per roll with its printed target, the rules behind every modifier, the cover verdict on every
shooting block, and what the dice set off; it drops every line that is not a roll, an action or a change to the table.

**Architecture:** Three optional wire fields (`RollModifier.Via`, `Die.Triggered`, `RollRecord.Cover`) filled by the rules engine in
`server/game`; the client's Godot-free `CombatLog`/`RollEntry` compose the words from the record and name no rule; `CombatLogPanel` and
`DiceTray` render them.

**Tech Stack:** .NET 10, xUnit, Godot 4.7 C# client, generated TypeScript types for the viewer.

**Spec:** `docs/superpowers/specs/2026-10-13-combat-log-design.md`

## Global Constraints

- The rules run only on the server; nothing in `client/` names a rule. Words the client composes come from record names and numbers.
- A log written before a field reads as before: every new field is optional and null in old records.
- p.5: a 6 always succeeds and a 1 always fails, so `needs N` is clamped to 2..6.
- The cover verdict is one per target unit (p.11 majority): it is the same on every block group of a shooting attack; melee has none.
- `client/src/Table/`, `DecisionBox*`, `Roster`, `HoverLabels`, `UnitMarkers*`, `EndMessage`, `MenuModel` are not touched.
- After a wire change: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`.

## Review Focus

- A Blast weapon fired at a unit in cover: the block line must say `cover ignored (Blast)`, not `no cover`.
- A hit die a forced reroll or `SetDice` replaces after the roll: its `Triggered` is gone with the old face; the line counts only dice still carrying one.
- A Rending group and a plain group in one attack: both block lines carry the same cover clause; only the Rending one shows `Rending AP -4`.
- An old log (no `Basis`, no `Cover`): the block line still reads `Block · 5+ (AP -1) → needs 6`.
- A roll outside any action (a morale test from a menu-less frame): it still reads as a roll line, not a dropped event.

---

### Task 1: Wire fields

**Files:**
- Modify: `libs/Tabletop.Protocol/State.cs:25-36`
- Test: `tests/Tabletop.Protocol.Tests/` (a round-trip test in the existing wire test file)
- Regenerate: `viewer/src/lib/protocol/*.generated.ts`

**Interfaces:**
- Produces: `Die(int Raw, int Modified, bool Success, IReadOnlyList<string>? Triggered = null)`,
  `RollModifier(string Source, int Amount, string? Via = null)`,
  `CoverVerdict(bool InCover, int Models, int Of, string? IgnoredBy = null)`,
  `RollRecord(..., ThresholdBasis? Basis = null, CoverVerdict? Cover = null)`.
- `CoverVerdict` semantics: `InCover` is p.11's verdict for the target unit; `Models`/`Of` the models counted in cover and the unit's
  living models; `IgnoredBy` names the rule that took the +1 away from a unit that had cover (Blast's suppression, Indirect's dropped
  sight half). The +1 applies iff `InCover && IgnoredBy is null`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void A_roll_record_round_trips_its_cover_verdict_modifier_grantor_and_die_triggers()
{
    var r = new RollRecord(Guid.CreateVersion7(), "defense", 5, [new RollModifier("AP", -4, "Rending")],
        [new Die(6, 2, true, ["Rending"])], null, null, new CoverVerdict(true, 4, 6, "Blast"));
    var back = JsonSerializer.Deserialize<RollRecord>(JsonSerializer.Serialize(r, WireJson.Options), WireJson.Options)!;
    Assert.Equal(new CoverVerdict(true, 4, 6, "Blast"), back.Cover);
    Assert.Equal("Rending", back.Modifiers[0].Via);
    Assert.Equal(["Rending"], back.Dice[0].Triggered!);
    Assert.DoesNotContain("triggered", JsonSerializer.Serialize(new Die(1, 1, false), WireJson.Options), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Protocol.Tests` — FAIL (no `CoverVerdict`).
- [ ] **Step 3: Implement** the four records in `State.cs` with doc comments stating the semantics above.
- [ ] **Step 4: Run** Protocol tests — PASS; then `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests` and a plain run — PASS.
- [ ] **Step 5: Commit** (local, folded into the final commit).
- `Die` keeps value equality: it overrides `Equals`/`GetHashCode` so two dice with equal `Triggered` sequences are equal.

### Task 2: The rule behind a modifier (`Via`)

**Files:**
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveProperty.Via`, `LiveSet.WithGranted`, `LiveSet.GrantorOf`)
- Modify: `server/game/Effects/RollEffects.cs` (`Attributed`)
- Test: `tests/Tabletop.GameServer.Tests/GfCombatLogTests.cs` (new)

**Interfaces:**
- Consumes: `RollModifier.Via` (Task 1).
- Produces: `LiveProperty(..., string? Via = null)`; `LiveSet.GrantorOf(LiveProperty) : string?` (the entry that granted it in pass
  one); `WithGranted(holder, entry, parameter)` finds the live property whose per-die `grant_property` row targets `entry` with that
  amount and stamps its entry name as `Via`. The `granted_` key format of `score-hits` is unchanged: the grantor is read off the live
  set in `roll-to-block`, which holds the same weapon rules.
- `Attributed` builds `new RollModifier(src.Entry.Name, amount, src.Via ?? live.GrantorOf(src))`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void A_rending_six_blocks_at_ap_one_plus_rending_ap_four_and_the_weapons_own_ap_has_no_grantor()
{
    var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Saw", "24\"", 2, P("AP", 1), P("Rending")));
    var sim = Attack(shooter, Unit("B", "4+", "5+", [], 5, []), 6, 4, 3, 3);       // a six and a four hit; both blocks fail
    var blocks = sim.Steps("roll-to-block").Single().Rolls;
    Assert.Contains(blocks, r => r.Modifiers.SequenceEqual([new RollModifier("AP", -1), new RollModifier("AP", -4, "Rending")]));
    Assert.Contains(blocks, r => r.Modifiers.SequenceEqual([new RollModifier("AP", -1)]));
}
```

(`Attack` is the existing helper shape in `DiceEditTests`: one shooter, one target, scripted dice, no waits.)

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.GameServer.Tests --filter GfCombatLogTests` — FAIL (Via null).
- [ ] **Step 3: Implement** `Via` on `LiveProperty`, `GrantorOf` from `PassOneActs.GrantedBy`, the lookup in `WithGranted`, and
  `Attributed`'s modifier.
- [ ] **Step 4: Run** the filter, then the whole GameServer suite — PASS.

### Task 3: What a die set off (`Triggered`)

**Files:**
- Modify: `server/game/Effects/RollEffects.cs` (new `Triggers`)
- Modify: `server/game/Interpreter/StepContext.cs` (`Roll(plan, count, triggers?)`)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`RollToHitStep`)
- Test: `tests/Tabletop.GameServer.Tests/GfCombatLogTests.cs`

**Interfaces:**
- Produces: `RollEffects.Triggers(LiveSet live, Side roller, Die die, string roll) : IReadOnlyList<string>?` — pure (records nothing).
  On a success: each roll-result `add_hit` row on hits gives `"{Entry} +{n} hit(s)"`, on attacks `"{Entry} +{n} attack(s)"`; each
  roll-result `grant_property` row gives `"{Entry}"` (the granting rule, e.g. Rending). Null when nothing.
- `StepContext.Roll(RollPlan plan, int count, Func<Die, IReadOnlyList<string>?>? triggers = null)` stamps `Triggered` on the record's
  dice and on forced-reroll replacements before they are logged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void A_rending_six_to_hit_is_marked_with_rending_and_a_four_with_nothing()
{
    var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Saw", "24\"", 2, P("Rending")));
    var sim = Attack(shooter, Unit("B", "4+", "6+", [], 5, []), 6, 4, 1, 1);
    var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
    Assert.Equal(["Rending"], hit.Dice[0].Triggered!);
    Assert.Null(hit.Dice[1].Triggered);
}

[Fact]
public void A_furious_chargers_six_is_marked_with_its_extra_hit()
{
    // A Furious unit charges; its first melee hit die is a six (scripted after the charge's dice).
    // Assert: the charger's hit roll holds a die with Raw 6 and Triggered ["Furious +1 hit"].
}
```

(The Furious test uses the charge harness already used by `GfMeleeTests` for a Furious charge; copy its setup verbatim.)

- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** `Triggers`, the `StepContext.Roll` overload, and pass `d => RollEffects.Triggers(live, Side.Attacker, d, GfNames.Hit)`
  from both rolls in `RollToHitStep`.
- [ ] **Step 4: Run** the GameServer suite — PASS (existing `Die` equality holds by Task 1's `Equals`).

### Task 4: The cover verdict on every block

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfRolls.cs` (`Cover` returning counts; `InCover` kept as `Cover(...).Majority`; `DefensePlan`)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`CoverOf`, `InCoverAgainst`, `DetermineAttacksStep`, `RollToBlockStep`)
- Modify: `server/game/Rules/GrimdarkFuture/GfShooting.cs:112` (`fire-groups` settles the counts)
- Modify: `server/game/Dice/RollPlan.cs`, `server/game/Dice/Roller.cs` (`RollPlan.Cover` copied onto the record)
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveSet.Suppressors(TargetModifier) : IReadOnlyList<string>`)
- Test: `tests/Tabletop.GameServer.Tests/GfCoverTests.cs`

**Interfaces:**
- Produces: `GfRolls.CoverCount(int Models, int Of) { bool Majority }`;
  `GfRolls.Cover(GameState, Guid targetUnit, IReadOnlyList<Guid> attackers, bool ignoresSightObstructions) : CoverCount`;
  `GfAttack.InCoverAgainst(...) : CoverVerdict`; `GfRolls.DefensePlan(StepContext, LiveSet, CoverVerdict?, int defenderModels)`.
- Values: next to `in_cover` (unchanged meaning: the +1 test passed), `cover_models`, `cover_of` and `cover_indirect` (1 when Indirect's
  dropped sight half lost a majority the full test had); `fire-groups` writes the same with the `_<i>` suffix. A parent with `in_cover`
  and no `cover_of` (a frame written before) gives `Cover` null.
- `roll-to-block` builds the verdict once and passes it to every group's `DefensePlan`. `DefensePlan` adds `cover +1` iff
  `InCover && IgnoredBy is null`, and when the plan came back without it sets `IgnoredBy` to the suppressor (`live.Suppressors(Cover)`,
  Blast). Melee and spells pass null.

- [ ] **Step 1: Write the failing tests** (in `GfCoverTests`, on the existing layout)

```csharp
[Fact]
public void A_screened_single_model_blocks_with_a_verdict_of_one_of_one_in_cover()
{
    var (shooter, target, screen) = (Rifle(), Unit("Lone", "4+", "6+", [], 1, []), Wall());
    var sim = Laid(shooter, target, screen, [Screened], wood: false, 6, 5);
    sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
    Assert.Equal(new CoverVerdict(true, 1, 1), sim.Steps("roll-to-block").Single().Rolls.Single().Cover);
}

[Fact]
public void One_screened_model_of_two_blocks_with_a_verdict_of_one_of_two_not_in_cover() { /* Laid([Screened, Open]); Cover == (false, 1, 2) */ }

[Fact]
public void Blast_at_a_unit_in_cover_records_the_verdict_and_names_blast_as_what_ignored_it()
{ /* Gun with P("Blast", 3); target screened; Cover == (true, 1, 1, "Blast"); no "cover" modifier */ }

[Fact]
public void Indirect_dropping_the_sight_half_names_indirect() { /* the Indirect test's first layout: Cover == (true, 1, 1, "Indirect") */ }

[Fact]
public void A_hold_action_carries_the_verdict_settled_by_fire_groups() { /* Hold via the action; every roll-to-block record has the same Cover */ }
```

- [ ] **Step 2: Run** `--filter GfCoverTests` — FAIL.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** the whole GameServer suite — PASS.

### Task 5: The roll line (client)

**Files:**
- Modify: `client/src/Core/RollEntry.cs`
- Test: `tests/Tabletop.Client.Tests/CombatLogTests.cs`

**Interfaces:**
- Produces on `RollEntry`: `Label` (`Hit`, `Block`, `Morale`, `Casting`, else the capitalised roll name — the roll names are wire
  names, the words are the spec's); `Target` (`Defense 5+ (no cover: 2/6 models, AP -1) → needs 6`); `Result` (`4 hits (3 sixes:
  Rending)`, `1 blocked, 2 wounds`, `passed`/`failed`, `2 of 3 passed`, empty when unjudged); `Line(Func<int,string> face)` the
  collapsed line; `Details` (the expanded lines: basis, each modifier, the verdict's counts, each die, amendments).
- `Target`: printed part from `Basis` (`{Stat} {Printed}+`) or `{Threshold}+` when absent; clauses in order: the cover verdict when
  `Cover` is set and not applied (`no cover: M/N models`, `cover ignored (X)`), each basis change (`{Source} {SetTo}+` or `{Source}
  ±n`), each modifier (`{Via} {Source} ±n`); `→ needs N` when anything changed the printed value, N = threshold − net modifier,
  clamped 2..6; `RequiredRaw` reads `unmodified {r}+`, nothing when it is 1.
- Result for `hit`: successes + the sum of `+n hit(s)` triggers; the parenthesis groups triggered dice by rule:
  `(3 sixes: Rending)`, `(2 sixes: Furious +2 hits)`; `sixes` is the face word when every die of the group shows one face, else `dice`.
- Amendments on the line: `rerolled ⚀ → ⚄`, `set by hand ⚀ → ⚅`.

- [ ] **Step 1: Write the failing tests** (replace the three roll-shape tests):

```csharp
Assert.Equal("Block · Defense 5+ (cover +1, AP -1) → needs 5 · 3 dice: ⚅ ⚁ ⚀ · 1 blocked, 2 wounds", roll.Line(CombatLog.Pip));
Assert.Equal("Block · Defense 5+ (no cover: 2/6 models, AP -1) → needs 6 · ⚀ · 0 blocked, 1 wound", ...);
Assert.Equal("Block · Defense 5+ (cover ignored (Blast)) · ⚃ · 1 blocked, 0 wounds", ...);
Assert.Equal("Hit · Quality 3+ · 8 dice: ⚁ ⚅ ⚀ ⚅ ⚅ ⚀ ⚄ ⚀ · 4 hits (3 sixes: Rending)", ...);
Assert.Equal("Hit · Quality 4+ · 2 dice: ⚅ ⚅ · 4 hits (2 sixes: Furious +2 hits)", ...);
Assert.Equal("Block · 5+ (AP -1) → needs 6 · ⚀ · 0 blocked, 1 wound", ...);          // an old record: no basis, no verdict
Assert.Equal("Morale · Quality 4+ · ⚀ · failed", ...);
Assert.Equal("Hit · Quality 4+ · 2 dice: ⚀ ⚄ · 1 hit · rerolled ⚀ → ⚄", ...);
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Client.Tests` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.

### Task 6: What the log draws and drops (client)

**Files:**
- Modify: `client/src/Core/CombatLog.cs`, `client/src/Core/LogText.cs`
- Test: `tests/Tabletop.Client.Tests/CombatLogTests.cs`, `tests/Tabletop.Client.Tests/UndoPlanTests.cs` (expectations that read lines)

**Interfaces:**
- Header: `R{round} · {team} · {unit} · {action}[ → {targets}][ · moved {d}"]`, `d` the farthest any model of the actor moved
  (summed over the block's table-to-table moves per model) to 0.5".
- Dropped: `AskOpened`, `AskAnswered`, `AskClosed`, `SequenceStarted` without an action, `SequenceFinished`, `SequenceAbandoned`,
  `TurnStarted`, `TurnEnded`, a `StepCompleted` with no roll and no change, and `DiceRerolled`/`DiceEdited` of a roll in the log
  (already on its line).
- Change lines: `N models wounded`, `N models removed` (wounded to the shelf), `{unit} Fled!` (a step that sends models to the shelf
  with no wound on them: they left the table, not died), `Shaken`, `Shaken cleared`. A change in a later roll-less step of a
  single-die test's frame goes on the test's line as its consequence (`failed → Shaken`; spec → Consequence).
- Outside actions: `Round N`; `Undone to: {header of the block the undo reached}` (else `Undone to #{seq}`); a menu edit
  (`StateChange` payload with an actor) is LogText's line with ` · by hand`; the rest LogText's.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Questions_answers_closes_empty_steps_and_frame_bookkeeping_are_not_drawn() { /* a charge with asks and no rolls: header only */ }
[Fact]
public void A_header_names_the_round_team_unit_action_targets_and_the_farthest_move() { /* "R2 · Team 1 · Battle Brothers · Charge → X · moved 7.5\"" */ }
[Fact]
public void A_unit_sent_to_the_shelf_unwounded_after_a_failed_morale_test_fled() { /* "Morale · Quality 4+ · ⚀ · failed", then "Battle Brothers Fled!" */ }
[Fact]
public void An_undo_reads_undone_to_the_header_it_reached() { /* "Undone to: R1 · Team 1 · Battle Brothers · Advance" */ }
```

- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement**; keep `ActivationBlock`, `Filter`, `Latest` signatures.
- [ ] **Step 4: Run** the Client suite — PASS.

### Task 7: Panel and tray

**Files:**
- Modify: `client/src/Hud/CombatLogPanel.cs`, `client/src/Hud/DiceTray.cs`

- Panel: a block is open by default (its lines indented beneath the header); clicking the header folds it to the header and its chain.
  A roll row is `RollEntry.Line`, the faces drawn bright or faded by success (`DiceFaces`) next to the text; clicking it expands
  `Details`, the rule chips and Undo to here. A change or outside line has Undo to here.
- Tray: the latest roll's `Line` pieces: label and target, the faces, the result.
- [ ] **Step 1:** `dotnet build client/Tabletop.csproj` and `./scripts/check-client` — clean.

### Task 8: Validation and docs

- [ ] **Step 1:** `dotnet test tests/Tabletop.GameServer.Tests`, `tests/Tabletop.Protocol.Tests`, `tests/Tabletop.Client.Tests`,
  `tests/Tabletop.Sim.Tests`, `tests/Tabletop.Viewer.Tests` — PASS.
- [ ] **Step 2:** `dotnet run --project sim -- suite --only <a cover game> --driver socket --keep`; read the recording's
  `roll-to-block` records: `cover` present on every shooting block. Render its lines through `CombatLog.Build` (a throwaway test or
  the sim's recording read in the Client tests) and quote them in the report.
- [ ] **Step 3:** Amend the spec's "What the server adds" with `RollRecord.Cover` and the Block line's cover clause; correct its `Via`
  bullet (the grantor is read from the live set, the `granted_` key is unchanged). Update `server/game/AGENTS.md`, `client/AGENTS.md`,
  `libs/Tabletop.Protocol/AGENTS.md`, `docs/design/interpreter/opr-sequences.md` (the cover keys) with the current state.
- [ ] **Step 4:** `git add -A && git commit -qm "Combat log: one line per roll, cover verdict on every block"`.
