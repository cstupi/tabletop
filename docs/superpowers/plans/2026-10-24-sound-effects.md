# Sound effects: implementation plan

Spec: `docs/superpowers/specs/2026-10-24-sound-effects-design.md`. Four cues (Shoot, Melee, Move, Wound), played only for events that arrive live.

## Findings

- Live signal: `Session.EventsApplied(batch)` is raised for a live socket batch, for a replay's animated forward step (`Session.Replay.cs`), and with an empty batch for other changes. A batch is live when `!Session.CatchingUp` (true from a reset or board until `ModelFacts` or an empty page), `!Session.Replaying`, and it holds no `Undone` (the same silence `FloatingResults.OnBatch` keeps).
- Kinds read, as `CombatLog` reads them: `SequenceStarted.Sequence` `shooting` / `melee` / `attack-sequence` (an `attack-sequence` is pushed per weapon group under a `shooting` or `melee` frame, `CombatLog.Words` `AttackSequences`), `SequenceFinished` / `SequenceAbandoned` (close a frame), `StepCompleted.Changes` and a bare `StateChange` (`ModelsMoved`, `WoundsSet`). Wounds are totals on the wire, so `SoundCues` keeps each model's last total, as `FloatingResults` does.
- `ClientSettings` is a record with a `Dto` for the JSON; `SettingsDialog` builds rows over `Session.UpdateSettings`.
- `client/export_presets.cfg` uses `export_filter="all_resources"`: a `.wav` is exported as its import. No include filter change.
- Generator pattern: `scripts/TerrainMeshes` (console project, `Stamp` + `meshes.sha256`, launcher `scripts/terrain-meshes`, tests in `tests/Tabletop.TerrainMeshes.Tests`).

## Tasks

1. `SoundCues` (Core), tests first.
2. `ClientSettings.Sound` and `.Volume`, tests first.
3. The generator `scripts/Sounds` (`Wav`, `Synth`, `Cues`, `Stamp`, `Program`), `tests/Tabletop.Sounds.Tests`, solution entries, launcher.
4. Generate the WAVs and `sounds.sha256`; Godot import files.
5. `SoundPlayer` node, `SettingsDialog` rows, `GameScreen.Play` wiring.
6. Docs; full build and test.

### Task 1: `SoundCues`

Files: `client/src/Core/SoundCues.cs`, `tests/Tabletop.Client.Tests/SoundCuesTests.cs` (uses `TableUx`).

API: `enum Cue { Shoot, Melee, Move, Wound }`; `sealed class SoundCues` with `Seed(Projection)`, `OnBoard()`, `IReadOnlyList<Cue> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool silent)`. The caller passes `silent` for catching up or a replay; a batch with an `Undone` is silent too, and a silent batch reseeds the wound totals and the open frames from `p`.

Tests, each red first:
- a `shooting` frame then an `attack-sequence` under it gives `[Shoot]`; a second `attack-sequence` under the same frame, in this batch or a later one, adds none;
- the same under `melee` gives `[Melee]` once per striking unit (two actors under one `melee` frame give two across batches);
- an `attack-sequence` with no open `shooting` or `melee` frame gives nothing;
- a `ModelsMoved` with a relocation `To` an `OnTable` (in a `StepCompleted`, as a bare `StateChange`, or a deployment from the shelf) gives `[Move]`; one that only shelves models gives none;
- a `WoundsSet` whose total rose, or a model going table to shelf, gives `[Wound]` once per batch however many units;
- a `WoundsSet` that did not rise gives none;
- a batch holds each cue at most once, in order of first appearance;
- `silent: true`, and a batch with an `Undone`, give nothing and reseed;
- `SequenceFinished` / `SequenceAbandoned` close a frame.

Then implement; `dotnet test tests/Tabletop.Client.Tests --filter SoundCues`. Commit.

### Task 2: Settings

Files: `client/src/Core/ClientSettings.cs`, `tests/Tabletop.Client.Tests/SettingsTests.cs`.

