# The whole-design review

Review session, 2026-09-04. Every design document read against every other, the way the data
layer was reviewed on 08-30, now that every module is designed.

Five reviewers each took a slice — `architecture.md` against the designs; the game side; the
preparation side; setup, table and value definitions; `schema.md` and `data-model.md` against
the specs — and reported findings with file and line. Eighty-one came back, about fifty
distinct after the overlaps between slices were merged. Every one was verified against the text
before it was acted on. **All are fixed in place**, in the document that was wrong, with a
dated note where the fix changes a decision rather than a word. This document records what the
review found, what it ruled, and where each fix landed.

## Five rulings

Findings that reversed an earlier decision or added a column went to the user.

1. **Enforcement is three positions, not two.** Identity and enumerations (08-29) ruled a
   boolean — warn or silent, never block — and both action sequences (09-03) and the scheduler
   (09-04) then wrote "at strict enforcement an out-of-set answer is refused" without noticing.
   The review put the contradiction to the user, who **kept the refusing position**.
   `game.enforcement` is `silent | warn | strict`, replacing `warns`. `strict` refuses an
   out-of-set Ask answer or an illegal command. It also ends the game when a state-time breach's consequence is the end ([the enforcement engine](2026-10-02-enforcement-engine-design.md)). **The menu's edits are accepted under every
   setting**, because they are the override path and are logged with an actor. Corrected in
   identity and enumerations, architecture, game flow, action sequences, the scheduler, the
   client shell, schema and data model.
2. **A game has a name.** The client shell's create screen said *name it* and no column held
   one; the game list had nothing to show. `game.name` added.
3. **Waiting is a game setting; animation is a player's.** Resolution said both were "set at
   game setup" and no column held either. Waiting changes what the server does and what the log
   records, so `game.waits_between_steps` is added. Animation is presentation only, so each
   player keeps it in their own client. Resolution and the client shell corrected.
4. **The game server writes a game's own rows before play.** Three documents said it read
   management's schema read-only and wrote only events; the setup phase had it writing the
   `table_state` draft, flipping `game.status`, and taking team and army picks over the socket.
   Ruled: it writes `game.status`, `team`, `participant.team_id` and `army_snapshot`, and the
   game's `table_state` tree while status is `lobby` or `setup`, and from `playing` on writes
   only events and one row, `game.status = 'ended'`, when its own log ends the game. **Layouts stay management's**: the client saves one by `POST /v1/layouts` from its own
   copy of the draft and loads one by `GET /v1/layouts/{id}` plus a replace-draft edit. Lobby
   picks — create a team, pick a team, pick an army, change configuration — are socket commands
   of the setup-edit kind: written, broadcast, never logged. Corrected in architecture, the
   management API, schema, data model and the setup phase.
5. **A participant has no team until they pick one.** Nothing said what team `join` put a
   participant on or who created team rows. `participant.team_id` is nullable; teams are created
   by lobby commands **only while the game is `lobby` or `setup`**; **a participant with no team
   is never addressed by an Ask, and that is what a spectator is** — superseding presence's
   "joins a third team". Zones re-derive from the zone style whenever the team list changes
   during setup. Corrected in presence, the scheduler, accounts, the setup phase, schema and
   data model.

   *Corrected 2026-09-06, ruled by the owner.* This ruling first read "teams are created by
   lobby commands at any status", and closed with ~~a team created after `playing` has no zone
   and nothing to deploy~~. Both are struck: they contradicted ruling 4 above, since a server
   that writes only events and the end from `playing` on cannot write a `team` row then, and no team is created
   after `playing` for the zone case to arise. **The team list is frozen at the flip to
   `playing`**, and a returning player **rejoins the slot recorded on their own `participant`
   row** — same team, no re-pick, not a spectator. Ruling 4 and this ruling now agree: both stop
   team writes at the flip. The model is stated in full in
   [game flow](2026-08-28-game-flow-design.md), *Teams are frozen at the flip, and a team is a
   slot*, and the other documents point there.

## Contradictions fixed

- **Login.** The client shell's no-social-login ruling had reached the web app but not
  everywhere: accounts still supported "all of Stytch's methods" and said the browser owns
  login; the management API still listed the magic-link and OAuth endpoints and said the client
  acquires a session from the browser; architecture said it in three places and gave Identity
  "sessions" to own; the web app kept a webview "to catch an auth redirect". All corrected.
- **Spell hits and the hit roll.** Action sequences said spells skip the hit roll; spell effects
  rolls one die per hit that cannot fail. The earlier line now says so.
- **Casting's target and its grant.** Action sequences had "Ask the target" and "grants write a
  condition"; spell effects made the target a set and the grant a store. Annotated.
