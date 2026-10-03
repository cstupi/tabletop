# Task 9: The documents

**Files:**
- Modify: `CLAUDE.md`, `docs/architecture.md`, `docs/data-model.md`, `docs/schema.md`, `docs/design/README.md`, `docs/design/contracts/design.md`, `docs/design/game-server/design.md`, `docs/design/management-api/design.md`, `docs/design/scheduler/design.md`, `docs/design/simulation/design.md`, `docs/superpowers/plans/2026-09-05-management-api/README.md`, `docs/superpowers/plans/2026-09-05-game-server-skeleton.md`, `docs/superpowers/specs/2026-09-04-scheduler-design.md`, `docs/superpowers/specs/2026-08-29-accounts-design.md`, `docs/superpowers/specs/2026-08-28-game-flow-design.md`, `docs/superpowers/specs/2026-09-04-whole-design-review.md`, `docs/superpowers/specs/2026-08-29-identity-and-enumerations-design.md`, `docs/superpowers/specs/2026-09-06-simulation-design.md`, `docs/handoff.md`

**Interfaces:**
- Consumes: the behaviour Tasks 1 to 8 built.
- Produces: no code. Every document states the current decision once, with no change history (*"Corrected on…"*, *"was …"*): the sentence is rewritten, not annotated.

Spec: enforcement spec → *Statements this reverses*; *The wire* (the `ProtocolVersion` assumption). The voice is the specs': one fact per sentence, no em-dashes in new text.

- [ ] **Step 1: Find every place the old facts are stated**

Run:

```bash
grep -rn -E "lobby. or .setup|read-only from .playing|no round limit|Nothing in the rules ends a game|writes only events|Ending is a flag|Decided|\"decided\"|ProtocolVersion" CLAUDE.md docs --include=*.md | grep -v "2026-10-02"
```

Expected: the passages below, and no others that state the old fact. A hit this task does not list is corrected the same way and named in the commit body.

- [ ] **Step 2: Rows by writer**

The game server now writes `game.status` once from `playing` on. Rewrite each statement so it says so:

- `CLAUDE.md` → *Conventions*: "The game server writes a game's rows only while it is in `lobby` or `setup`, and `game.status` once more, `playing → ended`, when its own log ends the game; management writes them at creation and at end/leave/delete (management plan → Rows by writer)."
- `docs/design/management-api/design.md` → *Rows by writer*: the third column's header becomes "Game server writes (while `lobby` or `setup`, and the end)"; the `game.status` row's game-server cell becomes `lobby → setup → playing`; `playing → ended` when its own log ends the game (guarded on `playing`)". In the paragraph after the table, add: "The game server's `'ended'` is guarded on `status = 'playing'`, so it never overwrites management's."
- `docs/superpowers/plans/2026-09-05-management-api/README.md` → *Rows by writer*: the same two cell changes.
- `docs/superpowers/plans/2026-09-05-game-server-skeleton.md`, *Interfaces needed* item 1: "Management never writes `status` to `setup` or `playing`" stands; add "and this server writes `ended` only from `playing`, when its own log ends the game (enforcement plan)".
- `docs/architecture.md` → *Game management data model*: "and is read-only from `playing` on" becomes "and from `playing` on writes only events and one row: `game.status = 'ended'` when its own log ends the game".
- `docs/data-model.md`, the *Two schemas* paragraph: the same rewrite of "is read-only from `playing` on".
- `docs/schema.md`, the settled game-metadata bullet: after "writes the game's own rows while its status is `lobby` or `setup`" add "and `game.status = 'ended'` from `playing` when its own log ends the game".
- `docs/design/README.md` → *7. Row ownership moves with game status*: "From `playing` on it writes only events." becomes "From `playing` on it writes events and one row: `ended`, when its own log ends the game." The deployment paragraph's "writes a game's own rows while it is in `lobby` or `setup`" gains the same clause.
- `docs/design/contracts/design.md`, the two-schemas bullet and the row-ownership table row for `game`: add "and `status = 'ended'` from `playing`".
- `docs/design/game-server/design.md` → *Row reads and writes*: the `From playing on` column gains "`game.status = 'ended'`, once, when a `game_ended` it appended becomes live (`IGameRows.EndGameAsync`)". → the enforcement table near `EnforcementPolicy.Decide`: add the breach form beneath it (the three-row table from the enforcement spec → *The policy*) and one sentence: "`RulesEngine.Advance` judges its own proposal through `IGameRules.JudgeAdvance`; under strict a breach there ends the game."

