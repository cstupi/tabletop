# scripts/Sounds

The generator for the client's sound effects, `client/content/sounds/*.wav` (spec
`docs/superpowers/specs/2026-10-24-sound-effects-design.md`). Synthesised, deterministic C# (seeded `System.Random`, no Godot,
no packages). Tests: `tests/Tabletop.Sounds.Tests`.

```bash
./scripts/sounds                         # writes all of them into client/content/sounds/ and the stamp
./scripts/sounds some/dir                # or into another directory, no stamp
./scripts/sounds measure [dir]           # each WAV's loudness, peak, tail, spectrum (default: the shipped files)
godot --headless --path client --import  # then refresh the .import files
```

## Layout

- `Cues`: name → target loudness and builder, the spec's Sounds table: `shoot-1` plasma, `shoot-2` ballistic, `shoot-3` las,
  `shoot-4` heavy, `shoot-5` flame, `impact`, `miss`, `melee`, `whiff`, `block`, `wound`, `cast-friendly`, `cast-enemy`, `fizzle`,
  `move`. Each builder layers a transient, a body, weight (`Thump`: a falling saturated sine) and a room; `Cues.Build(name)` runs
  it through `Synth.Master` at its target. Each has its own noise seeds.
- `Synth`: white and brown noise (RMS 0.5), one-pole low and high pass, `Filter` (RBJ-style 12 dB/octave, resonant) and `Swept` (a
  state-variable filter following a function of time), `Glide`, `Tone`/`Sweep`/`Sine`, `Fm`, `Envelope`, `Saturate` (tanh),
  `Grains` (crackle, debris, gravel), `Echo`, `Room` (Freeverb), `Mix`, `Tremolo`, `Fade`, and `Master` (25 Hz high pass, gain
  searched to the target's momentary-max LUFS through a soft limiter under `CeilingDb`, -1.2 dBFS, then a 2 ms fade in and 15 ms out).
- `Measure`: BS.1770 K-weighting and momentary-max loudness, peak, DC, peak time, -40 dB tail, centroid, share under 150 Hz,
  spectral flatness, crest; `Measure.Read` decodes a shipped WAV.
- `Wav`: 16-bit mono 44.1 kHz PCM. Each file is under 100 KB.
- Tests: `CueTests` (format, loudness ±0.5 LU of target, peak ≤ -1 dBFS, DC, fades, the stamp), `CharacterTests` (what each sounds
  like: peak time, tail, body and weight shares, dominant frequency, spectral centroid and its direction, attack count).
- `Stamp`: `sounds.sha256` holds the hash of this folder's source and of each shipped file. The tests fail when either differs, so
  run `./scripts/sounds` after any change here. They do not compare against a fresh build: `Math`'s last bits differ between C
  runtimes.

## Recipes

Change a sound:

1. Edit its builder in `Cues` (layers, seeds) or its target in `Cues.All`. A new file also needs a name in `Cues.All`, a variant or
   cue in `client/src/Core/SoundCues.cs`, and a row in the spec's Sounds table.
2. `./scripts/sounds some/dir` and `./scripts/sounds measure some/dir` to compare against `./scripts/sounds measure` (the shipped
   files): loudness, peak, tail, centroid, low share.
3. `./scripts/sounds` writes the shipped files and the stamp; `dotnet test tests/Tabletop.Sounds.Tests`.
4. A new file needs its `.import`: `godot --headless --path client --import` (then `dotnet build client/Tabletop.csproj`).
5. Listen in the client: a kept suite game's replay plays the cues on its forward steps (`client/AGENTS.md` → `PerfProbe`,
   `TABLETOP_PERF_REPLAY_FROM` and `_STEP`); a `--write-movie` run also writes the mixed audio as `<name>.wav` beside the frames.
