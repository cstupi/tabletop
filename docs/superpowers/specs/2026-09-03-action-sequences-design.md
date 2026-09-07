# Action sequences

Design session, 2026-09-03. The machinery that runs an action step by step, and Grimdark
Future's sequences written out of the rulebook.

[Resolution](2026-08-28-resolution-design.md) designed the shape — two orthogonal settings, a
half-resolved pipeline as durable state, modifiers split from abilities — and deliberately
excluded every step list. This session writes the step lists and the interpreter they run on.
It closes three questions that were open behind it: whether a waiting pipeline can be
abandoned, what consumes the effect grammar's `order` field, and what `Caster`'s token economy
needs.

## Read, not reasoned

Two rulesets were read for this session:

- **Grimdark Future v3.4.1**, pages 7–14 (actions, shooting, charge and melee, morale,
  terrain, every special rule), plus 18, 30–32 and 43 for the token economy's edges.
- **Warhammer 40,000 10th edition core rules** (Wahapedia), as a **falsifier**: the attack
  sequence, phase structure, Battle-shock, modifier caps, and the core Stratagems' timing
  clauses. The interface is designed against both rulesets; **only OPR's `GameRules` gets
  built**, per `architecture.md`'s generalise-on-the-second-example rule.
- One army book (Alien Hives, via the Army Forge API) for the shape of real spells.

## Shooting and melee are one sequence

Pages 8 and 9 give the same four steps word for word: **Determine Attacks → Roll to Hit →
Roll to Block → Remove Casualties**. There is one attack sequence invoked from two contexts,
not two pipelines. `architecture.md`'s sketch (*declare target → range/LoS → hit rolls →
defense rolls → apply wounds → morale*) put targeting and morale inside it; both turn out to
live outside — targeting in the action, morale on a trigger of its own.

## The resolution stack

A game's state gains a **resolution stack**: frames, one per sequence in flight. A frame is
the sequence's name, the context it was invoked with (acting unit, target, weapon group), its
position, and the values completed steps have produced — the hit count Roll to Block consumes,
the distance a checkpointed move has already used.

**The stack is never stored.** It is rebuilt by replaying the log, exactly as the action
record is (`schema.md`: *"reconstructed from the event log and never a row"*). Four event
kinds carry it:

- `sequence_started` — name and context
- `step_completed` — the step's name, plus everything it produced: choices made, rolls, state
  changes applied
- `sequence_finished`
- `sequence_abandoned`

A logged roll carries **threshold, modifiers, and each die's raw and modified result** — the
08-30 roll-record decision, extended per die because four properties trigger on unmodified 6s
and 40k's critical hits do the same.

**Replay applies recorded results; it never re-computes.** A replayed step does not re-roll or
re-derive a threshold — it applies what the event says happened. Same reasoning that put
thresholds in the event: `GameRules` is unpinned C#, so re-derivation after a deploy could
disagree with what the players rolled. A step event naming its step makes a step-list change
detectable on replay rather than silent — the accepted risk `architecture.md` records.

**Why a stack and not a call stack:** a C# continuation cannot be serialised, so an
async/await rendering of these sequences cannot be durable state, cannot survive the
hydrate-from-log path that [accounts](2026-08-29-accounts-design.md) made the only recovery
path, and cannot be replayed for undo. Three standing decisions rule it out at once. The cost
accepted instead: **a step may hold no C# locals across a suspension** — everything it needs
must be in game state or the frame.

## Abandonment

Open since the resolution session; the stack answers it. Abandoning is an ordinary logged
command that pops the frame and everything above it. **Applied effects stay applied; unapplied
steps never happen.** No rollback — nothing is an invariant, and inverse events were already
rejected. Players fix anything they dislike from the menu, or undo past the whole sequence by
replay. Any participant may abandon; the log says who.

## A step ends one of four ways

A step is a C# object supplied by `GameRules`; the interpreter that runs frames is generic and
knows no ruleset. A step reads game state and its frame, and ends by:

1. **Completing** — logs `step_completed`, the frame advances.
2. **Asking** — the frame suspends, naming a team and a question with the server-computed
   legal answers. It resumes when the matching command arrives. This is the mandatory-input
   case the resolution session missed: the rulebook requires defender input mid-sequence —
   casualty removal, strike-back, `Deadly` assignment — so waiting is not only a
   configuration.
3. **Pushing** — a nested sequence starts; the frame resumes when it finishes. Charge pushes
   melee; melee pushes resolve-hits; a spell pushes resolve-hits directly.
4. **Opening a window** — an interrupt point, below.

There is no branch primitive and no master pipeline: sequences are lists the ruleset composes,
and conditional flow is C# inside a step. A ruleset never no-ops a step it does not need — it
simply does not list one.