- [ ] **Step 3: Rules enforcement and transport in `docs/architecture.md`**

- *Rules enforcement*: after the first paragraph add: "**One policy behind the three positions.** Any system hands it a breach: what was broken, the sentence players read, and what `strict` does about it. The dispatcher hands it a command's legality; the engine hands it what Grimdark Future's judge finds in the engine's own proposal. Under `warn` the warning rides on the event; under `strict` a command breach is refused and a state-time breach ends the game. The first state-time breach is the end of round 4 (GF p.7): `silent` lets round 5 start, `warn` marks it, `strict` logs `game_ended` and the game server marks the row ended ([the enforcement engine](superpowers/specs/2026-10-02-enforcement-engine-design.md))."
- *Transport*: add a paragraph: "**`ProtocolVersion` is 1 and moves only for an installed client.** Additive wire kinds (`at_rest`, `game_ended`) do not move it, because every client is built from the same commit as the servers. That holds before release; the first release moves the version on the next addition an older client cannot read."

- [ ] **Step 4: The specs that said nothing ends a game**

- `docs/superpowers/specs/2026-09-04-scheduler-design.md`, *There is no round limit*: replace the two sentences ("There is no round limit. Rounds count up. Missions say four; players read their book, and ending is a flag they set.") with "**The final round is the ruleset's, and its end follows the enforcement setting.** Rounds count up. Grimdark Future's final round is 4 (p.7): when a player ends it, `silent` starts round 5, `warn` starts it with a warning, and `strict` ends the game ([the enforcement engine](2026-10-02-enforcement-engine-design.md))." In `docs/design/scheduler/design.md`, the matching sentence near "There is no round limit both state the rule" and the deferred-table row "A round limit or mission round count" are rewritten to the same fact, and the deferred row is removed.
- `docs/superpowers/specs/2026-08-29-accounts-design.md`, *Ending is a flag, not a state*: "Nothing in the rules ends a game — there are no victory conditions, because players count their own points." becomes "A game ends when someone ends it explicitly, when the last participant leaves, or, under `strict`, when its ruleset's final round has been played (the game server marks it ended; [the enforcement engine](2026-10-02-enforcement-engine-design.md))." and the following sentence's list of two ways becomes the same three.
- `docs/superpowers/specs/2026-08-28-game-flow-design.md`, *Nothing is an invariant*: add "A `strict` end is no exception: undo takes it back and the menu's set round plays on." (*Scoring* is the objectives plan's to rewrite.)
- `docs/superpowers/specs/2026-09-04-whole-design-review.md`, ruling 1: after "`strict` refuses an out-of-set Ask answer or an illegal command." add "It also ends the game when a state-time breach's consequence is the end ([the enforcement engine](2026-10-02-enforcement-engine-design.md))."
- `docs/superpowers/specs/2026-08-29-identity-and-enumerations-design.md`, the `GAME.enforcement` paragraph: the same added sentence.
- `docs/superpowers/specs/2026-09-06-simulation-design.md`: the *Round limit* row's "since OPR has no round limit" becomes "since a run under `warn` or `silent` plays past the final round and a stalemate must still terminate"; the settled round-cap bullet's "OPR has no round limit and `GameRules` must not invent one" becomes "the cap is not the ruleset's final round, which the enforcement engine reads"; the *What a full game means* table's `decided` row becomes `ended` ("the game logged `game_ended`; a wipe-out is not a win, p.6"). `docs/design/simulation/design.md` → the round-cap row and the outcome list ("five ends") likewise.

- [ ] **Step 5: The handoff**

In `docs/handoff.md`: the *Settled by the owner on 2026-10-02* bullet on the round-4 end and *Next* item 2 become one line: the enforcement engine and objectives are built by this tree (link the two plans). Remove "`SubmitResult.Accepted.Warning` is never populated". The recording-size sentence about "an eight-round game" becomes "a game played to the end of round 4 under strict". Add to the state paragraph the new test counts from the whole-tree check.

- [ ] **Step 6: Check, then commit**

Run: `grep -rn -E "read-only from .playing|no round limit|Nothing in the rules ends a game" CLAUDE.md docs --include=*.md | grep -v 2026-10-02`
Expected: no output.

```bash
git add CLAUDE.md docs
git commit -m "State the enforcement engine and the game server's one write from playing on in every document"
```
