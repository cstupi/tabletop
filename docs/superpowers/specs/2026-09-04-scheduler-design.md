# The scheduler

Design session, 2026-09-04. Whose input is needed next, and what may they do with it.

[Game flow](2026-08-28-game-flow-design.md) named the scheduler, gave it two shapes, and left
it two questions: what is legal in a window, and whether setup configuration can change once
a game is loaded. [Action sequences](2026-09-03-action-sequences-design.md) left it a third:
who goes first when several teams react in one window. This session designs the scheduler and
closes all three.

## Read, not reasoned

- **Grimdark Future v3.5.1**, page 6 (deployment roll-off and alternation), page 7 (rounds,
  turns, activations, and who goes first each round), pages 13–15 (`Ambush`, `Scout`), page 33
  (the optional `Overwatch` action), page 50 (free-for-all and team games).
- **Warhammer 40,000 10th edition core rules** (Wahapedia), as a **falsifier**: battle round
  structure, the Fight phase's alternation, Reinforcements, and the rule for simultaneous
  effects. **Only OPR's `GameRules` gets built.**

The rulebook's structure, page 7: rounds are made of turns, a turn is a single activation, an
activation is one action. Teams alternate until every unit has activated; on each new round
the player that finished activating first goes first. Page 50 makes turn order **team-based**
in team games. Free-for-all players **roll off once, for deployment order**, and every round
after round one is finished-first, as in a two-player game.

## The line

The scheduler is a generic engine component with two implementations, selected by
`game.scheduler_shape`. It knows no ruleset. Everything ruleset-specific is a hook it calls.

| The scheduler owns | `GameRules` supplies |
| --- | --- |
| the order over teams, and whose turn it is | who goes first each round: finished-first at any team count (pages 7, 50); 40k, the same team every round |
| when a turn ends, and when a round ends | which units on a team may activate now — a reading of the action record (OPR: no entry this round; 40k: no entry for this phase) |
| calling the end-of-turn hook after the sequence finishes | what runs there (OPR: the end-of-activation morale test from action sequences) |
| skipping a team with nothing to do | which actions a unit may take (`Immobile` means Hold only; Shaken means idle) |
| when the action record clears, per shape | the phase before round one (deployment), and the round-boundary hooks (spell tokens, Ambushers, clearing round-scoped conditions such as fatigue) |
| ordering reactions in a window | the list of phases inside a turn — empty for OPR |

Two other placements were considered. Putting the scheduler inside `GameRules`, with the
shape as a parameter, is closest to the words in `architecture.md` but makes the two shapes
two branches in one class rather than two implementations behind one interface, which is the
second example game flow chose both shapes to have. Running the scheduler as a sequence on the
resolution stack would make the turn literally an Ask, but the interpreter has no loop
primitive by design, abandoning the game loop means nothing, and a frame that lives for the
whole game is not what the stack was built for. What was kept from that option: **the turn is
an Ask**, issued through the interpreter's own Ask type.

## State

Round number, the order over teams for this round, the team whose turn it is, and which teams
have passed this round. Nothing else. A team is *finished* when it has passed or its legal set
is empty; only the pass is state, and it is a logged fact (`turn_ended` carrying `passed`).

**It is rebuilt from the log and never stored**, as the action record and the resolution
stack are, so a rejoining client and a crashed server recover it by the one hydrate path.
Game flow's "round number, current phase, active team" is this state; *phase* is
`deployment`, `scouts`, `rounds`, or a `GameRules` phase name inside a turn. `scouts` sits
between `deployment` and `rounds`. Under the player-turn shape a `GameRules` phase name replaces
`rounds` for the rest of the round; OPR lists no phases inside a turn, so its phase stays
`rounds`.

## Events

Four kinds, all facts the scheduler emits:

- `round_started` — round number, and the order over teams
- `turn_started` — the team
- `turn_ended` — the team
- `phase_started` — a name

Deployment's order is carried the same way. The roll-off before the first phase is followed by
`round_started` with round `0`, and `phase_started` names the phase. Round one's `round_started`
follows the last phase before rounds and is itself followed by `phase_started("rounds")`. The
scheduler's position has no other field for the deployment order, so hydration finds it in the
log.

**Replay applies them and never re-derives them.** A change to a first-team policy after a
deploy does not rewrite who went first in a game already played — the same reasoning that put
thresholds in roll events. The roll-off that produces an order is an ordinary sequence on the
roll primitive and logs as one.

## Nothing is an invariant

