# Browser play

Owner, 2026-10-24, brainstorming a computer opponent: a 2D board players play on in the browser, because *"This will let
us view and evaluate the ai faster if it's through a browser and many can run at once"*. For *"Regular players too. Same
with replays"*. *"Use little circles for models so it's easier. We should be able to setup without godot."* Terrain:
*"All of the terrain. Let them setup, have option for random, have a few predefined. We want these options for godot setup
too."* *"Desktop is enough."* *"Yes random should place them on the table with a balance across both teams."*

## Intent

Any player sets up, plays and replays a whole game from the browser on a top-down 2D board, with no Godot. A game is the
same game in either client: one player in Godot and another in the browser at the same table works. The near purpose is
the computer opponent (designed next, separately): many browser tabs of games against it, watched and judged quickly.

## Goals

- Every step of a game from the browser: create, lobby, setup, play to the end, leave, and replay.
- The 2D board: models as circles, terrain footprints, objective markers, deployment zones, all to scale in inches.
- Terrain setup equal to Godot's: hand placement and editing of every catalog piece, a random table, saved layouts, and new
  **predefined tables**, which Godot gets too.
- Replays of their own games for every player, in the browser; admins keep watching any game.

## Non-goals

- Phones, tablets and touch. Desktop with a mouse and keyboard.
- Meshes, painted looks, 3D, lighting, animation polish. It is a clean diagram.
- Mesh upload, army import and the library: they stay on the website pages they are on.
- The computer opponent itself.

## Anti-goals

- **Rules in the browser.** The browser judges nothing the server judges; every answer, move and edit goes to the server,
  which refuses what is illegal, as it does for Godot (root `AGENTS.md`: the rules engine lives only in `server/game/`).
- **A second protocol.** The browser speaks the game socket and management's `/v1` exactly as the Godot client does. No
  browser-only commands, edits or endpoints, except where this document adds one for both clients.
- **Godot losing anything.** Godot keeps every feature; it gains the predefined tables and whatever shared generator the
  random table moves to.

## Constraints

- The browser app grows out of `viewer/`: it already follows a live game over the socket (`liveFeed.ts`), ports
  `EventApplier` (`live/apply.ts`) and the projection, draws the 2D board (`board/`), lists open Asks (`AskPanel`,
  `asks/describe.ts`) and has a hosted build at `/viewer/` on the website's origin that reads the website's session.
  It plays back only today (`viewer/AGENTS.md` → Playback only).
- The hosted build is admin-only today (*Not authorised* without the `admin` role) and the website shows its Replays link
  only to admins. Both open to every signed-in player. The game server already lets a participant connect, and watch its own
  game read-only (`ConnectionHandshake`); a non-admin is refused other games.
- Game creation, joining, leaving and deleting are management's `/v1/games` calls; `GET /v1/games` is public.
- Every lobby and setup change is an `Edit` on the socket (`libs/Tabletop.Protocol/Edits.cs`: `CreateTeam`, `PickTeam`,
  `PickArmy`, `LeaveTeam`, `StartSetup`, `EndSetup`, `SetTableSize`, `SetTexture`, `AddTerrain`, `MoveTerrain`,
  `ScaleTerrain`, `RemoveTerrain`, `OverrideTerrainVolumes`, `SwapTerrainMesh`, `AddSetupMarker`, `MoveSetupMarker`,
  `RemoveSetupMarker`, `SetZoneStyle`, `SwapZones`, `ReplaceDraft`); optional rules ride on the game. Every play action is a
  command (`AnswerAsk`, hand moves, conditions, undo, sight queries).
- The decision kinds the browser answers are the client's `AskMode`s over the server's `LegalSet`s: Choose (`Options`),
  Move (`MoveRegion`), PickPieces (`PieceSelection`), Assign (`Assignment`), Allocate (`Allocation`), TokenSpend, Confirm
  (`Confirmation`), plus `game_over`.
- The built-in terrain catalog is served by management (`GET /v1/builtin`); saved layouts are `GET/POST/PATCH/DELETE
  /v1/layouts`.
