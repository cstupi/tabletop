# Sound effects

Owner, 2026-10-24: *"We also need to add some basic sound effects for shooting, melee, moving, wounds."*

Owner, 2026-10-25: *"We need to add a few more sounds. Longer shooting, maybe a couple of shooting sounds. Add spell cast sound
different for friendly vs enemy targets on the spell. We need a wound sound when things take wounds. If things all miss it should
make a bullet miss sound. If they hit it should play a sound for the hit. Melee should have sound when a hit lands and again when
wounds land. If all are blocked it should play armor block sound for example."*

The Godot client plays them. The browser game has no sound (owner: *"The browser game doesn't need sound"*).

## Cues

| Cue | Plays when the log gains |
| --- | --- |
| Shoot | an `attack-sequence` starting under an open `shooting` frame: once per shooting frame, not per weapon or die; one of three variants, picked by the shooting frame's id |
| Impact | a `hit` roll under `shooting` with at least one success |
| Miss | a `hit` roll under `shooting` with no success |
| Melee | a `hit` roll under `melee` with at least one success: the strike lands |
| Whiff | a `hit` roll under `melee` with no success |
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
client names no rule. It returns each cue with a delay and a variant.

## Sequence

A batch plays each cue at most once, in order of first appearance; each starts after the one before it by that cue's gap, so a shot
reads fire, then the dice landing, then the block or wound, as the dice tray's 0.4 s tumble shows them:

| After | Gap |
| --- | --- |
| Shoot, CastFriendly, CastEnemy | 0.5 s |
| Impact, Miss, Melee, Whiff | 0.45 s |
| Block, Wound, Fizzle, Move | 0.4 s |

A roll that waits on a player's answer (a casualty pick) arrives in a later batch and sounds then. Cues play only for events that
arrive live: not for the log a join or reconnect loads, not for a replay seek, not for an undo, which also drops cues still waiting.

## Sounds

`scripts/Sounds/` synthesises each as a 16-bit mono 44.1 kHz WAV (noise bursts, envelopes, filtered and swept tones, echoes),
deterministic, the way `scripts/TerrainMeshes/` generates meshes, into `client/content/sounds/`. Each is under 1 s and 100 KB, peaks at
-6 dBFS with no clipping, and fades 2 ms in and 12 ms out.

| File | Sound |
| --- | --- |
| `shoot-1` | a plasma bolt: a crack, a whine falling from 1.4 kHz, a light thump, slap-back echo (0.7 s) |
| `shoot-2` | a heavy rifle: a crack over a deep thump and a rumbling, echoing tail (0.75 s) |
| `shoot-3` | a three-round burst, 85 ms apart, over a rumble (0.65 s) |
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

Tests measure each: its peak's time (a shot, impact or clang within 20 ms; a whiff after 80 ms), its tail, its dominant frequency and
spectral centroid and which way the centroid moves (a miss and an enemy cast fall, a friendly cast rises), and its attacks (a burst
three, a fizzle four or more).

## Playing

`SoundPlayer` holds a pool of eight `AudioStreamPlayer` voices and one stream per file; each cue waits its delay, then takes an idle
voice (else the oldest), so one sound never cuts another off. Settings has **Sound** (on by default) and **Volume** (0–100, default
70), kept in `ClientSettings`, which gate and scale every cue.