Tests first: defaults `Sound == true`, `Volume == 70`; both round-trip through Save/Load; a settings file without them loads the defaults; `Volume` loads clamped to 0..100; both take part in `Equals`.
Then add `bool Sound { get; init; } = true`, `int Volume { get; init; } = 70`, the `Dto` fields, `Load` (clamp), `Save`, `Equals`. Commit.

### Task 3: The generator

Files: `scripts/Sounds/Tabletop.Sounds.csproj` (as `Tabletop.TerrainMeshes.csproj`, assembly `Tabletop.Sounds`), `Wav.cs`, `Synth.cs`, `Cues.cs`, `Stamp.cs`, `Program.cs`, `AGENTS.md`, `CLAUDE.md` (`@AGENTS.md`); `scripts/sounds` launcher; `tests/Tabletop.Sounds.Tests/` (csproj as the TerrainMeshes tests); both projects in `Tabletop.sln`.

- `Wav.Of(float[] samples)`: 16-bit mono PCM RIFF, 44.1 kHz.
- `Synth`: seeded `System.Random` noise, one-pole low-pass and high-pass, sine partials, exponential decay envelopes, linear fade in (2 ms) and out (last 10 ms), `Normalize` to -6 dBFS (0.5012).
- `Cues`: `shoot` (a high-passed noise crack, 3 ms attack, fast decay, with a low sine thump falling 160 to 60 Hz, 0.25 s), `melee` (inharmonic metallic partials with fast decays over a short noise tick, 0.35 s), `move` (two soft low-passed noise scuffs, 0.3 s), `wound` (a sine falling 110 to 55 Hz with a low-passed noise body, 0.3 s). Each has its own fixed seed.
- `Program`: writes `shoot.wav`, `melee.wav`, `move.wav`, `wound.wav` into `client/content/sounds` (or the directory given); a full run into the default directory also writes `scripts/Sounds/sounds.sha256` (`Stamp`, as `TerrainMeshes`).

Tests first (`tests/Tabletop.Sounds.Tests`): each cue builds the same bytes twice; valid RIFF/WAVE header, 16-bit mono 44.1 kHz; under 1 s and 100 KB; peak within 0.5 dB of -6 dBFS; first and last samples zero and no large step between neighbours (no click); not silent; the four differ; the shipped files match `sounds.sha256` and the source hash matches.

### Task 4: Generate and import

- `./scripts/sounds`; commit `client/content/sounds/*.wav` and `scripts/Sounds/sounds.sha256`.
- Godot imports a `.wav` as an `AudioStreamWAV` through `<name>.wav.import`, written by the editor. Commit the four `.import` files as the repo does for meshes and textures, if the repo commits them. `all_resources` exports them; no preset change.

### Task 5: Playing

Files: `client/src/Table/SoundPlayer.cs`, `client/src/Screens/SettingsDialog.cs`, `client/src/Screens/GameScreen.Play.cs`.

- `SoundPlayer : Node`: `Bind(Session)`; four `AudioStreamPlayer`s; subscribes to `EventsApplied` and `BoardChanged` (`OnBoard`, then `Seed`); on a batch calls `OnBatch(batch, projection, silent: CatchingUp || Replaying)`; per cue, when `Settings.Sound`, sets `VolumeDb = Mathf.LinearToDb(Volume / 100f)` and `Play()` (restarts a playing cue). `_ExitTree` unsubscribes.
- `SettingsDialog`: a `Sound` check box and a `Volume` `HSlider` (0..100) over `UpdateSettings`.
- `GameScreen.Play`: add and `Bind` the `SoundPlayer` beside `FloatingText`.
- `./scripts/check-client` passes.

### Task 6: Docs and verification

- `client/AGENTS.md`, `scripts/Sounds/AGENTS.md`, root `AGENTS.md` scripts row. Not `docs/handoff.md`.
- `dotnet build` and `dotnet test` from the root; `./scripts/check-client`.
