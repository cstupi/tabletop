# Browser play C2: the game page, the lobby and setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the browser, a player takes a game from its lobby (teams, armies, optional rules, Start setup) through setup on
the 2D board (table size and texture, terrain placed, dragged solid, turned, scaled, edited and swapped, markers, zones, saved,
predefined and random layouts) to End setup, sending the same edits under the same limits as Godot.

**Architecture:** `src/lib/game/GamePage.svelte` owns one `GameConnection` (C1 Task 6) and the board `View`, and shows the
lobby, setup, or (from playing) play by `game.status`. Godot's lobby and setup decisions port to `src/lib/lobby/` and
`src/lib/setup/` as TS modules with Vitest tests; setup drags call plan B's `Geometry` (`landPiece`, `turnPiece`) so a drop
lands where Godot's would. The setup board is C1's `Board` over a scene built from the draft, driven through
`BoardInteraction` and overlays.

**Tech Stack:** Vite 8, Svelte 5, TypeScript 5.9, Vitest 5, Node 24; plan B's `viewer/src/lib/geometry/` (WebAssembly).

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Decided → Setup without Godot, Terrain; Coordinator's
calls → Drags are solid, Heights, The random table, Predefined tables). Prerequisites: `2026-10-24-browser-play-c1-shell.md`;
plan A's endpoints (`2026-10-24-browser-play-a-tables.md` → Endpoint contracts, Tasks 5–6); plan B's `Geometry`
(`2026-10-24-browser-play-b-geometry.md` Task 5). Godot sources ported: `client/src/Core/Seats.cs`, `LobbyView.cs`,
`RulesOptions.cs`, `SetupLimits.cs` (with `TextLimits`), `TerrainCardContent.cs`, `TerrainLabel.cs`, `MenuModel.cs`
(`ForSetupPiece`, `TurnInDraft`, `RemoveFromDraft`), `BuiltinCatalog.cs` (terrain entries, `Place`);
`client/src/Panels/LobbyPanel.cs`, `SetupToolbar.cs`; `client/src/Hud/TerrainCard.cs`; `client/src/Table/TableInput.cs`
(setup drops).

## Findings that shape the plan

- **Every lobby and setup control is one edit Godot already sends**; the browser adds none. Godot's controls and what they
  send: Set size `SetTableSize`; texture `SetTexture(builtin)`; Add built-in `AddTerrain(Place(name, Beside(size), yaw 0))`;
  Add my terrain `AddTerrain` with the asset's volumes (`GET /v1/assets/{id}/volumes`), new ids, scale 1; Clear terrain
  `ReplaceDraft(draft with terrain [])`, asked first; a drop `MoveTerrain(id, landing, yaw)` / `MoveSetupMarker`; turn
  `MoveTerrain(id, DragBodies.Turn(...), yaw)` (`TurnInDraft`); scale `ScaleTerrain` (0.25–4, step 0.25); swap mesh
  `SwapTerrainMesh(id, builtin | asset)`; reset volumes `OverrideTerrainVolumes` with the mesh's own; each volume field one
  `OverrideTerrainVolumes` with the whole list and that volume changed; remove `RemoveTerrain` / `RemoveSetupMarker`; Add
  objective `AddSetupMarker(objective, FreeSpot(…, 3), 3, no label, objective-ring)`; Add token `(token, FreeSpot(…, 1), 1,
  "token", chip)`; Set zones `SetZoneStyle(style, ShapesOf(style))`; Swap `SwapZones(a, b)`; Load layout `ReplaceDraft(layout)`;
  Save as `POST /v1/layouts` with the draft named.
- **Godot has no layout delete**; the spec asks for it: `DELETE /v1/layouts/{id}` exists and is the account's own.
- **`GET /v1/builtin` answers 404 when nothing is published** (every dev stack). Godot falls back to its packed catalog; the
  browser falls back to the same file, `client/content/builtin/catalog.json`, bundled by Vite. Circle footprints keep `a` as
  the radius (`Footprint.Circle(a)`, the card shows `2a` across).
- **Wire declarations** are the `[Flags]` string, canonical order, `", "`-joined, `"none"` when empty (`project.ts`
  `declarationsOf` reads it; this plan adds the writer).
- **Off-table terrain**: Godot adds pieces beside the table (`x = W + 4 + {0,8,16}`, `y = 4 + {0,8,16,24}`) and End setup drops
  pieces whose centre is off the slab (`Table.ContainsPoint`), naming the count on the button. The setup board fits the table
  plus whatever terrain lies beside it (`fitBox`).
