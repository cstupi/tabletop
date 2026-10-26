# scripts/Sounds

The generator for the client's sound effects, `client/content/sounds/*.wav` (spec
`docs/superpowers/specs/2026-10-24-sound-effects-design.md`). Synthesised, deterministic C# (seeded `System.Random`, no Godot,
no packages). Tests: `tests/Tabletop.Sounds.Tests`.

```bash
./scripts/sounds                         # writes all of them into client/content/sounds/ and the stamp
./scripts/sounds some/dir                # or into another directory, no stamp
godot --headless --path client --import  # then refresh the .import files
```

## Layout

- `Cues`: name → builder, the spec's Sounds table: `shoot-1` plasma, `shoot-2` rifle, `shoot-3` burst (each a band-passed noise
  crack, swept tones, a slap-back `Echo`; 0.65–0.75 s), `impact`, `miss` (ricochet), `melee` (clash), `whiff`, `block` (clang),
  `wound`, `cast-friendly` (rising arpeggio), `cast-enemy` (falling tritone growl), `fizzle`, `move`. Each has its own noise seeds, a
  2 ms fade in and a 12 ms fade out, and peaks at -6 dBFS.
- `Synth`: noise, two-stage one-pole low and high pass, sine and sweep, attack-and-decay envelope, echo, tremolo, mix, fade, normalise.
- `Wav`: 16-bit mono 44.1 kHz PCM. Each file is under 100 KB.
- Tests: `CueTests` (format, level, fades, the stamp), `CharacterTests` (what each sounds like: peak time, tail, dominant frequency,
  spectral centroid and its direction, attack count).
- `Stamp`: `sounds.sha256` holds the hash of this folder's source and of each shipped file. The tests fail when either differs, so
  run `./scripts/sounds` after any change here. They do not compare against a fresh build: `Math`'s last bits differ between C
  runtimes.
