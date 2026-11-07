# Sound and animation

Owner's handoff item: *"The sound effects still don't sound right, and the animations still don't look good enough for an immersive war
feel."* Spec `docs/superpowers/specs/2026-10-24-sound-effects-design.md` (cues, sequence, sounds); `client/AGENTS.md` (Sound,
Animation).

## Diagnosis

### Sounds (`./scripts/sounds measure`, the shipped files before this plan)

| File | s | LUFS (momentary max) | Peak at | -40 dB at | Centroid | < 150 Hz |
| --- | --- | --- | --- | --- | --- | --- |
| shoot-1 plasma | 0.36 | -22.0 | 8 ms | 274 ms | 726 Hz | 5.5 % |
| shoot-2 bolter | 0.40 | -21.9 | 6 ms | 339 ms | 89 Hz | 99.6 % |
| shoot-3 las | 0.30 | -19.4 | 4 ms | 210 ms | 1624 Hz | 0 % |
| impact | 0.35 | -22.7 | 3 ms | 210 ms | 178 Hz | 24.4 % |
| miss | 0.50 | -13.6 | 38 ms | 494 ms | 2522 Hz | 0 % |
| melee | 0.40 | -23.6 | 2 ms | 369 ms | 685 Hz | 0 % |
| whiff | 0.40 | -19.2 | 101 ms | 399 ms | 2216 Hz | 0 % |
| block | 0.65 | -19.7 | 12 ms | 644 ms | 443 Hz | 0 % |
| wound | 0.30 | -19.9 | 6 ms | 294 ms | 89 Hz | 100 % |
| cast-friendly | 0.80 | -16.8 | 223 ms | 798 ms | 1169 Hz | 0 % |
| cast-enemy | 0.80 | -18.6 | 86 ms | 798 ms | 255 Hz | 16.7 % |
| fizzle | 0.55 | -23.4 | 5 ms | 379 ms | 3065 Hz | 0 % |
| move | 0.30 | -25.2 | 11 ms | 254 ms | 1287 Hz | 0.8 % |

Why they sound wrong:

- **Levels are inverted.** Every file peaks at -6 dBFS, so loudness follows crest factor, not importance: a miss's whistle
  (-13.6 LUFS) is 8–10 LU louder than a shot, an impact or a melee hit (-22 to -23.6); footsteps sit 11.6 LU under the miss.
- **The bolter and the wound are sine "boops".** 99.6 % and 100 % of their energy is under 150 Hz, a pure falling sine. A laptop or
  headset speaker plays almost none of it, and what is left has no blast or crunch.
- **No body.** Noise layers go through two one-pole stages (6 dB/octave each), so a "crack" is soft and about 10 dB under the sines
  beside it. Shots have a 2 ms transient and nothing after it but tones.
- **Synth tones, not weapons.** The plasma, las, melee clash and block are plain sine partials with exponential decays: a beep, a
  bell.
- **Dry, no space.** Every sound ends dead at its -40 dB point (210–340 ms for the shots), with no echo or room: a war on a table
  in a void.
- **One ballistic sound for every gun.** Variants are a hash of the weapon's name over three files, so a cannon, a flamer and a
  pistol can share the bolter's report.

### Animation (`client/src/Table/Animator.cs`, `Lasers.cs`, `FloatingText.cs`, `TableView.cs`)

- **Linear glides.** `Animator` tweens `position` in 0.4 s with Godot's default linear transition, whatever the distance: a 1"
  shuffle and a 12" rush take the same time, start and stop at full speed, and never turn (yaw snaps).
- **A charge looks like any move.** No anticipation, no acceleration, no impact on contact.
- **Shooting is one static beam.** `Lasers` draws, per shooting model, a beam and three bolts for 1 s at the moment the batch
  arrives, while the sound's volley plays one shot per die over up to 5 s afterwards: the visuals and sound do not line up, and a
  20-shot volley is the same three bolts. No muzzle flash, no recoil, no hit or miss at the target.
