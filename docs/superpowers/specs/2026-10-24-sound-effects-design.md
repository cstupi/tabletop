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
| Shoot | a `hit` roll under `shooting`: a volley, one shot per die; the variant is the weapon group of the attack sequence's first weapon, read off the words of its name (below), so a weapon sounds the same in every game |
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

`scripts/Sounds/` synthesises each as a 16-bit mono 44.1 kHz WAV, deterministic (seeded noise), the way `scripts/TerrainMeshes/`
generates meshes, into `client/content/sounds/`. No recorded samples. Each sound is layered: a transient (the first milliseconds' crack
or knock), a body (a saturated blast band, an FM zap, a crunch, a clang), weight (a falling low thump, saturated so its harmonics carry
on small speakers), then a slap echo and a room (a Freeverb: eight damped combs, four all-passes) for the tail. Each is under 1 s and
100 KB. The master stage removes DC and rumble under 25 Hz, sets the loudest 400 ms window (ITU-R BS.1770 K-weighted, "momentary
max") to the cue's target through a soft limiter whose ceiling is -1.2 dBFS, then fades 2 ms in and 15 ms out. The targets rank the
cues: a heavy gun loudest, footsteps quietest.

| File | Target LUFS | Sound |
| --- | --- | --- |
| `shoot-1` | -15 | plasma: a crack, an FM discharge falling from 900 to 110 Hz, a sizzle swept down from 7 kHz with crackle, a thump, a room (0.6 s) |
| `shoot-2` | -15 | ballistic: a crack, a saturated mid blast, a thump, the round's hiss, a slap echo, a room (0.6 s) |
| `shoot-3` | -16 | las: a snap, an FM zap falling from 3.2 kHz to 800 Hz in 80 ms, a short ring, a room (0.5 s) |
| `shoot-4` | -13 | heavy: a crack, a hard-saturated blast, a sub boom falling from 75 to 30 Hz, a rumble, debris, a long slap, a large room (0.95 s) |
| `shoot-5` | -16 | flame: an ignition whump, a roaring swept band of noise with crackle (0.7 s) |
| `impact` | -14 | a round striking home: a knock, a saturated low punch, a crunch, debris (0.5 s) |
| `miss` | -18 | a ricochet: a tick and a spit of dirt, a whistle falling from 3.4 to 1.4 kHz with a flutter (0.6 s) |
| `melee` | -15 | a blow landing: a knock, a thump, a crunch, a short clash of metal (0.5 s) |
| `whiff` | -19 | a blade through air: a resonant band swept up to 2.4 kHz and back, swelling for 120 ms (0.45 s) |
| `block` | -15 | armour taking a blow: a knock, a thump, a long clang of inharmonic partials, a room (0.8 s) |
| `wound` | -14 | a heavy body hit falling to 40 Hz, a wet crunch of grains, a crack of armour (0.5 s) |
| `cast-friendly` | -17 | a rising arpeggio of detuned bells over an upper chord pad and a shimmer, a large room (0.95 s) |
| `cast-enemy` | -16 | FM tones a tritone apart falling and darkening to a throbbing growl, a dark rush, a large room (0.95 s) |
| `fizzle` | -19 | a falling FM sigh broken by electric sputters that die away (0.6 s) |
| `move` | -21 | a squad on the move: five boot falls on gravel, a clink of kit (0.75 s) |

Weapon groups (`SoundCues.VariantOf`): the first group with a word of the weapon's name starting with one of its words, in this order;
else ballistic.

| Group | Words |
| --- | --- |
| plasma (`shoot-1`) | plasma, fusion, melta, disintegrat, psy, warp, flux, graviton |
| las (`shoot-3`) | las, laser, beam, ray, lance, gauss, ion, particle, shock, lightning, tesla, arc, volt |
| flame (`shoot-5`) | flame, fire, burn, incinerat, inferno, napalm, torch, blaze, pyre, scorch |
| heavy (`shoot-4`) | cannon, launcher, missile, rocket, mortar, artillery, howitzer, grenade, bomb, siege, demolish, quake |
| ballistic (`shoot-2`) | every other weapon (Rifle, Pistol, Autocannon, Spore Gun …) |

A shot (other than the heavy gun's) cracks within 30 ms and is down to its room tail by 0.25 s (under 15 % of its first 50 ms), so a
volley's shots overlap only by their tails. Tests (`tests/Tabletop.Sounds.Tests`) measure each sound: format, loudness within 0.5 LU of
its target and peak at or under -1 dBFS, DC under 0.001, the fades; its peak's time, its tail, its share of energy at 250 Hz–6 kHz (a
body every speaker plays) and under 200 Hz (weight), its dominant frequency and spectral centroid and which way the centroid moves (a
miss and an enemy cast fall, a friendly cast rises), and its attacks (a fizzle and a move four or more). `./scripts/sounds measure`
prints each file's measures.

## Playing

`SoundPlayer` holds one stream per file and two pools of `AudioStreamPlayer` voices: sixteen for a volley's die sounds and eight for
every other cue, so a long volley never takes an impact's or a wound's voice. Each cue waits its delay, then takes an idle voice of
its pool (else the oldest) at its pitch and level. Settings has **Sound** (on by default) and **Volume** (0–100, default
70), kept in `ClientSettings`, which gate and scale every cue.
`SoundPlayer.Cued` raises every live batch's cues, Sound on or off, for the table's effects.

## On the table

The table draws the same cues on the same clock (`Core/BattleEffects`, `Table/BattleFx`), while Settings' **Animate** is on:

| Cue | Effect |
| --- | --- |
| Shoot, per die | the frame's shots round robin: a muzzle flash in the weapon group's colour and a recoil on the shooter, a tracer flying at the group's speed (las 600"/s, ballistic 140, plasma 80, heavy 70, flame 40, at most 0.35 s; a flamer's is rolling puffs of fire), then a strike (flash and sparks) on the target model for a hit, or a puff of dust 0.7–1.8" beyond it, within 35° of the line of fire, for a miss |
| Melee, Whiff, per die | the attacking unit's next model jabs 0.4" at the nearest target model; a Melee strikes it when the jab reaches |
| Block | a pale flash and sparks off a model of the target unit |
| Wound | each wounded model shakes for 0.32 s under a red flash; each removed model, kept standing until then, topples 85° to a side of its own, sinks and fades over 1.1 s |

Every shot also draws a faint aim beam in the shooter's team colour from its chest to the target point, held until its volley's last
shot, Animate on or off.