## One roll primitive

Roll N dice against a threshold, with modifiers. Split:

| The generic engine owns | `GameRules` supplies per roll |
| --- | --- |
| the seeded RNG in game state | the dice convention (OPR 1D6 ≥ N; 40k Battle-shock 2D6 ≥ Ld) |
| recording raw and modified per die | the threshold derivation (read Quality; 40k's S-vs-T lookup) |
| the two effect passes, in `ordinal` order | the modifier cap (OPR none; 40k ±1 after summing) |
| folding in player-added modifiers | |

Summing is generic; clamping is ruleset. The pooled primitive serves both granularities: OPR
pools a unit's attacks by rule, and 40k's own Fast Dice Rolling legitimises pooling identical
attacks, with per-attack allocation happening in a later step that consumes the pool.

**Effects apply at the roll or step they attach to**, via the two passes from the effect
grammar: settle which properties are live and their parameters (grant, suppress,
parameter-modify, by `ordinal`), then apply what remains. The engine consults the pinned
`Properties` data; `GameRules` never enumerates abilities.

**Rerolls and hand-added modifiers stay commands between steps**, unchanged from the
resolution design. When the wait-between-steps setting is off, the interpreter runs ahead
until an Ask; when on, every step boundary is a confirm gate — the gate is an Ask owned by the
interpreter rather than the rules. **Pausing is an Ask, so confirming is answering it**: one
input path, not a second mechanism.

## Asks

An Ask is addressed to a **team** — activation authority is team-wide, per
[game flow](2026-08-28-game-flow-design.md) — and carries a question kind and the
server-computed legal set. The client renders it as the thing to answer; any team member may,
and the log records who did.

What the legal set *means* is the enforcement setting's business: under `strict` an
out-of-set answer is refused; under `warn` it is accepted and logged with a warning; under
`silent` it is accepted. (This sentence reopened identity and enumerations' boolean, and the
review of 09-04 kept the refusing position.) This is enumeration-rather-than-validation applied
to resolution.

## Windows

**A window is an Ask about interrupting.** A step declares a named point — *charge declared*,
*before the casting roll*, *before attacking* — and the engine checks whether anything can
actually react there: a property in the pinned data, a `GameRules` hook, a caster with tokens
in range. **If nothing can react, no window opens and nobody is bothered.** If something can,
each eligible team gets an explicit Ask: **react or pass, and passing is a real answer,
logged.** A reaction pushes its sequence on top of the suspended frame; the window closes when
every offered team has passed or its reaction has finished. Clients may auto-pass window kinds
a player has muted — client convenience, not model; the pass is still their pass.

Two ways a window opens, one suspension:

- **Offered** — to teams other than the actor. OPR core barely uses this; 40k's Fire
  Overwatch — *"your opponent's Movement or Charge phase, just after an enemy unit is set up
  or when an enemy unit starts or ends a Normal, Advance or Fall Back move, or declares a
  charge"* — pushes a whole shooting sequence into an enemy move step, and the model carries
  it without extension.
- **Invoked** — by the actor, at will, inside its own step. Casting is this: *"at any point
  before attacking"* includes **mid-move**, so the acting team may checkpoint a move — the
  current position logs as a settled stop, the same debounced shape the table model already
  uses — cast, then resume the move with its remaining budget. The frame holds
  distance-used-so-far.

**One window variant is simultaneous and sealed**: every eligible team answers the same Ask
concurrently, answers withheld from the other teams until the window closes, then revealed
together. It exists for one OPR rule — casting's simultaneous token spend — because online,
sequential reveal hands the last mover an advantage the tabletop does not. It is the first real
use of the per-player filtered streams `architecture.md` kept available.

*Renamed 2026-09-06.* "Sealed bid", "token bid" and "bidding" were invented for this document;
the rule has no bid, no auction and no winner. The rulebook has models spending tokens at the
same time, p.13, `Caster(X)`: *"Models within 18" in line of sight of the caster's unit may
spend any number of spell tokens at the same time before rolling, to give the caster +1/-1 to the
roll per token."* The behaviour designed here is unchanged, and "at the same time" is why the
answers are withheld until the window closes. The names are `SimultaneousSpend` for the window
variant, `TokenSpend` for the legal set, `TokensSpent` for the answer and `spend_tokens` for the
question. Every other document that named this mechanic points here.

When several teams want to react in one window, who goes first is the **scheduler's**
question, not the sequence's. That seam is left for the scheduler session.

## Grimdark Future's sequences

Three shared sub-sequences do most of the work:

- **deal-wounds** — assign each wound: `Tough` stacking onto the most-wounded tough model
  until it dies, heroes assigned last (p.14); `Deadly(X)` assigns then multiplies, resolved
  first, no carry-over (p.13). `Regeneration` roll per wound unless suppressed. Casualties go
  to the shelf. Records wounds-this-activation for the morale threshold — a per-unit counter in
  game state, like spell tokens, cleared at `turn_ended` (placed 09-04; it must outlive the
  frame that wrote it).
- **resolve-hits** — Roll to Block, one die per hit against Defense (`AP`, cover modify it);
  fails become wounds; push deal-wounds. `Impact` hits enter here, skipping the hit roll
  entirely; spell hits enter after a hit roll that cannot fail, so *on rolls of 6* rules read a
  real die ([spell effects](2026-09-04-spell-effects-design.md), correcting "spells skip the hit
  roll").
- **attack-sequence** — Determine Attacks: sum Attack of models in range and line of sight
  (p.8) → Roll to Hit: Quality tests as a pool; unmodified 6s feed `Furious`, `Relentless`,
  `Rending`; `Blast(X)` multiplies after other rules, capped at models in the target unit →
  push resolve-hits.

The actions:

- **Shooting** (p.8) — Ask: declare weapon groups and up to two targets, *before any roll* →
  attack-sequence per group, `Sniper` groups first (p.14).
- **Charge** (p.9–10) — Ask target: within charge distance including special-rule movement,
  clear path, no line of sight needed → move, ignoring the 1" restriction → defender's models
  not in contact move up to 3" into contact (their models, their Ask) → push **melee**:
  - defender's `Counter` weapons strike first — attack-sequence;
  - charger's `Impact(X)` rolls unless fatigued, minus one total per `Counter` model — X dice
    at 2+, hits push resolve-hits;
  - charger's attack-sequence (models within 2" horizontally, 4" vertically, all melee
    weapons); charger becomes fatigued;
  - Ask defender: strike back? If yes, defender's attack-sequence — fatigue means hitting only
    on unmodified 6s — and the defender becomes fatigued;
  - compare wounds caused, `Fear(X)` counting as +X (p.13); a destroyed unit always counts as
    the loser; loser pushes a morale test; tie means no test;
  - consolidation: if a unit was destroyed or routed the other may move 3" (Ask); otherwise
    the charger moves back 1".
- **Morale test** (p.10) — one Quality test; Shaken units always fail; `Fearless` re-rolls a
  failure at 4+. A wound-triggered failure means Shaken; a melee-loss failure at half or less
  of starting size (or Tough value, single-model) means Rout — removal only happens on the
  melee path.
- **End-of-activation hook** — units that took wounds this activation leaving them at half or
  less of starting size (or Tough value, single-model) take a morale test; units that fought this melee compare wounds
  instead. Morale is a sequence with ruleset-owned triggers, never a fixed step of the attack
  machinery. The scheduler calls this hook after the declared action's sequence finishes and
  before `turn_ended` ([the scheduler](2026-09-04-scheduler-design.md), added 09-04).
- **Dangerous terrain** (p.11) — per model: one die, or the model's Tough value in dice; each
  1 is a wound → deal-wounds. Invoked by moves through it, activating in it, and transport
  destruction.
- **Casting** (p.13) — an invoked window on the activation, any point before attacking,
  mid-move included → Ask: spell and tokens, cost equal to the spell's value, one try per
  spell → simultaneous token spend window: models with tokens within 18" and line of sight of
  the caster's unit, **both sides**, ±1 per token → 1D6 against 4+, modified → on success Ask
  the targets in line of sight, one to `target_count` of them from the spell's four target
  columns → spell effects per target: hits push resolve-hits after a hit roll that cannot fail,
  grants with `duration = once` write the grants store. *Corrected 2026-09-04 by
  [spell effects](2026-09-04-spell-effects-design.md): this read "Ask the target" and "grants
  write a condition".*

**The token economy.** A counter per model with `Caster(X)`: +X at the start of each round,
even off the table (p.13); capped at 6; spent by casting and by the token spend. The counter is
ordinary editable game state, logged like everything else — no schema change, and nothing in
`Properties` beyond the parameter the entry already carries. Round-start grant is a
`GameRules` hook on the round boundary. Two edge sources exist and need nothing new: army
terrain granting a token on activation nearby (p.18), and the hero upgrade that holds one
token only spendable in a token spend (p.30).

**The four actions are sequences too**, which this document left implicit and the review of
09-04 made explicit: `Hold` is no move, then an Ask *shoot or not* that pushes Shooting;
`Advance` is a move step of the action's distance, then the same Ask; `Rush` is a move step and
nothing else; `Charge` is as written above. The scheduler starts whichever the Ask's answer
named, and the `action` row supplies the distance.

**Fatigue** is a condition set by the melee sequence when a unit first attacks in melee in a
round, cleared at the round boundary — by a third OPR round-boundary hook, *clear round-scoped
conditions*, which [the scheduler](2026-09-04-scheduler-design.md) lists since 09-04 — read by
the melee hit roll and by `Impact`'s predicate.

**Rulings on ambiguities the book leaves.** `Counter` resolves **before** `Impact` — ruled in
this session. Two remain interpretation calls `GameRules` must make visibly: whether *"one try
per spell"* scopes to the activation or the round, and whether a later weapon group's attacks
are recomputed after an earlier group's casualties (targets are declared up front and *"all
weapons are fired simultaneously"*, yet groups resolve in order). Recorded here so the choice
is a flagged interpretation, not a silent guess.

## What the interface must not assume

Each item is a constraint on the generic interpreter — something that must live on the
`GameRules` side of the line — with the 40k rule that falsifies the OPR-shaped assumption:

1. **Roll count and names.** OPR has two rolls; 40k has Hit, Wound, Save, plus Feel No Pain.
   Rolls become rows — below.
2. **Threshold derivation is a function, not a stat reference.** Quality is read; 40k's wound
   roll is a lookup on Strength against Toughness.
3. **Dice conventions are per roll.** 1D6 ≥ N against Battle-shock's 2D6 ≥ Leadership.
4. **Modifier caps are per roll.** 40k clamps hit and wound at ±1 and save improvement at +1;
   OPR never clamps.
5. **Granularity.** OPR pools a unit's attacks; 40k allocates per attack, with pooling of
   identical attacks legitimised by its own Fast Dice Rolling rule.
6. **Damage model.** A model's wound capacity is `Tough(X)` in OPR and the W stat in 40k;
   deal-wounds reads a capacity, not a rule name.
7. **Morale is a sequence with ruleset-owned triggers.** End-of-activation and melee-loss
   against a Command-phase sweep of units below half strength.
8. **Interrupts push whole sequences from the non-active team.** Fire Overwatch is a shooting
   sequence inside an enemy move step.
9. **Who answers an Ask differs per ruleset.** 40k's defender allocates attacks; OPR's
   defender removes casualties in any order. The Ask names its team; nothing assumes the
   actor.

## Rolls become rows

`effect.target_roll` is a `text` check constraint of four OPR values — `hit | defense |
morale | casting` — so supporting 40k's wound and save rolls would be a migration. That
contradicts `schema.md`'s own argument for the `action` table: *"what an effect can target
must be data."* Rolls are the other thing effects target.

So `Properties` gains a `roll` table — `properties_hash`, `name`, nothing else — and
`effect.target_roll` becomes a foreign key into it. Eight tables, not seven. The convention,
threshold derivation and cap stay in `GameRules`, exactly as an action's *"may shoot after"*
does: the row exists because an effect points at it, and only what effects target must be
data.

## Deliberately not decided

- ~~The scheduler: activation legality, and who goes first when several teams react in one
  window.~~ **Settled 2026-09-04** by [the scheduler](2026-09-04-scheduler-design.md):
  reactions resolve in turn order starting with the team after the actor, replaceable by a
  `GameRules` hook.
- 40k's `GameRules`. The core rules were read to falsify the interface, not to build it.
- Advanced Casting's three optional modules — spell books, the psychic pool, living spells
  (p.31–32) — same status as action boosting: recorded, not designed.
- Buildings as targets, unchanged from [game flow](2026-08-28-game-flow-design.md).
- Spell *content* as data. The casting sequence is designed; the rows for real spells wait on
  the effect grammar follow-up below.

## Still open

- ~~**The effect grammar has two gaps real spells exposed** (Alien Hives, Army Forge): a grant
  with a duration — *"gets X once (next time the effect would apply)"* — and multi-target
  selection — *"pick up to two friendly units within 12""*. Neither blocks this design; both
  block authoring spell rows.~~ **Settled 2026-09-04** by
  [spell effects](2026-09-04-spell-effects-design.md): `effect.duration` and a grants store; four
  target columns on the spell entry, so the target Ask returns a set and each target gets the
  effect independently.
- The two flagged `GameRules` interpretations: *one try per spell* scope, and weapon-group
  recomputation after casualties.
- ~~Whether a muted window kind is per player or per team.~~ **Settled 2026-09-04** by
  [the scheduler](2026-09-04-scheduler-design.md): per player.
- Whether `step_completed` payloads need a schema per step kind, or stay free-form within the
  event payload's existing `jsonb`.