- **`lib/geometry` stays out of playback** (plan B's lazy test): setup modules live in `src/lib/setup/`; `GamePage` calls
  `loadGeometry()` only when the game is in setup or later.

## Global Constraints

- The browser judges nothing; limits are mirrored only to explain before the server refuses (`SetupLimits`, `TextLimits`):
  64 terrain, 32 markers, 12–144" edges, 64-character names and labels, 500-character notes.
- Every send goes through `GameConnection.sendEdit`; a `refused` outcome shows Godot's text (`refusalText`) in the panel's
  `role="alert"` line.
- New ids are `crypto.randomUUID()`. Godot's copy where Godot has it.
- `src/lib/components/` and the playback modules import nothing from `src/lib/geometry/` or `src/lib/setup/drag.ts`.
- Files under 500 lines.
- Commands: `cd viewer && npm test`, `npm run check`, `npm run build:hosted`; `./scripts/geometry-wasm` before the viewer
  tests that load the WASM; `docker compose up -d` for Task 8.

## Review Focus

- Two players editing one draft: every `board` frame replaces the draft, and a piece selected or held by me that another
  player removed drops out of selection and the drag cancels. Test: `a held piece that leaves the draft cancels the drag`
  (Task 5).
- A drag across a `board` frame that moved other terrain: the landing is recomputed against the new draft on the next move,
  never sent against a stale one. Test: `the drop lands against the draft current at the drop` (Task 5).
- A random table before any zones are set: the request leaves `zones` out and the server balances for Frontline's two.
  Test: `randomRequest leaves zones out when the draft has none` (Task 6).
- A layout from a larger or smaller table: `ReplaceDraft` changes the size too (Godot's dialog says so). Test: `loading a
  layout replaces the whole draft, size included` (Task 6).
- Notes over 500 characters or a label over 64: the field stops at the limit. Test: `volume notes are clipped to 500 and
  empty notes clear` (Task 3).

---
### Task 1: The game page and the lobby

**Files:** create `viewer/src/lib/game/GamePage.svelte`, `viewer/src/lib/lobby/seats.ts`, `viewer/src/lib/lobby/lobbyView.ts`,
`viewer/src/lib/lobby/rulesOptions.ts`, `viewer/src/lib/lobby/LobbyPanel.svelte`; modify `viewer/src/App.svelte` (the
`game` route renders `GamePage`); test `viewer/tests/lobbyView.test.ts`, `viewer/tests/rulesOptions.test.ts`.

**Interfaces:**

```ts
// seats.ts — Seats.Mine, Seats.Teams
export function mine(game: GameInfo, participantId: string | null): Participant[];
export function teamsOf(game: GameInfo, participantId: string | null): string[];          // by ordinal
export const isCreator = (game: GameInfo, participantId: string | null): boolean;
// lobbyView.ts — LobbyView, word for word
export interface TeamChoice { readonly teamId: string; readonly text: string; readonly enabled: boolean; readonly why: string;
  readonly edit: PickTeam | LeaveTeam }
export interface ArmySeat { readonly teamId: string | null; readonly label: string; readonly armyId: string | null }
export interface LobbySeat { readonly participantId: string; readonly name: string; readonly seat: string; readonly selected: number;
  readonly army: string | null }
export const CREATOR_ONLY: string, NO_TEAM: string, NO_PLAYERS: string;
export function seatLine(game: GameInfo, participantId: string | null): string;
export function teamChoices(game: GameInfo, participantId: string | null, creator: boolean): TeamChoice[];
export function armySeats(game: GameInfo, participantId: string | null): ArmySeat[];
export const pickArmy = (seat: ArmySeat, armyId: string | null): PickArmy;   // { kind: 'pick_army', armyId?, teamId? }
export function seatChoices(game: GameInfo): { teamId: string | null; label: string }[];
export function players(game: GameInfo): LobbySeat[];
export function members(game: GameInfo, teamId: string): string;
export function place(game: GameInfo, participantId: string, choice: number): PickTeam;
export function waitingForCreator(game: GameInfo, creator: boolean): string | null;
/** LobbyPanel.EndText: "End setup" or "End setup (n off-slab pieces will be dropped)". */
export function endSetupText(draft: TableState | null): string;
// rulesOptions.ts — RulesOptions
export const CUSTOM = 'Custom';
export function valueOf(option: RulesOption, map: Readonly<Record<string, string>>): string;
export function presetOf(catalog: RulesOptionCatalog, map: Readonly<Record<string, string>>): string;
export function mapOf(catalog: RulesOptionCatalog, chosen: Readonly<Record<string, string>>): Record<string, string>;
/** The SetConfiguration Apply sends (WaitsBetweenSteps false; the game's properties hash). */
export function configurationOf(game: GameInfo, name: string, shape: SchedulerShape, enforcement: Enforcement,
  catalog: RulesOptionCatalog, chosen: Readonly<Record<string, string>>): SetConfiguration;
```

`GamePage` (props `gameId`, `army?`, `auth`, `socketUrl`): `connectGame({ url: socketUrl, gameId, jwt: auth.jwt })`, closes on
destroy; holds `view` for the board; sends `PickArmy(army)` once after the first welcome when `army` is set; shows phase and
`closeReason` (closed: a link to `#/games`); by `game.status`: `lobby` → `LobbyPanel`; `setup` → `LobbyPanel` collapsed beside
`SetupPage` (Task 7); `playing`/`ended` → D's play page, and until plan D lands, `PlaybackView` over the connection's frames
(a `FrameProjector` fed from `onEvents`). `LobbyPanel` is `LobbyPanel.cs`: seat line, waiting line, team buttons (enabled and
tooltip from `teamChoices`), Watch (`PickTeam(null)`), Create team, Players (the creator gets a team picker per row sending
`place`), my army pickers over `GET /v1/armies`, the configuration (name, activation, enforcement, optional rules: a preset
select and one select per option labelled `Label (Page)`, a preset with `enforcement` setting it), Apply (name checked by
`nameProblem`, C1 Task 4), read-only with `CREATOR_ONLY` for others, Leave game (asked first, `leaveGame` then `#/games`),
Start setup (creator, lobby), End setup (creator, setup, `endSetupText`).

- [ ] Tests first, Godot's `LobbyViewTests` and `RulesOptionsTests` cases in TS: `the seat line names my teams and asks for an
  army`; `a watcher's seat line`; `an empty team is joined, my team is left, a held team asks the creator`; `the creator may
  join a held team`; `one army picker per team I hold, labelled per team when several`; `players list teamless rows with No
  team`; `members name each army or no army yet`; `place builds PickTeam with the participant`; `waiting lines by status, none
  for the creator`; `endSetupText counts pieces whose centre is off the slab` (72×48: centres (76, 4) and (72, 48) → 1);
  `presetOf finds the matching preset else Custom`; `mapOf drops defaults`; `configurationOf carries the hash and the map`.
- [ ] `cd viewer && npx vitest run tests/lobbyView.test.ts tests/rulesOptions.test.ts` fails.
- [ ] Implement the modules, then `LobbyPanel.svelte` and `GamePage.svelte`.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the game page over GameConnection, and the lobby — teams, armies, optional rules, start setup`.

### Task 2: The terrain catalog, library terrain and the layout calls

**Files:** create `viewer/src/lib/setup/catalog.ts`; modify `viewer/src/lib/api/types.ts`, `viewer/src/lib/api/client.ts`,
`viewer/vite.config.ts` (alias `@builtin-catalog` → `../client/content/builtin/catalog.json`, `server.fs.allow: ['..']`),
`web/Dockerfile` (viewer stage: `COPY client/content/builtin/catalog.json /src/client/content/builtin/catalog.json`),
`.dockerignore` (`!client/content/builtin/catalog.json` after `client`, the line plan A Task 5 adds; keep one); test `viewer/tests/catalog.test.ts`, `viewer/tests/api.test.ts`.

**Interfaces:**

```ts
// catalog.ts — BuiltinCatalog's terrain entries and Place, ported
export interface BuiltinTerrain { readonly id: string; readonly label: string; readonly volume: Volume }
export interface Catalog { readonly terrain: ReadonlyMap<string, BuiltinTerrain>; readonly textures: readonly string[] }
/** BuiltinCatalog.Parse's terrain and texture checks; throws Error naming the first fault (Godot's messages). */
export function parseCatalog(json: unknown): Catalog;
/** GET /v1/builtin, else (404) the packed file: the catalog in use, as Godot's BuiltinContent. */
export function loadCatalog(api: Api, packed?: unknown): Promise<Catalog>;
/** The wire's [Flags] string: canonical order (palette DECLARATIONS), ", "-joined, "none" when empty. */
export function declarationsWire(names: readonly string[]): string;
/** BuiltinCatalog.Place: a new placement of the built-in with one volume copied from the catalog, new ids. */
export function placeBuiltin(entry: BuiltinTerrain, position: Point3, yaw: Yaw, scale?: number): TerrainPlacement;
/** AddLibraryTerrain: the asset's volumes with new ids, scale 1 (the asset's own scale is already in its mesh and volumes). */
export function placeAsset(assetId: string, volumes: readonly TerrainVolume[], position: Point3): TerrainPlacement;
/** TerrainLabel.NameOf: the built-in's label, else "Terrain". */
export function nameOf(piece: TerrainPlacement, catalog: Catalog): string;
// api/types.ts
export interface LayoutSummary { readonly id: string; readonly name: string; readonly size: TableSize; readonly terrain: number; readonly markers: number }
export interface AssetDto { readonly id: string; readonly name: string; readonly kind: 'model' | 'terrain' | 'texture';
  readonly uploaded: boolean; /* other AssetDto fields as the server sends them */ }
export type Planet = 'generic' | 'hospitable' | 'wasteland' | 'death' | 'ruins';
export interface RandomTableRequest { readonly size: TableSize; readonly planet: Planet; readonly zones?: readonly DeploymentZone[]; readonly seed?: number }
export interface RandomTable { readonly seed: number; readonly terrain: readonly TerrainPlacement[]; readonly unplaced: readonly string[]; readonly unlisted: readonly string[] }
// Api gains
builtinCatalog(): Promise<unknown | null>;                    // GET /v1/builtin; null on 404
terrainAssets(): Promise<AssetDto[]>;                         // every page of /v1/assets?kind=terrain&limit=200, uploaded only
assetVolumes(id: string): Promise<TerrainVolume[]>;           // GET /v1/assets/{id}/volumes
listLayouts(): Promise<LayoutSummary[]>; getLayout(id: string): Promise<TableState>;
saveLayout(table: TableState): Promise<TableState>; deleteLayout(id: string): Promise<void>;
builtinLayouts(): Promise<TableState[]>;                      // GET /v1/layouts/builtin
randomTable(r: RandomTableRequest): Promise<RandomTable>;     // POST /v1/layouts/random
```

- [ ] Tests first. `catalog.test.ts`: `parses the packed catalog` (the real file: 38 terrain, textures from `grass`; `barricade`
  is a solid 3×1 rect, top 1, `"cover, difficult"`); `a circle keeps a as the radius`; `faults name the entry` (unknown
  declaration `"lava"` → "Terrain 'x' declares 'lava', which is not a terrain declaration."; kind `"gas"`; top 0; duplicate
  id); `declarationsWire orders and joins` (`['difficult','cover']` → `"cover, difficult"`, `[]` → `"none"`);
  `placeBuiltin copies the volume with fresh ids` (two calls, different placement and volume ids, mesh `{ builtin }`);
  `placeAsset keeps the volumes with new ids at scale 1`; `loadCatalog falls back to the packed file on 404`.
  `api.test.ts`: `randomTable posts the request and returns the terrain`; `builtinCatalog is null on 404`; `terrainAssets
  keeps uploaded terrain only`; `deleteLayout accepts 204`.
- [ ] `cd viewer && npx vitest run tests/catalog.test.ts tests/api.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check && npm run build:hosted`; `docker build -f web/Dockerfile --build-arg TABLETOP_BLOB_ORIGIN=http://127.0.0.1:9000 -t tabletop-web .`
  succeeds.
- [ ] Commit: `Viewer: the built-in terrain catalog with the packed fallback; layout, random-table and library calls`.

### Task 3: Setup limits, edits and the terrain card

**Files:** create `viewer/src/lib/setup/limits.ts`, `viewer/src/lib/setup/edits.ts`, `viewer/src/lib/setup/terrainCard.ts`;
test `viewer/tests/setupLimits.test.ts`, `viewer/tests/setupEdits.test.ts`, `viewer/tests/terrainCard.test.ts`.

**Interfaces:**

```ts
// limits.ts — SetupLimits and TextLimits, word for word
export const TERRAIN = 64, MARKERS = 32, MIN_EDGE = 12, MAX_EDGE = 144, LABEL = 64, NOTES = 500;   // names: C1's api/names.ts
export const SIZE_HINT: string;                                     // 'Width × length, 12–144" a side'
export function terrainHeading(d: TableState | null): string;       // 'Terrain (3/64)'
export function markersHeading(d: TableState | null): string;
export function cannotAddTerrain(d: TableState | null, adding?: number): string | null;
export function cannotClearTerrain(d: TableState | null): string | null;
export function cannotAddMarker(d: TableState | null): string | null;
export function freeSpot(size: TableSize, markers: readonly Marker[], diameter: number): Point3;
/** SetupToolbar.Beside: x = W + 4 + 8·{0,1,2}, y = 4 + 8·{0..3}, z 0; `random` in [0, 1). */
export function beside(size: TableSize, random?: () => number): Point3;
// edits.ts — the setup edits Godot's toolbar, card, menu and drops send
export const TERRAIN_TURN = 15, SCALE_MIN = 0.25, SCALE_MAX = 4, SCALE_STEP = 0.25;
export const ZONE_STYLES: readonly string[];                        // ZoneStyles.All, in order
export function zoneShapes(style: string): string[];                // ZoneStyles.ShapesOf: 'style/1'… (4 for free-for-all)
export function objective(size: TableSize, markers: readonly Marker[]): AddSetupMarker;   // diameter 3, objective-ring
export function token(size: TableSize, markers: readonly Marker[]): AddSetupMarker;       // diameter 1, label 'token', chip
/** MenuModel.TurnInDraft over Geometry.turnPiece: facing kept in 0–360; null for no such piece or a non-finite angle. */
export function turnInDraft(g: Geometry, d: TableState, pieceId: string, degrees: number): MoveTerrain | null;
export function removeFromDraft(d: TableState, id: string): RemoveTerrain | RemoveSetupMarker | null;
export interface MenuItem { readonly label: string; readonly input: 'none' | 'integer'; readonly edit: (text: string) => Edit | null }
/** MenuModel.ForSetupPiece: terrain Turn left 15°, Turn right 15°, Set facing…, then Remove; a marker Remove; else []. */
export function setupMenu(g: Geometry, d: TableState, id: string): MenuItem[];
/** TerrainCard.RenderVolumeEdits: the whole volume list with one volume changed. */
export function overrideVolume(piece: TerrainPlacement, volumeId: string, change: (v: Volume) => Volume): OverrideTerrainVolumes;
export function withNotes(text: string): (v: Volume) => Volume;     // clipped to 500; '' clears
export function withDeclaration(name: string, on: boolean): (v: Volume) => Volume;
export function withFootprint(field: 'a' | 'b', value: number): (v: Volume) => Volume;   // a circle sets a and b
export function clearTerrain(d: TableState): ReplaceDraft;
// terrainCard.ts — TerrainCardContent, MarkerCardContent, TerrainLabel
export interface CardVolume { readonly id: string; readonly kind: string; readonly footprint: string; readonly heights: string;
  readonly declarations: readonly string[]; readonly notes: string | null }
export interface TerrainCard { readonly pieceId: string; readonly name: string; readonly source: string; readonly facing: number;
  readonly scale: number; readonly height: string; readonly volumes: readonly CardVolume[]; readonly editable: boolean }
export interface MarkerCard { readonly pieceId: string; readonly name: string; readonly diameter: string; readonly label: string | null }
export const DECLARATION_WORDS: readonly (readonly [string, string])[];   // TerrainLabel order: ['cover','Cover'] … ['see_in_and_out','See in/out']
export function inches(v: number): string;                               // '0.#'
export function facingOf(yaw: Yaw): number;
export function terrainCardOf(t: TableState | null, status: GameStatus | null, selected: string | null, catalog: Catalog): TerrainCard | null;
export function markerCardOf(t: TableState | null, status: GameStatus | null, selected: string | null): MarkerCard | null;
```

- [ ] Tests first, each from Godot's own tests or code: `headings count against the caps`; `cannotAddTerrain at 64 and for a batch` (Godot's three texts); `freeSpot takes the
  centre, then the first free ring spot` (a 3" marker at the centre → (42, 24) on 72×48); `beside stays right of the table`;
  `zoneShapes gives two, four for free-for-all`; `objective and token build Godot's markers`; `turnInDraft wraps the facing and
  asks turnPiece` (a fake `Geometry` records `turnPiece(terrain, id, { degrees: 345 })` for −15); `setupMenu for terrain, a
  marker, and an unknown id`; `overrideVolume changes only that volume`; `volume notes are clipped to 500 and empty notes
  clear`; `a circle footprint edit sets both sizes`; `clearTerrain keeps markers and zones`; `the card shows facing 0–359,
  heights and Open for no declaration` (yaw −15 → 345; volume 0–3 at z 1, scale 2 → `1–7"`); `the card is editable in setup
  only`; `a marker card is setup only`.
- [ ] `cd viewer && npx vitest run tests/setupLimits.test.ts tests/setupEdits.test.ts tests/terrainCard.test.ts` fails.
- [ ] Implement (`Geometry` and its types from `../geometry`, type-only in `edits.ts` apart from the calls).
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: setup limits, edits and the terrain card, ported from Godot`.

### Task 4: The draft as a board scene

**Files:** create `viewer/src/lib/board/table.ts`; test `viewer/tests/table.test.ts`.

**Interfaces:**

```ts
// table.ts — no geometry import: zone polygons come in as a function (Geometry.zonePolygons in setup)
export type ZonePolygons = (shape: string, size: TableSize) => readonly (readonly Point[])[];
/** A TableState as a Scene: terrain outlines (frame.ts volumeOutline's arithmetic over wire placements) labelled
 *  with `nameOf`, markers, zones resolved by `zones`, no models. */
export function sceneOfTable(table: TableState, zones: ZonePolygons, name: (piece: TerrainPlacement) => string): Scene;
/** The box fitBox shows: the table, widened to every terrain outline and marker beside it. */
export function extentOf(scene: Scene): { minX: number; minY: number; maxX: number; maxY: number };
/** One terrain piece's outlines at a placement other than its own: the drag's ghost. */
export function pieceOutlines(piece: TerrainPlacement, at: Placement): Shape[];
export const ghostOverlay: (shapes: readonly Shape[], colour: string) => Overlay;
```

- [ ] Tests first: `a draft becomes terrain, markers and zones` (one 6×6 rect at (10, 10) yaw 90 → a polygon whose corners
  are (7, 7)…(13, 13); one objective → radius 1.5; zone `frontline/1` with a fake `zones` → its polygon); `declarations read
  from the wire string`; `extentOf widens to a piece beside the table` (piece at (80, 8) on 72×48 → `maxX` ≥ 83);
  `pieceOutlines moves and turns the footprint` (the 6×1 piece at (20, 20) yaw 90 → corners (19.5, 17)…(20.5, 23)).
- [ ] `cd viewer && npx vitest run tests/table.test.ts` fails.
- [ ] Implement, sharing `volumeOutline`'s arithmetic with `frame.ts` (extract the wire-shaped core both call).
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: the setup draft as a board scene, its extent and drag ghosts`.

### Task 5: Solid setup drags

**Files:** create `viewer/src/lib/setup/drag.ts`; test `viewer/tests/setupDrag.test.ts` (a fake `Geometry`),
`viewer/tests/setupDrag.wasm.test.ts` (plan B's `loadTestGeometry()`).

**Interfaces:**

```ts
// drag.ts — TableInput's setup drag: a terrain piece lifted to the cursor and landed by Geometry.landPiece; a marker on the table plane
export type Held =
  | { readonly kind: 'terrain'; readonly id: string; readonly offset: XY; readonly yaw: Yaw; readonly end: PieceEnd }
  | { readonly kind: 'marker'; readonly id: string; readonly offset: XY; readonly yaw: Yaw; readonly at: Point3 };
export class SetupDrag {
  constructor(geometry: Geometry);
  get held(): Held | null;
  /** Picks up the hit piece; the offset keeps the grab point under the cursor. False for a model or an unknown id. */
  start(draft: TableState, hit: BoardHit, at: XY): boolean;
  /** Recomputes against `draft` (the latest board): terrain → landPiece(draft.terrain, id, { position, yaw }, cursor − offset),
   *  marker → (cursor − offset, z 0). Cancels when the piece left the draft; returns the held state or null. */
  move(draft: TableState, at: XY): Held | null;
  /** Q/E while held: ±15° (Shift ±30°), counter-clockwise positive, then re-lands at the last cursor. */
  turn(draft: TableState, degrees: number): Held | null;
  /** The drop: MoveTerrain(id, end.lands, yaw) / MoveSetupMarker(id, at, yaw); null when nothing is held or it did not move. */
  end(draft: TableState): MoveTerrain | MoveSetupMarker | null;
  cancel(): void;
}
```

The board's terrain is `draft.terrain` as the latest `board` frame has it, so a drag lands against what another player just
moved. The ghost overlay draws `pieceOutlines(piece, { position: end.drawn, yaw })` see-through and a ring at `end.lands`
(Godot's `LandingMarks`). Turning a piece in place (menu, card, keys without a drag) is `turnInDraft` (Task 3), which calls
`turnPiece`. The browser always drops lifted (spec → Heights): no level keys, no `restPiece` call in setup.

- [ ] Tests first (fake `Geometry` returning scripted `PieceEnd`s and recording calls): `start takes terrain and markers, not
  models`; `move lands through landPiece with the grab offset`; `turn re-lands at the new yaw`; `the drop sends MoveTerrain at
  the landing`; `a marker drops on the table plane with MoveSetupMarker`; `a still drop sends nothing`; `a held piece that leaves
  the draft cancels the drag`; `the drop lands against the draft current at the drop` (move with draft A, end with draft B
  where another piece moved → the last `landPiece` call saw B's terrain). WASM test: `a piece dropped onto an Impassable
  solid lands clear of it` (two 4×4 Impassable solids; dropping the second's centre on the first's → `lands` at least 4"
  from it in x or y, z 0).
- [ ] `./scripts/geometry-wasm && cd viewer && npx vitest run tests/setupDrag.test.ts tests/setupDrag.wasm.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check` (plan B's lazy test still passes: nothing under `components` imports `setup/drag.ts`).
- [ ] Commit: `Viewer: solid setup drags through Geometry.landPiece, turns through turnPiece`.

### Task 6: Layouts, predefined tables and the random table

**Files:** create `viewer/src/lib/setup/layouts.ts`; test `viewer/tests/layouts.test.ts`.

**Interfaces:**

```ts
// layouts.ts
export const PLANETS: readonly { readonly planet: Planet; readonly label: string }[];   // Generic, Hospitable, Wasteland, Death, Ruins planet
/** Load (a saved layout or a predefined table): the whole draft, size included. */
export const loadEdit = (layout: TableState): ReplaceDraft;
/** Plan A's request for the current draft: its size and zone rows (left out when there are none); no seed. */
export function randomRequest(draft: TableState, planet: Planet): RandomTableRequest;
/** The random table's edit: the draft with its terrain replaced; size, texture, markers and zones kept. */
export function randomEdit(draft: TableState, answer: RandomTable): ReplaceDraft;
/** The line after a random table: "Placed n pieces." plus "m did not fit: …" (unplaced) and "Not in the catalog: …" (unlisted). */
export function randomSummary(answer: RandomTable): string;
/** Save as: the draft named (nameProblem first); management stores it as the account's layout. */
export function saveRequest(draft: TableState, name: string): { table: TableState } | { problem: string };
/** A layout row's text, Godot's: "Name (72×48, 12 terrain)". */
export function layoutLabel(l: LayoutSummary): string;
```

- [ ] Tests first: `loading a layout replaces the whole draft, size included`; `randomRequest carries size and zones`;
  `randomRequest leaves zones out when the draft has none`; `randomEdit replaces only the terrain` (texture, markers, zones and
  size equal the draft's); `randomSummary names what did not fit`; `saveRequest refuses a blank name with Godot's text and trims a good one`;
  `layoutLabel`.
- [ ] `cd viewer && npx vitest run tests/layouts.test.ts` fails.
- [ ] Implement.
- [ ] `npm test && npm run check`.
- [ ] Commit: `Viewer: layout loading and saving, predefined tables and the random table as ReplaceDraft`.

### Task 7: The setup page

**Files:** create `viewer/src/lib/setup/SetupPage.svelte`, `viewer/src/lib/setup/SetupToolbar.svelte`,
`viewer/src/lib/setup/TerrainCard.svelte`, `viewer/src/lib/setup/PieceMenu.svelte`, `viewer/src/lib/setup/LayoutsSection.svelte`;
modify `viewer/src/lib/game/GamePage.svelte` (the `setup` branch).

**Interfaces:**

- Consumes: `GameConnection` (C1 Task 6), `Board`, `BoardInteraction`, `Overlay`, `View` (C1 Task 7), `loadGeometry`/`Geometry`
  (plan B Task 5), Tasks 2–6 here.
- Produces: `SetupPage` props `{ connection: GameConnection; api: Api; view: View | null (bindable); creator: boolean }`.

`SetupPage` loads the geometry and the catalog on mount ("Loading the table tools…" until both are in; drags and turns wait
for them), builds `sceneOfTable(draft, geometry.zonePolygons, nameOf)` from each `draft`, fits `extentOf`, and drives the
board: `onSelect` selects a terrain piece or marker (the card); `onDragStart` → `SetupDrag.start`, `onDragMove` → `move` (the
ghost overlay), `onDragEnd` → `end` then `sendEdit`; `onKey`: `q`/`e` turn a held piece (`turn`, ±15°, Shift ±30°) or the
selected one in place (`turnInDraft`), `Delete`/`Backspace` send `removeFromDraft` for the selection, Escape cancels;
`onContextMenu` opens `PieceMenu` over `setupMenu` ("Set facing…" asks for an integer). `SetupToolbar` holds Godot's sections
under collapsible headings that count (`terrainHeading`, `markersHeading`): **Table** (width and length, 12–144, Set size;
texture select over the catalog's textures, showing the draft's), **Terrain** (a catalog select by label and Add built-in
terrain (beside the table) via `placeBuiltin(entry, beside(size), { degrees: 0 })`; Add my terrain… over `terrainAssets` and
`assetVolumes`; a planet select and Random table; Clear terrain, asked first: "All n terrain pieces come off the table.
Markers and zones stay."), each add disabled with its `cannot…` reason as the tooltip; **Markers** (Add objective, Add token);
**Zones** (a style select and Set zones; two team numbers 1–8 and Swap); **Layouts** (`LayoutsSection`: Predefined tables from
`builtinLayouts` with Load; My layouts from `listLayouts` with Load and Delete (asked first: "Delete the layout <name>?"); a
name field and Save as; every load asked first: "Loading replaces the whole table, size included."). `TerrainCard` shows
`terrainCardOf`/`markerCardOf`: name, source, height; Facing (0–359, ⟲/⟳ 15°, through `turnInDraft`), Scale (0.25–4, step
0.25, `ScaleTerrain`), swap mesh (a built-in select, "Swap mesh (keeps volumes)"; "Swap for my asset…"), Reset volumes (the
built-in's volume or the asset's, new ids), Remove; per volume kind, footprint (radius or w×l), from/to, the seven declaration
checkboxes in `DECLARATION_WORDS` order and notes (Enter saves, 500 characters); a marker's Remove marker. Every outcome with
`refused` shows `refusalText` in the page's alert line.

- [ ] `npm run check` passes and `npm test` stays green.
- [ ] `npm run build:hosted` passes; the socket path is validated in Task 8.
- [ ] Commit: `Viewer: the setup page — table, terrain, markers, zones, layouts, random and predefined tables`.

### Task 8: Validation against the stack, and the docs

**Files:** create `viewer/tests/e2e/setup.e2e.test.ts`; modify `viewer/package.json` (`"test:e2e": "TABLETOP_E2E=1 vitest run
tests/e2e"`), `viewer/AGENTS.md`, `docs/design/viewer/design.md`, `web/AGENTS.md`.

**Interfaces:**

- Consumes: `connectGame` (C1), `apiOf`, `loadCatalog`, `placeBuiltin`, `SetupDrag`, `randomRequest`/`randomEdit`, `loadEdit`,
  `liveFeed` with `watchUrl`, plan B's `loadTestGeometry`. Node 24's global `WebSocket` is the socket.
- The suite skips unless `TABLETOP_E2E=1` (`describe.skipIf`), so `npm test` stays offline.

The test signs in three dev accounts with `POST http://127.0.0.1:5100/v1/dev/sessions { name }` (`sessionJwt`), runs every step
through the same modules the pages use, and asserts on what the server sends back:

- [ ] Write `setup.e2e.test.ts`: `a game goes from lobby to playing from the browser modules` —
  A `createGame` (first rule set, alternating, warn), B `joinGame`; both `connectGame` on `ws://127.0.0.1:5200/ws` and reach
  `connected`; A sends `CreateTeam` until the game has two teams (a new game may have none); A and B each `PickTeam` a
  different team (each settles `refused: null`; `game.participants` show the teams);
  B `StartSetup` → `refused` reason `not_permitted`; A `StartSetup` → both see `status: 'setup'` and a draft;
  A `SetTableSize 60×44` → B's draft is 60×44; A adds two `placeBuiltin('large-sight-blocker')` pieces beside the table, drags
  the second onto the first through `SetupDrag` with the real geometry and sends the drop → both drafts hold it at a landing
  clear of the first; A `ScaleTerrain 2`, `OverrideTerrainVolumes` with `difficult` added, `AddSetupMarker` (objective),
  `SetZoneStyle('spearhead', …)` → B's draft agrees on each; A `randomTable` (hospitable) and `randomEdit` → B's draft has that
  terrain and still the objective and spearhead zones; A `saveLayout` named `e2e` then `deleteLayout` → `listLayouts` lacks it;
  A loads `builtinLayouts()[2]` (Death) → both drafts are 72×48 with four objectives; B `EndSetup` → `not_permitted`; A
  `EndSetup` → both see `status: 'playing'`. C (no participant) `connectGame` → `closed` with "You are not in that game: join it
  from the list."; B opens `liveFeed` on `watchUrl` → status `open` while B's play connection stays `connected`.
- [ ] Run `docker compose up -d`, `./scripts/geometry-wasm`, then `cd viewer && npm run test:e2e`: passes. Fix what fails in the
  module at fault, never by loosening the assertion.
- [ ] Docs. `viewer/AGENTS.md`: Layout gains `src/lib/shell/`, `api/`, `games/`, `connection/`, `lobby/`, `game/`, `setup/`,
  `board/interaction.ts`, `pointer.ts`, `table.ts`; How it works: the hosted build opens to every signed-in player (admins also
  get Watch), the routes, play beside playback (`GamePage` over `GameConnection`, `PlaybackView` over a `Feed`), replays on
  `?watch=1`, the management calls the viewer now makes (games, armies, account, assets, layouts, builtin, random), the packed
  catalog fallback; the *Playback only* paragraph becomes the connection's rules (sends, refusals, queue of 16, reconnect 1/2/5/10
  s, `Reauth` every 60 s, one resync per log position); Commands gain `npm run test:e2e`. `docs/design/viewer/design.md`: the
  same structure, current state only. `web/AGENTS.md`: the Replays link shows for every signed-in reader; `isAdmin` gone.
  Grep for `admin-only`, `Not authorised`, `Playback only`, `sends no command` across `viewer/`, `web/`, `docs/design/` and fix
  each.
- [ ] `cd viewer && npm test && npm run check && npm run build:hosted`; `cd web && npm test && npm run check`;
  `dotnet test tests/Tabletop.Viewer.Tests`.
- [ ] Commit: `Viewer: lobby-to-playing validated over the socket from the browser modules; docs`.
