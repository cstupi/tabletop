# Sound effects

Owner, 2026-10-24: *"We also need to add some basic sound effects for shooting, melee, moving, wounds."*

Owner, 2026-10-25: *"We need to add a few more sounds. Longer shooting, maybe a couple of shooting sounds. Add spell cast sound
different for friendly vs enemy targets on the spell. We need a wound sound when things take wounds. If things all miss it should
make a bullet miss sound. If they hit it should play a sound for the hit. Melee should have sound when a hit lands and again when
wounds land. If all are blocked it should play armor block sound for example."*

Owner, 2026-10-25: *"The shooting sounds should scale with the number of shots. 10 bolters should be rapid fire of the shot that
semi overlap for the 10 shots."* *"Same logic with melee: 10 hits and 10 misses in melee can overlap to give a more immersive
sound. It shouldn't just start them all at the same time but should stagger a bit."* *"5 seconds is a good cap."*

The Godot client plays them. The browser game has no sound (owner: *"The browser game doesn't need sound"*).

## Cues

| Cue | Plays when the log gains |
| --- | --- |
| Shoot | a `hit` roll under `shooting`: a volley, one shot per die; the variant is the weapon's (the attack sequence's first weapon, by its name), so a weapon sounds the same in every game |
| Impact | after that volley, when the roll has at least one success |
| Miss | after that volley, when it has none |
| Melee | a `hit` roll under `melee`: a volley, one strike per die that hits |
| Whiff | the same volley, one swish per die that misses, in die order with the strikes |
| Block | a `defense` roll whose every die blocks and none sets off an extra wound |
| Wound | a `defense` roll with a failed die or an extra wound; a `WoundsSet` whose total rose; a model leaving the table for the shelf |
| CastFriendly | a `casting` frame's `targets` step answered with a piece of the caster's team (a buff) |
| CastEnemy | the same answered with a piece of another team (a hex, a spell's hits) |
| Fizzle | a `casting` roll with no success |
| Move | a unit's move applied (a move step's answer, a hand move, a forced move, a deployment placement) |

A roll is under `shooting` or `melee` when that frame is the innermost open one of `shooting`, `melee`, `casting`, `spell-hits`,
`rule-hits` and `pick-rule`; a spell's or a rule's hits (which cannot miss) play no Impact, and their `defense` roll plays Block or
Wound. A pick-rule's target answer is no cast.

`Core/SoundCues` (Godot-free, unit-tested) reads the events a log append adds, from the records the combat log reads (`CombatLog`:
frame starts and ends, `RollRecord` dice, a step's `SelectedPieces` answer, `WoundsSet`, `ModelsMoved`), and the team of a unit; the
client names no rule. It returns each cue with a delay, a variant, a pitch and a level, and marks a volley's die sounds.

## Sequence

Cues play in log order, each after the one before it by that cue's gap, so a shot reads volley, then the dice landing, then the block
or wound, as the dice tray's 0.4 s tumble shows them:

| After | Gap |
| --- | --- |
| Shoot (a volley's last shot), CastFriendly, CastEnemy | 0.5 s |
| Impact, Miss, Melee, Whiff (a clash's last die) | 0.45 s |
| Block, Wound, Fizzle, Move | 0.4 s |
| a volley's last die, when another volley follows | 0.15 s |

Every cue but a volley's plays at most once between volleys, so each weapon group sounds its own result and a step's many wounds one.
Weapon groups and a strike back sound in the order their rolls arrive, which is the dice tray's. A roll that waits on a player's
answer (a casualty pick) arrives in a later batch and sounds then.

A volley of n dice (`SoundCues`):

| Constant | Value |
| --- | --- |
| `VolleyInterval`, the natural spacing | 95 ms |
| `MaxVolleySeconds`, first die sound to the last one's start (owner) | 5 s |
| `MinVolleyInterval`, the floor | 30 ms |
| `MaxVolleySounds`, the count played at the floor | 167 |
| `VolleyJitter`, each die's shift off its slot (at most a quarter of the spacing; none on the first, never later on the last) | ±12 ms |
| `PitchSpread`, `VolumeSpreadDb` | ±6 %, 0 to -3 dB |

The spacing is 95 ms while the volley fits 5 s (up to 53 dice), then 5 s / (n - 1) down to the floor; past 167 dice the volley plays
167 of them, spread evenly over its dice, 30 ms apart. Jitter, pitch and level come from a hash of the roll's frame id and the die's
index, so the same volley sounds the same every time.

Cues play for events that arrive live and for a replay's forward steps (Play at 1× or slower, Step and Action forward): not for the
log a join or reconnect loads, not for a replay's jump, step back or faster Play, not for an undo, which also drops cues still waiting.

## Sounds

`scripts/Sounds/` synthesises each as a 16-bit mono 44.1 kHz WAV (noise bursts, envelopes, filtered and swept tones, echoes),
deterministic, the way `scripts/TerrainMeshes/` generates meshes, into `client/content/sounds/`. Each is under 1 s and 100 KB, peaks at
-6 dBFS with no clipping, and fades 2 ms in and 12 ms out.

| File | Sound |
| --- | --- |
| `shoot-1` | a single plasma bolt: a crack, a whine falling from 1.4 kHz, a light thump (0.36 s) |
| `shoot-2` | a single bolter round: a crack over a deep thump and a short rumble (0.4 s) |
| `shoot-3` | a single las shot: a short crack and a bright zap falling from 2.6 kHz over a sizzle (0.3 s) |
| `impact` | a round striking home: a low thud and a crunch (0.35 s) |
| `miss` | a ricochet: a tick, then a whistle falling from 3.4 to 1.5 kHz (0.5 s) |
| `melee` | a short metallic clash (0.4 s) |
| `whiff` | a blade through air: band noise swelling for 140 ms (0.4 s) |
| `block` | armour taking a blow: a low, long-ringing clang (0.65 s) |
| `wound` | a dull thud (0.3 s) |
| `cast-friendly` | a rising major arpeggio of bell tones under a shimmer (0.8 s) |
| `cast-enemy` | low tones a tritone apart falling to a throbbing growl over a dark rush (0.8 s) |
| `fizzle` | a falling sigh broken by crackles that die away (0.55 s) |
| `move` | a soft scuff of footsteps (0.3 s) |

Each shot is one short report (one attack, its level down to under 15 % by 0.2 s), so a volley's shots overlap only by their
tails. Tests measure each sound: its peak's time (a shot, impact or clang within 20 ms; a whiff after 80 ms), its tail, its dominant frequency and
spectral centroid and which way the centroid moves (a miss and an enemy cast fall, a friendly cast rises), and its attacks (a shot one,
a fizzle four or more).

## Playing

`SoundPlayer` holds one stream per file and two pools of `AudioStreamPlayer` voices: sixteen for a volley's die sounds and eight for
every other cue, so a long volley never takes an impact's or a wound's voice. Each cue waits its delay, then takes an idle voice of
its pool (else the oldest) at its pitch and level. Settings has **Sound** (on by default) and **Volume** (0–100, default
70), kept in `ClientSettings`, which gate and scale every cue.
