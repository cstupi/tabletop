# Client: setup and animation

## Setup: the lobby panel, the toolbar, built-in content and the generators

Lobby and setup are panels inside the game screen, driven by `game.Status`. Everything they do is
an `Edit`, not a `Command`: a third message kind, applied to the game's rows or the `table_state`
draft, broadcast, persisted, never logged. Editing is free for all, with no per-team permission and
no turn. Two people dragging the same piece is settled by whoever drops last, and each sees the
other's drag through presence. The server answers every setup edit with a `BoardMessage` carrying
the whole draft, and every lobby edit with a `GameInfoMessage`. The client has no local merge to do.

`LobbyPanel` covers the six lobby edits: `CreateTeam`, `PickTeam` (with `null` meaning watch),
`PickArmy`, `SetConfiguration` pre-filled with the current values, and, for the creator only,
`StartSetup` and `EndSetup`. The creator's two buttons are absent for anyone else, who reads what
they wait for (`LobbyView.WaitingForCreator`). The configuration is shown to everyone and editable by
the creator alone; for anyone else its controls are disabled with `LobbyView.CreatorOnly` as label and
tooltip. A seat line (`LobbyView.Seat`) says which team I am on, or that I have none and will watch
once play starts. `LobbyView.Teams` makes the team buttons: `Join team N` on an empty team, `On team N`
(disabled) on mine, and a team another participant holds named `Team N: {holders}`, disabled with
"ask the creator to place you on it", except for the creator, who may join it. The game name is held
to 64 characters (`TextLimits`). The Players list (`LobbyView.Players`) shows every participant, teamless
ones included, with their team or "No team"; for the creator each row is a team picker (No team, then
the teams by ordinal) sending `PickTeam(teamId, participantId)` (`LobbyView.Place`), the server's path
for putting a second player on a held team. A forged edit is refused with `NotPermitted`, so the UI is a
convenience over a server rule rather than the rule itself. A Leave game button (the game screen's confirm, then `Session.LeaveAsync`) gives the seat up from lobby and setup. The "End setup" button notes how many placements are still off the slab, because the
end-setup command deletes those rows before flipping.

`SetupToolbar` covers the fourteen setup edits: table size and texture; add built-in terrain, add
library terrain (`ListAssets(Terrain)` → `GetAssetVolumes` → `AddTerrain` with `VisualRef.OfAsset`),
move, scale, remove, swap mesh, override volumes and reset volumes; add and remove markers; set the
zone style (sending `ZoneStyles.ShapesOf(style)` as `SetZoneStyle.Shapes`) and swap two ordinals'
zones; load a layout (`GET /v1/layouts/{id}` then `ReplaceDraft`) and save one
(`POST /v1/layouts` from the client's own copy of the draft). Layouts stay management's. The game
server never writes an owner-side row.

The toolbar says the game server's `DraftLimits` before it refuses (`SetupLimits`): the Terrain and
Markers headings count `n/64` and `n/32`; each add button is disabled at the cap with the reason as its
tooltip; a planet generator's set is sent whole or, when it would pass 64, not at all with a toast; the
Table section names the 12–144" range; a layout name (Enter saves) and a marker label or condition
name in a menu dialog stop at 64 characters, a volume's notes at 500; an empty or invalid layout name
is caught before the request.

The built-in catalog is data, `client/content/builtin/catalog.json`: 35 terrain names with their `Volume` and
declarations and the mesh each draws with, and five textures. The client packs it (`BuiltinContent.Packed`) with its
meshes and textures, and the server publishes it with each release, which the client caches and uses in place of the
packed one (spec `2026-10-18-builtin-content-from-server-design.md`). `BuiltinContent` adds two marker visuals and the
zone style ids. A minimal art set ships with it, so that a generated table looks like a table.

The art is eighteen files, not fifty. The 35 pieces group into thirteen meshes that differ only by
footprint, height, scale and yaw: `hill` (4 pieces, round), `hill-rect` (2, square), `woods` (5), `ruins` (4), `building` (3),
`wall` (2), `rock` (2), `field` (2), `water` (4, the rivers and streams), `lake` (1, round), `pad` (4, the flat ground
hazards), `crater` (1, round), `haze` (1, the toxic smog). `scripts/terrain-meshes` generates all thirteen
(`scripts/TerrainMeshes/AGENTS.md`): glTF 2.0 binary, at most 20 000 triangles and 2 MB each, vertex-coloured PBR materials
(translucent ones, the smog and water surfaces, in a second node), authored glTF Y-up in a unit box: the footprint on x and z in [−0.5, 0.5] (within r = 0.5 for a round one), the height on
y in [0, 1], base at y = 0. `BuiltinContent.MeshOrientation` (pitch 90) reads it into the table frame, z up, the value
the web app writes for a Y-up asset, and `MeshLoader.LoadBuiltin` scales that box onto the placement's first
volume, so `water` serves a 3"×12" river and a 2"×12" stream. The five table textures are
seamless 1024×1024 PNGs, authored as a 12"×12" tile because `SlabNode.Set` derives `Uv1Scale` from
`width/12`. Markers and zones need no art: `MarkerNode` builds a disc and a `Label3D`, `ZoneNode`
builds `ArrayMesh` fans from `ZoneStyles.Resolve`.

Every mesh file is still optional, and a missing one
still draws the hitbox: `LoadBuiltin` returns null and `TerrainNode` keeps its per-volume proxy
visible, the same degrade path as a missing asset, and a missing texture leaves the slab its flat
colour. That path is the fallback rather than the plan, kept rather than deleted, so a build with
no `content/builtin/` at all still renders a playable table. For a built-in placement the placement's volume rows are
the volumes rather than overrides, because there is no `asset_volume` row to fall through to. They
are always written on add, and "reset" re-copies the shipped ones.

`TerrainGenerators` implements the rulebook's four planet tables (GF p.18–19) as
`Generate(Planet, TableSize, Random)`: nine fixed pieces plus 4D3 rolled on 2D6. They are returned
as a batch of `AddTerrain` edits placed off the slab beside the long edge at `x = width + 4`, in
rows 8" apart, for players to drag on. The client rolls, because nothing in setup is logged and no
seeded RNG is owed. An entry naming alternatives ("Hill, Forest, or Field") is resolved by that
same RNG, so rolling a table produces a finished board in one action; a player
who dislikes a piece swaps its mesh or deletes it, which the toolbar already offers. The tables ship with the client and the server never reads them, which keeps
the game server ruleset-blind during setup. 40k has no such tables. Declarations follow the tables
literally. Conditional clauses ("dangerous when using Rush or Charge", "units on top may ignore one
unit for line of sight") go in `Notes` and are never flags. The engine reads a closed set of six
and carries the rest as text nobody interprets.

Zones are drawn for every ordinal during setup, in the team's colour when a team with that ordinal
exists and grey otherwise, so a group can see and swap zones before every team has formed. From
`playing` on, only zones with a team are drawn, through round 0 (deployment and the phases before the first round),
and none from round 1 (`TableView.ZonesShown`). A new game's draft starts with the Frontline style's zones
(`GameSession.NewDraft`); setup may set another style.

## Animation

`AnimationQueue` sits between the projection and the renderer and does nothing else. The projection
applies an event the moment it arrives. Whether the table then plays it out is the player's own
`Animate` setting in `ClientSettings`. It is presentation only, so it is not on the game row. The
game's `waits_between_steps` is on the game row, because it changes what the server does.

`Take(animate)` returns everything at once when animating is off, and one event at a time when it
is on. `Animator` raises `Played` for each event as it takes it (a whole batch at once with Animate off), glides
every piece of a `ModelsMoved`, or of a step's moves (`AnimationQueue.Relocations`), together over 0.4 s, then pumps the next once the last has landed; `FloatingText` shows each
event's results on `Played`. The queue may lag the projection and never
delays an Ask. A player may answer while a roll's readout is still up, because the decision box reads
the projection directly and the queue holds no lock. Presence is never animated, only drawn.

A roll is a readout and not an object. The dice tray and the combat log show it as a `RollEntry`: the roll's
name, its threshold, the faces with the passing dice marked, and each modifier by source, all of which `RollRecord`
already holds. With Animate on the tray's dice tumble for 0.4 s, each face changing in place every frame. No die is placed on the table.

**A step may carry several rolls, and the readout carries all of them**, one block each, stacked.
`StepCompleted.Rolls` is a list, and the ruleset fills it with more than one wherever it splits a
roll: OPR's `roll-to-hit` takes one pool per Quality value the attacking models bring, since
rulebook p.4 tests each attack against *"the model's Quality value"* and a joined hero brings its
own. Each block's threshold is what tells the pools apart. The combat log's roll entries follow the
same list: one `RollEntry` per `RollRecord`. Godot physics is out
of the scene, so a die on the table would be a canned animation of a result the server has already
decided, and the readout says more than a tumbling die can. The client shell spec → Animation says
the same.