The active team, the round number, a unit's action record — all editable from the menu as
ordinary commands, through the same path the scheduler writes. A menu edit setting the active
team logs a `turn_started`. **The scheduler reads state on every step and never assumes its
last output is still true.** A team's "finished" is its legal set being empty, read again each
time, or its own logged pass this round — never a flag held apart from the log.

## The turn is an Ask

When a turn starts, the scheduler issues an Ask to the team, through the same Ask type the
interpreter uses. The question is *activate*; the legal set is server-computed — every unit
`GameRules` says may activate now, each paired with the actions it may take. Two teams or
free-for-all, the Ask is the same.

The answer names a unit and an action — one of the `action` rows, each of which is a sequence
([action sequences](2026-09-03-action-sequences-design.md) lists the four). The scheduler starts
that sequence on the resolution stack. **When the sequence finishes, the scheduler calls
`GameRules`' end-of-turn hook** — OPR's end-of-activation morale test lives there — **and then
the turn ends** — ruled in this session — and the next team's Ask goes out. The activation is written to the unit's action record when the
sequence starts, not when it ends, so an abandoned sequence still counts as the unit having
gone.

**Moving models by hand is not an answer.** "Anyone can move anything" exists so players can
arrange the models inside a unit; activating is a declared action from a button, and the move
button assists with the distance. Hand moves never end a turn.

**A team with an empty legal set is skipped** — no Ask, no turn events. A participant with no
team at all is never addressed either, and that is what a spectator is since 09-04
([the whole-design review](2026-09-04-whole-design-review.md); presence's "joins a third team" reading is
superseded). It is also how
OPR's "continues until all units have activated" works when one side has more units.

**Passing is a real answer**, logged, and there is one of it. Grimdark Future p.7: "Players must activate one unit that
has not been activated yet", so while a team holds a legal activation it may not pass. The activate Ask is open only
while the legal set is non-empty. Under `strict` it carries `AllowPass: false`, and a pass is outside the set and
refused. Under `warn` the pass stays in the set and is accepted with a warning, `AskAnswered.Breach` naming the rule
(`activation_required`, `IGameRules.AnswerBreach`). Under `silent` it is accepted with no warning. An accepted pass is the
team finishing the round, as the alternating scheduler reads it. A team with no legal activation is not asked at all
(next paragraph). There is no separate end-turn command. The menu's *mark unit activated* writes the
action record, and the scheduler reacts to the record.