- **The scheduler's pass.** Its state held "which teams have finished", its text said finished
  is not a flag but an empty legal set, and a team that passed with a non-empty legal set would
  have been re-asked at once. Ruled: the pass is a logged fact (`turn_ended` carrying `passed`);
  finished is passed-this-round or empty legal set; only teams that did not pass re-enter.
- **Tokens had no home in the log.** Setup content said a token is "dropped during play and
  moved freely" and the marker table lives only on the snapshot. Ruled: objectives are snapshot
  rows; a token dropped in play is an ordinary logged command, rebuilt from the log.
- **Containment meant two things.** The table model's "computed, never stored" is standing in
  terrain; game flow's is being inside a transport, a stored location. Annotated.
- **Mesh prep tool.** The asset library and geometry values still credited an offline tool.
  Corrected.
- **Configuration "before the game loads".** Game flow said so; clients connect at `lobby` and
  the lobby is where it is set. Table dimensions struck from the list; the "ordinary commands"
  line about setup struck, since setup edits are not commands.
- **Architecture's examples.** `EndActivation` is not a command (the turn ends when the sequence
  finishes); `DiceRolled` is not an event (a roll is a payload in `step_completed`); "every roll
  is an event" likewise. Corrected.
- **Architecture's rules-as-data section** still argued for JSON files validated by JSON Schema
  in tests; the data half has been Postgres rows since 08-30. Corrected, and the one untouched
  part named: how authored content reaches the tables.
- **Smaller.** `target_roll` was still listed as an enumeration after becoming a foreign key;
  "two" foreign-key exceptions on `EFFECT` are four; "the four contracts" are three; the
  filtered-streams paragraph said nothing uses them while the simultaneous token spend does
  (renamed 2026-09-06 from "sealed bid"; the note is in
  [action sequences](2026-09-03-action-sequences-design.md)); "served through
  the management API" for asset bytes that go by presigned URL; the miniature proxy marked
  "still open" after being settled 08-30; "Linux (Fargate)" and "Postgres on RDS" pre-empting the
  deferred hosting session; "exercised from step 6 onward" for networking that step 4 builds;
  local development without the Docker the hosting ruling introduced; "open games" in accounts;
  "conversion is the offline prep tool" in architecture's web bullet; a spell grant's subject
  written as "the hits themselves" when the closed set is `self | opponent`.

## Gaps filled

Things two documents together implied that neither stated.

- **The four OPR actions are sequences.** The scheduler starts "that action's sequence" and
  action sequences listed Shooting, Charge, Casting, morale and dangerous terrain but no Hold,
  Advance or Rush. Now: Hold is an Ask *shoot or not* pushing Shooting; Advance is a move of the
  action's distance then the same Ask; Rush is a move and nothing else; Charge as written.
- **Nothing cleared fatigue.** A third OPR round-boundary hook, clear round-scoped conditions.
- **The end-of-activation morale hook had no caller.** The scheduler calls `GameRules`'
  end-of-turn hook after the sequence finishes and before `turn_ended`.
- **Wounds-this-activation had no store that outlived the frame.** A per-unit counter in game
  state, like spell tokens, cleared at `turn_ended`.
- **`GameRules` name checks read the grants store**, or a granted hook-name rule would never
  fire.
- **The roll-off has no threshold.** The roll primitive admits a null threshold; raw results
  compared, ties rerolled.
- **Who computes the bounding-box volume "at upload".** The browser, from the mesh it holds,
  written with `PUT /v1/assets/{id}/volumes` at confirm.
- **Off-slab terrain at the flip.** The end-setup command deletes those rows before flipping.
- **Built-in placements' volumes are the volumes**, not overrides; always written on add.
- **A lobby connect receives** teams, participants and configuration, and no board.
- **Home disconnects; Leave is a menu action** calling `/leave`, since disconnecting is not
  leaving.
- **`GET /v1/properties`** lists the pinned sets a game may pick; `POST /v1/games` takes name,
  hash, shape, enforcement and the wait setting.
- **The version check** is a request header answered by `426 Upgrade Required`.
- **The catalog's per-kind check** stated in one place; the built-in-or-asset check on
  `table_state` and `marker` stated where the terrain section leaned on it; kind 6 takes an
  `amount`; the army snapshot is written when setup ends and is nullable.
- **Presence's piece id is any placeable**, and setup's selection may be terrain or a marker;
  scale applies on release.

## Left as first-pass assertions

Named by the architecture reviewer and deliberately not touched, since no design doc has:
how authored `Properties` content reaches the tables (the content import); the four table-size
presets, now marked as a suggestion; the internal army format as authoritative for a future
native list builder; "four players at 20Hz" for a presence sample rate presence left open;
Blazor WASM or Godot web export as a future browser client; UUIDv7, justified only there; "the
first playable seats two" and the build order as a whole, which the implementation plans will
replace.

## Still open

Nothing of its own. The union of every design doc's *Still open* is in `handoff.md`.