- The random table is `client/src/Core/TerrainGenerators.cs` (Grimdark Future p.18–19's planets plus the owner's Ruins pack),
  C# in the Godot client's core, so the browser cannot run it as it stands.

## Decided

- **Who:** every signed-in player, for their own games; admins as today.
- **Where:** the hosted viewer at `/viewer/`, linked from the website header for everyone (no longer admin-only), and the
  developer viewer as now.
- **Models are little circles:** each model a circle as wide as its base's longer side, in its team's colour, with a tick
  for its facing; its real base still decides everything on the server. Terrain draws its footprints with their
  declarations (the board already does). Markers and zones as the board draws them.
- **Setup without Godot:** create the game, the lobby (teams, players, armies, optional rules), the table (size, texture),
  terrain, objective markers, deployment zones, end setup.
- **Terrain:** place any catalog piece; move, turn, scale, remove; edit its volumes, declarations and notes; swap its mesh
  reference; a random table per planet, placed on the table with the terrain balanced across the teams; load and save
  layouts; and a few **predefined tables**, which Godot's setup offers too.
- **Desktop only.**

## The builder's calls

These are left to the builder; the ones below them are the coordinator's calls, open to the owner's correction:

- How the viewer's code is reorganised to hold a play mode beside playback.
- How the decision box looks in the browser, as long as every `AskMode` above can be answered.
- Mouse and keyboard handling on the board: drag to move or deploy, rotate, select, measure.

Coordinator's calls:

- **Drags are solid, as in Godot** (owner ruling: collision means physical solidity). `libs/Tabletop.Geometry` is
  Godot-free C#; it runs in the browser compiled to WebAssembly (.NET `browser-wasm`, exported functions), not ported, so
  a browser drag lifts over, lands clear (`Collision.Land`, `Hover`) and rests exactly as Godot's does, and a mixed table
  agrees. A few megabytes of download is acceptable on desktop.
- **Heights:** the browser always drops lifted (the highest open surface, Godot's default); it has no level keys, since
  the owner noted most tables have no bridges or caves. A model standing above the table draws its circle with a small
  height label. Placing under a floor stays a Godot action.
- **Moves in the browser** show each moving model's allowance as a circle of that radius round its start and a ruler while
  dragging; the route round terrain and the difficult cap are the server's to judge, and a refused move comes back with
  the server's reason. Godot's full reach outline (`MoveRegions`) is not ported.
- **What play shows besides the decision box:** a unit card for the selected or hovered unit (stats, weapons with range and
  AP, rules with their texts, wounds), the combat log, the latest dice, Undo, range rings for the selected unit, the sight
  check on an assigned target, contact and violation marks, and the menus' hand actions (set aside, transports, wounds,
  conditions, objective control, score) where Godot has them. Not ported: themes, animation, sounds, presence, camera
  follow, turn banners beyond a plain line.
- **The random table moves to shared C#** and now lays the table out: it rolls the pieces as today (p.18–19) and places them
  on the table, clear of each other, so each team's side gets an even share of cover, line-of-sight blockers and difficult
  terrain (p.12's balance), mirrored about the table's centre for two teams and spread evenly over the zones for three or
  four. Both clients reach it the same way, e.g. a management endpoint returning the placements, and it replaces only the
  draft's terrain (sent as `ReplaceDraft` of the current draft with the new terrain): size, texture, markers and zones stay.
  Godot's Random button changes to this.
- **Predefined tables** are built-in layouts: whole `TableState`s at 6'×4' with terrain, four objective markers and
  Frontline zones, shipped with the built-in content and listed by management under their own read-only endpoint (e.g.
  `GET /v1/layouts/builtin`), so an account's `GET /v1/layouts` is unchanged. Loading one is `ReplaceDraft`, as loading a
  saved layout is. Four to start, one per planet the generator knows except Generic, made by the coordinator to Grimdark
  Future p.12's terrain balance.
- **Replays** in the browser are the viewer's existing playback over `/ws?watch=1`, listing the player's own games.