**Enforcement.** Answering with a unit or action outside the legal set is what the enforcement
setting governs: `strict` refuses it; `warn` accepts it, logs it with a warning, and the
scheduler proceeds as if it were legal; `silent` accepts it without the warning. (This sentence,
with action sequences', reopened the earlier boolean; the review of 09-04 kept the three.) That is the whole answer to game flow's question about legality
in a window — legality *is* the legal set, and out-of-set is enumerated rather than rejected,
which is how the model holds "a unit that has already moved moving again".

**An answer the ruleset cannot read follows the same dial**, exactly as an out-of-set answer
does: refused under `strict`, and under `warn` or `silent` logged, with a warning under `warn`,
and read as a pass. An option id that does not parse and a unit that does not exist are both
this case. The two are one rule because they are decided in one place, before anything is
written, which is the only point at which a refusal can leave the Ask open. *Ruled by the owner
2026-09-06; the earlier reading, that an unreadable answer is a pass whatever the dial says, is
withdrawn.*

**A superseded scheduler Ask closes with no answers.** A team asked while its turn is open by a
menu edit setting the active team or the round has that Ask closed rather than answered. It is
the one close the engine writes, and it is never an answer.

## The two shapes

**Alternating activation.** One activation per turn. The order over teams comes from the
first-team policy at `round_started`; turns rotate through it, skipping finished teams. A team
finishes when its legal set is empty or it passes. The round ends when every team has finished.
The action record clears at `round_started`. Next round's order is the policy's: OPR two-team,
the team that finished first goes first, at any team count, after one roll-off for deployment
order (p.50).

**Player-turn.** A team holds the turn for every activation until its legal set is empty or it
passes. The turn is a list of phases from `GameRules`; each phase is an Ask loop of *act or end
phase*, with the legal set read per phase. OPR lists no phases, which the player-turn scheduler
runs as one unnamed phase, activating every unit in a row. The team's action record clears at
its `turn_ended`, per game flow. The round ends when every team has had a turn.

**Only OPR's `GameRules` is built, and it runs under both shapes.** Game flow's point stands: a
group may pick alternating activation in a game whose pack defaults to player turns, and the
reverse.

## Deployment

The phase before round one, driven by the same scheduler. `GameRules` supplies it as a phase
whose Ask is *deploy one unit*: the legal set is the team's shelf minus units set aside as
Scouts or Ambushers. The answer names a unit; its models come off the shelf by ordinary
placement commands, and the turn ends when the last model of that unit has a placement. A team
with nothing left to deploy is skipped; the phase ends when every team is skipped.

**The order comes from a roll-off** at the start of the phase — a `GameRules` sequence on the
roll primitive, logged. Its winner deploys first and, per page 7, takes the first turn of round
one.

The roll-off is the roll primitive with no threshold — one die per team, raw results compared,
ties rerolled among the tied — so the primitive admits a null threshold and records dice as
always (added 09-04).

**The roll-off does not pick a table edge, and the deviation from the rulebook is accepted** —
*and, since the same day, reconciled: [the setup phase](2026-09-04-setup-phase-design.md) lets
anyone swap two teams' zones during setup, which is how the winner picks an edge.*
Deployment zones are geometry, assigned to team ordinals during the unlogged setup phase
([setup content](2026-08-29-setup-content-design.md)). The roll-off must be logged, because the
first team of round one depends on it. So the app's order is *zones, then roll-off*, where the
rulebook's is *roll-off, then edge*; a group that wants the rulebook's order rolls off among
themselves before assigning zones. The setup phase session may add a zone swap as a convenience
and is not asked to.

**Scouts** (page 14) deploy after every other unit, alternating, "starting with the player that
activates next" — at that moment, the roll-off winner. They deploy in a **second phase**,
`scouts`, logged as `phase_started("scouts")`, with the same Ask mechanics and the question
`deploy_scouts`. A phase start restarts the rotation from the first team of the order, which is
what makes "starting with the player that activates next" a logged fact rather than a special
case; without the phase boundary the rotation would continue after whichever team deployed last,
contradicting page 14. Set-aside is a condition on the shelved unit, per game flow's rule that
why a model is on the shelf is a condition.

## The round boundary

At `round_started`, after the order is set and before the first turn, the scheduler calls the
`GameRules` hooks in the order they are listed. OPR has three:

- **Clear round-scoped conditions** — fatigue, per action sequences. Added 09-04; nothing had
  cleared it.
- **Spell tokens** — `Caster(X)` grants X, capped at 6, placed here by action sequences.
- **Ambushers** (page 13) — for any round after the first, each team in the new order is Asked
  *deploy an Ambusher or pass*, "starting with the player that activates next", alternating
  until every team passes. Deploying an Ambusher writes nothing to the action record, so the
  unit may activate that round, as the rulebook allows. Ambusher Asks are not turns: no
  `turn_started`/`turn_ended` is written, and a pass there stands for the boundary only, so it
  cannot mark the team as passed for the round.

**The final round is the ruleset's, and its end follows the enforcement setting.** Rounds count up. Grimdark Future's final round is 4 (p.7): when a player ends it, `silent` starts round 5, `warn` asks every team End or Continue (End ends the game; every team's Continue starts round 5 with a warning), and `strict` ends the game ([the enforcement engine](2026-10-02-enforcement-engine-design.md)).

**A round's end comes between the answer and the next round.** Once a player has ended the round, the scheduler asks the ruleset for the round's end changes (`IGameRules.OnRoundEnded`) and writes them as one `round_ended` event when there are any; then the next `round_started` is proposed. Grimdark Future seizes objectives there ([objectives](2026-10-02-objectives-design.md)).

**Rounds count up only when a player says so.** *Ruled by the owner 2026-09-06, to avoid an
accidental auto-advance; the earlier reading, that the scheduler idles and the menu moves the
game on, is withdrawn.* The round does not advance on its own. When no team can take another
turn the scheduler stops at the boundary and opens an Ask: question `end_round`, a confirmation
with no pass, addressed to every team that has a participant. The next round's `round_started`
follows the first answer to it. Any player on any team may answer, because the boundary belongs
to the table rather than to a turn. This is one rule for the ordinary boundary
and the degenerate one, so a wiped or idle table waits at the same Ask instead of counting up.
The Ask sits **before** `round_started`, so the round-boundary hooks above and the first-team
roll-off all still run after it, in the new round. It is one of the scheduler's own Asks, so a
menu edit setting the active team or the round supersedes it and closes it with no answers.

## Windows

Eligibility is the sequence's business: a step names an interrupt point and the engine finds
who can react. **Ordering is the scheduler's.** When more than one team answers *react*, their
reactions resolve **in the current turn order, starting with the team after the actor** — ruled
in this session. Teams that passed drop out. `GameRules` may replace the ordering with a hook,
which is where 40k's *"the player whose turn it is chooses the order"* would go. Page 13 orders
simultaneous rules across players by a roll-off, the winner picking. That case is unreachable
while only the acting team can react, and OPR's `OrderReactions` returns null.

**A window's pass is the player's own**: every window Ask is shown to the player, who answers it or
passes, and the pass is logged with their name.

The one OPR reaction outside casting is the optional **Overwatch** action (page 33): a unit
stays idle and may react once to an enemy activation, at any point as it moves or shoots. It is
a condition set by the action and read at the interrupt points inside move and shoot steps — a
window offered to the non-active team, which the interface already allowed for.

## Re-entering a round

If a menu edit clears a unit's record after its team's legal set emptied, the scheduler sees a
non-empty legal set at that team's slot and Asks it again. A team that **passed** is not re-asked
— its pass is logged and stands for the round — which is the one thing the earlier sentence "the
scheduler holds no finished flag" got wrong, corrected 09-04. A round that has already ended is reopened only by undo,
which is replay.

## What the interface must not assume

Each is a constraint on the generic scheduler, with the 40k rule that falsifies the OPR-shaped
assumption.

1. **The first team each round is a policy.** OPR: whoever finished first, at any team count,
   with round one following the deployment order (p.50). 40k: the same player every round, chosen
   by the mission.
2. **A phase may alternate inside one team's turn.** 40k's Fight phase selects units
   alternately, starting with the active player, inside the active player's turn. A phase
   supplied by `GameRules` must be able to declare its own alternation, and the player-turn
   scheduler must run it. Not built.
3. **Reinforcements arrive on the ruleset's schedule.** OPR Ambushers arrive at the round
   boundary, alternating; 40k Reserves arrive in a step of the active player's own Movement
   phase. Both are hooks; the scheduler owns neither.
4. **The action record is read per ruleset.** Any entry means spent in OPR; 40k checks the
   entry for this phase. The scheduler never reads the record itself.
5. **A window may belong to a non-active team**, and a whole sequence may run inside another
   team's turn. Fire Overwatch, carried by action sequences.
6. **Ordering simultaneous reactions is a policy with a default.** Turn order from the actor,
   replaceable by a hook.

## Setup configuration is fixed once playing

Closing game flow's open item. The rule pack is pinned; the board is the setup snapshot; and
**the scheduler shape and the armies in play are fixed once playing.** A group that wants a
different shape starts a new game.

*Corrected 2026-09-04 by [the setup phase](2026-09-04-setup-phase-design.md), the same day.*
This section first said team membership was fixed and joining closed at the lobby, which
contradicted accounts' settled rule that a game is joinable at every status. Accounts holds:
~~anyone may join an existing team at any time~~ (corrected again below), brings no army after
deployment, and is what presence calls a spectator. The scheduler is unaffected — a late joiner
adds nothing to any legal set.

*Corrected again 2026-09-06, ruled by the owner.* The 09-04 note went one step too far: joining
the **game** is open at every status, but joining a **team** is not. Teams are created and
picked only while the game is `lobby` or `setup`, and at the flip to `playing` the team list
freezes along with the scheduler shape, the armies and the layout. A participant's team is
their **slot**, recorded on their `participant` row, and reconnecting during play returns them
to that slot with no re-pick and nothing re-derived. Someone who first joins after play began
has no slot, so no team, and is exactly the spectator this section already describes. The
canonical statement is [game flow](2026-08-28-game-flow-design.md) → *Teams are frozen at the
flip, and a team is a slot*. The scheduler is unaffected either way.

## Testing

The scheduler is pure logic over game state and a log, with no Godot and no socket in it. It is
the first candidate for the headless test project `CLAUDE.md` defers until there is logic worth
testing: two shapes, one `GameRules`, scripted logs in and events out.

## Deliberately not decided

- How any of this is expressed in C#.
- The setup phase: who assigns deployment zones, who places terrain and when. Its own session.
- 40k's `GameRules`. The core rules were read to falsify the interface, not to build it.
- Action boosting's *Random* playstyle (page 31), which draws a token "whenever it's a player's
  turn to activate" and would hang off `turn_started`. Undesigned with the rest of boosting.

## Still open

Nothing of its own. Closed here, and struck where they live: game flow's *what is legal in a
window* and *whether setup configuration can change once loaded*; action sequences' *who goes
first when several teams react*.