- **No hit feedback.** A wound shows only the floating text; the model does not react.
- **Removal is a pop.** `TableView.Sync` frees a model's node the moment the batch lands; the model vanishes before its wound
  sound plays.
- **Melee has no motion.** Strikes and whiffs are sound only.

## Sound plan (`scripts/Sounds/`)

Generated procedurally in C#, deterministic, no samples (no licensing to track).

- `Synth`: RBJ-style resonant filters and a time-varying state-variable filter (swept bands for whooshes, discharges, sizzles);
  FM tones; brown noise; tanh saturation (carries a low thump's harmonics onto small speakers, adds grit); grains (crackle, debris,
  gravel); a slap echo; a Freeverb room (eight damped combs, four all-passes); noise bands RMS-normalised so layer gains compare.
- `Synth.Master(x, lufs)`: DC/rumble high pass at 25 Hz, gain searched so the loudest 400 ms window (BS.1770 K-weighted) hits the
  cue's target, through a soft limiter (knee 4.4 dB under a -1.2 dBFS ceiling), then a 2 ms fade in and 15 ms fade out.
- `Measure`: momentary-max LUFS, peak, DC, peak time, -40 dB tail, centroid, share under 150 Hz, flatness, crest;
  `./scripts/sounds measure [dir]` prints them.
- Each cue is layered: transient (crack, knock), body (blast band, FM zap, crunch, clang), weight (saturated falling thump), then echo
  and room.

| Cue | Target LUFS | Character |
| --- | --- | --- |
| shoot-1 plasma | -15 | crack, FM discharge 900 → 110 Hz, sizzle swept from 7 kHz with crackle, thump, room |
| shoot-2 ballistic | -15 | crack, saturated mid blast, thump, round's hiss, slap echo, room |
| shoot-3 las | -16 | snap, FM zap 3.2 kHz → 800 Hz, short ring, room |
| shoot-4 heavy (new) | -13 | crack, hard-saturated blast, sub boom 75 → 30 Hz, rumble, debris, long slap, large room |
| shoot-5 flame (new) | -16 | ignition whump, roaring swept noise band with crackle |
| impact | -14 | knock, saturated low punch, crunch, debris |
| miss | -18 | tick and dirt spit, falling ricochet whistle |
| melee | -15 | knock, thump, crunch, short metal clash |
| whiff | -19 | resonant band swept up and back, swelling |
| block | -15 | knock, thump, long inharmonic clang, room |
| wound | -14 | heavy body hit, wet crunch grains, armour crack |
| cast-friendly | -17 | rising detuned bell arpeggio, upper chord pad, shimmer, large room |
| cast-enemy | -16 | tritone FM growl falling and darkening, dark rush, large room |
| fizzle | -19 | falling FM sigh, saturated electric sputters |
| move | -21 | five boot falls on gravel, kit clink |

Weapon groups (`SoundCues.VariantOf`, Core): the first matching word of the weapon's name, case-insensitive; else ballistic.

| Variant | Words |
| --- | --- |
| plasma (shoot-1) | plasma, fusion, melta, disintegrat, psy, warp, flux, dark matter, graviton |
| las (shoot-3) | las, laser, beam, ray, lance, gauss, ion, particle, shock, lightning, tesla, arc, volt |
| flame (shoot-5) | flame, fire, burn, incinerat, inferno, napalm, torch, blaze, pyre, scorch |
| heavy (shoot-4) | cannon, launcher, missile, rocket, mortar, artillery, howitzer, grenade, bomb, siege, demolish, battle, quake |
| ballistic (shoot-2) | anything else (rifle, gun, pistol, bolter, shotgun, sniper, spike, spore …) |

## Animation plan (client)

Core (Godot-free, unit-tested):

- `GlidePlan` (`Core/GlidePlan.cs`): the frames a batch opens and closes (seeded from the projection), and for each relocation a
  `Glide`: Walk or Charge (a move in a `charge` frame of the frame's actor's own models), its seconds (walk 0.35–0.9 s by
  distance, charge 0.3–0.6 s), and `GlideCurve.At(kind, u)`: along-path fraction and lift. Walk: ease-in-out (smoothstep) with a
  low step bob. Charge: a pull back (anticipation, 0 → -0.06 over the first 18 %), then accelerating (cubic ease-in) into contact,
  a small overshoot and settle.
