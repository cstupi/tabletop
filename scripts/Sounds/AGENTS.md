# scripts/Sounds

The generator for the client's sound effects, `client/content/sounds/{shoot,melee,move,wound}.wav` (spec
`docs/superpowers/specs/2026-10-24-sound-effects-design.md`). Synthesised, deterministic C# (seeded `System.Random`, no Godot,
no packages). Tests: `tests/Tabletop.Sounds.Tests`.

```bash
./scripts/sounds                         # writes all four into client/content/sounds/ and the stamp
./scripts/sounds some/dir                # or into another directory, no stamp
godot --headless --path client --import  # then refresh the .import files
```

## Layout

- `Cues`: name → builder. `shoot` a high-passed noise crack over a falling 170 to 60 Hz thump (0.28 s); `melee` four inharmonic
  partials with quick decays over a noise tick (0.4 s); `move` two low-passed noise scuffs (0.3 s); `wound` a falling 125 to 48 Hz
  tone with a muffled noise body (0.3 s). Each has its own noise seed, a 2 ms fade in and a 12 ms fade out, and peaks at -6 dBFS.
- `Synth`: noise, two-stage one-pole low and high pass, sine and sweep, attack-and-decay envelope, mix, fade, normalise.
- `Wav`: 16-bit mono 44.1 kHz PCM. Each file is under 40 KB.
- `Stamp`: `sounds.sha256` holds the hash of this folder's source and of each shipped file. The tests fail when either differs, so
  run `./scripts/sounds` after any change here. They do not compare against a fresh build: `Math`'s last bits differ between C
  runtimes.
