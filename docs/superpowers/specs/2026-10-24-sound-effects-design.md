# Sound effects

Owner, 2026-10-24: *"We also need to add some basic sound effects for shooting, melee, moving, wounds."*

The client plays no sound today.

## Cues

Four cues, each one short sound:

| Cue | Plays when the log gains |
| --- | --- |
| Shoot | a shooting attack: once per attacking unit's shooting, not per weapon or die |
| Melee | a melee attack: once per striking unit's strikes |
| Move | a unit's move applied (a move step's answer, a hand move, a forced move, a deployment placement) |
| Wound | wounds placed on a unit or a model removed as a casualty: once per unit per batch |

`Core/SoundCues` (Godot-free, unit-tested) reads the events a log append adds and returns the cues in order, from the same
event and sequence kinds the combat log reads (`CombatLog`); the client names no rule. A batch plays at most one of each cue.
Cues play only for events that arrive live: not for the log a join or reconnect loads, not for a replay seek, not for an undo.

## Sounds

`scripts/Sounds/` generates the four as 16-bit mono WAV files, synthesised (noise bursts, envelopes, filtered tones), the way
`scripts/TerrainMeshes/` generates meshes, into `client/content/sounds/`. Each under 1 s and under 100 KB. A shot is a sharp
filtered crack, melee a short metallic clash, a move a soft scuff of footsteps, a wound a dull thud.

## Playing

A `SoundPlayer` node with one `AudioStreamPlayer` per cue plays the cues `SoundCues` returns; a cue already playing restarts.
Settings gains **Sound** (on by default) and **Volume** (0–100, default 70), kept in `ClientSettings`.