- `BattleEffects` (`Core/BattleEffects.cs`): from a batch's cues (`SoundCues`) and shots (`ShotLines`, with the attack frame) the
  timed effects, on the sound's clock: per volley die a `Muzzle` (the shooting model, round robin over the frame's shots) and a
  tracer that lands after its flight time (by weapon group's speed, at most 0.3 s) as a `Strike` (die hit) or a `Puff` (miss,
  scattered around the target point); per melee die a `Jab` (an attacker toward the nearest target model) with a `Strike` on a hit;
  at a Wound cue a `Shake` on each wounded model and a `Fall` for each removed one; at a Block cue a `Deflect` spark on the
  target unit.
- `SoundCues` carries what the effects need on each `CueAt`: the attack frame, the die's hit, the actor and target units, the
  wounded and removed models (merged into the segment's one Wound cue).

Godot:

- `Animator`: glides by `TweenMethod` over `GlideCurve`, yaw turned along the shortest arc; the fallback timer follows the longest
  glide.
- `BattleFx` (`Table/BattleFx.cs`, replaces `Lasers`'s bolts): one node with its own clock and `_Process`; pooled, shared meshes and
  one material per team colour (fade by `GeometryInstance3D.Transparency`); muzzle flash (emissive billboard quads, 70 ms),
  recoil (the shooter's mesh pushed back 0.12" and back over 0.15 s), tracer (a stretched emissive capsule along the line),
  strike (flash, 6 sparks flying out on ballistic arcs over 0.35 s, no physics), puff (a grey dust disc rising and fading),
  shake (decaying 0.25 s jitter of ±0.08" and a red overlay pulse), fall (the removed node, kept by `TableView.Removing`,
  stands until its Wound cue, then topples 85° away from the hit over 0.45 s, sinks and fades by 1.1 s), jab (0.35" toward the
  target and back over 0.22 s). `Lasers` keeps the faint aim beam, held for the volley.
- `Settings.Animate` off: no glides (as now) and no effects; the aim beam stays.
- Shader warm-up: the effect meshes and materials are drawn in `WarmupScene`, so the first volley compiles nothing.

## Validation

- Generator: `tests/Tabletop.Sounds.Tests`: 16-bit mono 44.1 kHz, under 1 s and 100 KB; momentary-max loudness within ±0.5 LU of
  each cue's target and peak ≤ -1 dBFS; |DC| < 1e-3; first and last sample zero, first millisecond under half scale, last under a
  tenth; character tests (shot peaks within 30 ms and down to its room tail by 0.25 s, a body of ≥25 % energy at 250 Hz–6 kHz for
  every report and hit, weight under 200 Hz for ballistic, heavy, impact, wound; plasma falls, las brightest, heavy lowest and
  longest; flamer roars; miss falls; whiff swells; block rings longer than melee; casts rise and fall; fizzle and move have ≥4
  attacks); the stamp.
- Client Core: `SoundCuesTests` (variant groups, new cue fields), `GlidePlanTests` (kinds, durations, curve endpoints, charge
  anticipation and late speed), `BattleEffectsTests` (muzzles on the volley's die delays, round robin, flight time, strike vs puff,
  shake/fall at the Wound cue, jab per melee die).
- `dotnet test` on `tests/Tabletop.Sounds.Tests` and `tests/Tabletop.Client.Tests`; `./scripts/check-client` empty.
- Frame captures in the Linux client (`--write-movie --fixed-fps 30`): a kept suite game with shooting and melee, its replay played
  as `sim-player-0`; contact sheets of glide, charge, volley (muzzle, tracer, strike, puff), wound shake and removal fall.
- Perf: the same replay with `TABLETOP_PERF_OUT`; no frame over 33 ms during a shooting sequence attributable to the effects.
