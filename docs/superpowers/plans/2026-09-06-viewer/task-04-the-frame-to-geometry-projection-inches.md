# Task 4: The frame-to-geometry projection — inches to shapes, and the view transform

**Files:**
- Create: `viewer/src/lib/board/transform.ts`, `viewer/src/lib/board/palette.ts`, `viewer/src/lib/board/frame.ts`
- Test: `viewer/tests/transform.test.ts`, `viewer/tests/frame.test.ts`

**Interfaces:**
- Consumes: `RecordedBoard`, `RecordedFrame`, `RecordedModel`, `RecordedModelState`, `RecordedTerrain`, `RecordedVolume`, `RecordedZone`, `RecordedMarkerState`, `RecordedUnitState` (Task 2). Not `RecordedMarker`: since the marker state carries its own definition, `sceneOf` reads `frame.markers` and never `board.markers`.
- Produces (`transform.ts`): `type Point = { readonly x: number; readonly y: number }`; `type Table = { readonly width: number; readonly length: number }`; `type Viewport = { readonly width: number; readonly height: number }`; `type View = { readonly scale: number; readonly panX: number; readonly panY: number }`; `const MARGIN = 12`; `function fit(table: Table, viewport: Viewport): View`; `function toScreen(view: View, table: Table, p: Point): Point`; `function toTable(view: View, table: Table, p: Point): Point`; `function zoomAt(view: View, table: Table, screen: Point, factor: number): View`; `function panBy(view: View, dx: number, dy: number): View`; `function gridStep(view: View): number`; `function rotate(p: Point, yawDegrees: number): Point`.
- Produces (`palette.ts`): `const TEAM_COLOURS: readonly string[]`; `function teamColour(ordinal: number): string`; `const DECLARATIONS: readonly string[]` (the closed six); `function declarationColour(declaration: string): string`; `function terrainStroke(declarations: readonly string[]): string`; `function markerColour(kind: string): string`.
- Produces (`frame.ts`): `type Shape = { kind: 'circle'; centre: Point; radius: number } | { kind: 'polygon'; points: readonly Point[] }`; `const OVAL_VERTICES = 64`; `type ModelShape`, `type TerrainShape`, `type MarkerShape`, `type ZoneShape`, `type OffTable`, `type Scene` as declared below; `function sceneOf(board: RecordedBoard, frame: RecordedFrame): Scene`; `function baseOutline(model: RecordedModel, centre: Point, yaw: number): Shape`; `function volumeOutline(terrain: RecordedTerrain, volume: RecordedVolume): Shape`.
- Ported, not invented: `baseOutline` is `Tabletop.Geometry.Bodies.BaseOutline` (geometry plan Task 3) and `volumeOutline` is `WorldVolumes.WorldOutline` (same task), line for line, including `OvalVertices = 64` and `Point2.Rotate`'s "yaw 0 faces +Y, positive yaw counter-clockwise seen from above". These are the only two pieces of `Tabletop.Geometry` the viewer needs, and it needs them because a canvas draws outlines.

Spec: spec → *`viewer/` is a developer web app* — a grid in inches, each model's base outline (circle, oval or rect) coloured by team with its facing, terrain as outlines carrying their declarations, deployment zones; "a model is its base and its facing". Spec → *Testing* — "the frame-to-geometry projection".

**Deployment zones.** `RecordedZone` carries `shape` (a shape id such as `frontline/1`), `teamOrdinal` and `polygons` — an array of rings, each a list of `RecordedPoint` `{x, y}`, already resolved by `Tabletop.Geometry.ZoneStyles.Resolve` and already in the corner frame, so the viewer converts nothing and resolves nothing. That is why the field exists: resolving a shape id is C#, and OPR's frontline is an absolute twelve-inch inset rather than a fraction of the table, so a browser cannot recover it from the id and the table size, and porting `ZoneStyles` would be a second geometry implementation. `sceneOf` reads `zone.polygons` directly and wraps each ring as a `Shape`. A ring list can still be **empty** — `ZoneStyles.Resolve` answers an empty list for a shape id it does not know, and the live feed has no resolver at all (Task 9) — so an empty `polygons` draws no outline and is not an error; the zone panel lists shape id → team ordinal either way.

- [ ] **Step 1: Write `viewer/tests/transform.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { MARGIN, fit, gridStep, panBy, rotate, toScreen, toTable, zoomAt } from '../src/lib/board/transform';

const opr = { width: 72, length: 48 };

describe('the view transform', () => {
  it('fits the table into the viewport with a margin and centres it', () => {
    const view = fit(opr, { width: 720 + 2 * MARGIN, height: 480 + 2 * MARGIN });
    expect(view.scale).toBeCloseTo(10);
    expect(view.panX).toBeCloseTo(MARGIN);
    expect(view.panY).toBeCloseTo(MARGIN);
  });

  it('flips y exactly once, so the table origin is the bottom-left corner on screen', () => {
    const view = fit(opr, { width: 720 + 2 * MARGIN, height: 480 + 2 * MARGIN });
    expect(toScreen(view, opr, { x: 0, y: 0 })).toEqual({ x: MARGIN, y: MARGIN + 480 });
    expect(toScreen(view, opr, { x: 72, y: 48 })).toEqual({ x: MARGIN + 720, y: MARGIN });
  });

  it('round-trips a point through screen space', () => {
    const view = panBy(zoomAt(fit(opr, { width: 800, height: 600 }), opr, { x: 400, y: 300 }, 2.5), 17, -9);
    const back = toTable(view, opr, toScreen(view, opr, { x: 13.5, y: 41.25 }));
    expect(back.x).toBeCloseTo(13.5);
    expect(back.y).toBeCloseTo(41.25);
  });

  it('zooms about the pointer, leaving the table point under it fixed', () => {
    const view = fit(opr, { width: 800, height: 600 });
    const cursor = { x: 300, y: 200 };
    const before = toTable(view, opr, cursor);
    const after = toTable(zoomAt(view, opr, cursor, 3), opr, cursor);
    expect(after.x).toBeCloseTo(before.x);
    expect(after.y).toBeCloseTo(before.y);
  });

  it('coarsens the grid as the table shrinks so the lines never crowd', () => {
    expect(gridStep({ scale: 20, panX: 0, panY: 0 })).toBe(1);
    expect(gridStep({ scale: 6, panX: 0, panY: 0 })).toBe(3);
    expect(gridStep({ scale: 3, panX: 0, panY: 0 })).toBe(6);
    expect(gridStep({ scale: 0.5, panX: 0, panY: 0 })).toBe(12);
  });

  it('rotates counter-clockwise from +y, as Point2.Rotate does', () => {
    const turned = rotate({ x: 0, y: 1 }, 90);
    expect(turned.x).toBeCloseTo(-1);
    expect(turned.y).toBeCloseTo(0);
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `transform.ts`**

```ts
// viewer/src/lib/board/transform.ts
export type Point = { readonly x: number; readonly y: number };
export type Table = { readonly width: number; readonly length: number };
export type Viewport = { readonly width: number; readonly height: number };
/** Pixels per inch, and the screen position of the table's top-left corner (x = 0, y = length). */
export type View = { readonly scale: number; readonly panX: number; readonly panY: number };

export const MARGIN = 12;

export function fit(table: Table, viewport: Viewport): View {
  const usableWidth = Math.max(1, viewport.width - 2 * MARGIN);
  const usableHeight = Math.max(1, viewport.height - 2 * MARGIN);
  const scale = Math.min(usableWidth / table.width, usableHeight / table.length);
  return {
    scale,
    panX: (viewport.width - table.width * scale) / 2,
    panY: (viewport.height - table.length * scale) / 2,
  };
}

/** The only place y is flipped: the table's y grows away from the near edge, the canvas's grows downward. */
export function toScreen(view: View, table: Table, p: Point): Point {
  return { x: view.panX + p.x * view.scale, y: view.panY + (table.length - p.y) * view.scale };
}

export function toTable(view: View, table: Table, p: Point): Point {
  return { x: (p.x - view.panX) / view.scale, y: table.length - (p.y - view.panY) / view.scale };
}

export function zoomAt(view: View, table: Table, screen: Point, factor: number): View {
  const scale = Math.min(400, Math.max(0.25, view.scale * factor));
  const anchor = toTable(view, table, screen);
  return {
    scale,
    panX: screen.x - anchor.x * scale,
    panY: screen.y - (table.length - anchor.y) * scale,
  };
}

export function panBy(view: View, dx: number, dy: number): View {
  return { scale: view.scale, panX: view.panX + dx, panY: view.panY + dy };
}

/** Inches between grid lines: the smallest of 1, 3, 6, 12 that keeps lines at least 12 px apart. */
export function gridStep(view: View): number {
  for (const step of [1, 3, 6, 12]) if (step * view.scale >= 12) return step;
  return 12;
}

/** Tabletop.Geometry.Point2.Rotate: about the origin; yaw 0 faces +y, positive yaw turns counter-clockwise. */
export function rotate(p: Point, yawDegrees: number): Point {
  const radians = (yawDegrees * Math.PI) / 180;
  const s = Math.sin(radians);
  const c = Math.cos(radians);
  return { x: p.x * c - p.y * s, y: p.x * s + p.y * c };
}
```

- [ ] **Step 3: Write `palette.ts`**

```ts
// viewer/src/lib/board/palette.ts

/** One per team ordinal, 1-based; ordinals past the end wrap. */
export const TEAM_COLOURS = ['#4ea1ff', '#ff6b5c', '#ffc44e', '#6ddf7c', '#c98cff', '#4ee0d0'] as const;

export function teamColour(ordinal: number): string {
  const colours = TEAM_COLOURS;
  const index = ((ordinal - 1) % colours.length + colours.length) % colours.length;
  return colours[index]!;
}

/** The closed six (setup content → Terrain declarations), snake_case as the recording spells them. */
export const DECLARATIONS = [
  'impassable', 'blocks_line_of_sight', 'see_in_and_out', 'cover', 'difficult', 'dangerous',
] as const;

const DECLARATION_COLOURS: Record<string, string> = {
  impassable: '#ff6b5c',
  blocks_line_of_sight: '#c98cff',
  see_in_and_out: '#8ab4ff',
  cover: '#6ddf7c',
  difficult: '#ffc44e',
  dangerous: '#ff9d4e',
};

export function declarationColour(declaration: string): string {
  return DECLARATION_COLOURS[declaration] ?? '#8b93a1';
}

/** A volume takes the colour of its first declaration; a volume with none is drawn grey. */
export function terrainStroke(declarations: readonly string[]): string {
  return declarations.length === 0 ? '#8b93a1' : declarationColour(declarations[0]!);
}

export function markerColour(kind: string): string {
  return kind === 'objective' ? '#f2f4f8' : '#8b93a1';
}
```

- [ ] **Step 4: Write `viewer/tests/frame.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { OVAL_VERTICES, sceneOf } from '../src/lib/board/frame';
import type { RecordedBoard, RecordedFrame } from '../src/lib/protocol/recording.generated';

const board: RecordedBoard = {
  width: 72, length: 48, texture: 'grass',
  terrain: [{
    id: 'tp1', mesh: 'ruin', x: 20, y: 10, z: 0, yaw: 90, scale: 2,
    volumes: [
      { id: 'v1', kind: 'solid', footprint: 'rect', a: 3, b: 1, offsetX: 1, offsetY: 0, yaw: 0, bottom: 0, top: 4, declarations: ['impassable', 'blocks_line_of_sight'] },
      { id: 'v2', kind: 'area', footprint: 'circle', a: 2, b: 2, offsetX: 0, offsetY: 0, yaw: 0, bottom: 0, top: 6, declarations: ['cover', 'difficult'] },
    ],
  }],
  markers: [{ id: 'k1', kind: 'objective', diameter: 2, label: 'A' }],
  // frontline/1 as ZoneStyles.Resolve returns it: the corner frame, four vertices, twelve inches deep.
  zones: [{ id: 'z1', shape: 'frontline/1', teamOrdinal: 1,
    polygons: [[{ x: 0, y: 0 }, { x: 72, y: 0 }, { x: 72, y: 12 }, { x: 0, y: 12 }]] }],
  models: [
    { id: 'm1', unitId: 'u1', unitName: 'Rifles', name: 'Rifleman', ordinal: 1, teamId: 't1', teamOrdinal: 1, baseShape: 'circle', baseA: 1, baseB: 1, height: 1.2 },
    { id: 'm2', unitId: 'u1', unitName: 'Rifles', name: 'Sergeant', ordinal: 2, teamId: 't1', teamOrdinal: 1, baseShape: 'oval', baseA: 3, baseB: 2, height: 2 },
    { id: 'm3', unitId: 'u2', unitName: 'Walker', name: 'Walker', ordinal: 1, teamId: 't2', teamOrdinal: 2, baseShape: 'rect', baseA: 2, baseB: 2, height: 4 },
  ],
};

const frame: RecordedFrame = {
  index: 0, seq: 9, kind: 'models_moved', round: 1, phase: 'rounds', line: '3 model(s) moved',
  models: [
    { id: 'm1', where: 'table', x: 10, y: 20, z: 0, yaw: 0, wounds: 1, spellTokens: 0, conditions: ['shaken'] },
    { id: 'm2', where: 'table', x: 12, y: 20, z: 2, yaw: 90, wounds: 0, spellTokens: 2, conditions: [] },
    { id: 'm3', where: 'inside', containerId: 'm2', wounds: 0, spellTokens: 0, conditions: [] },
  ],
  units: [
    { id: 'u1', actions: ['Advance'], conditions: [], grants: [], woundsThisActivation: 1 },
    { id: 'u2', actions: [], conditions: ['shaken'], grants: ['Regeneration'], woundsThisActivation: 0 },
  ],
  // k1 is also in board.markers and agrees with it; k9 was dropped during play and is in no board block.
  markers: [
    { id: 'k1', kind: 'objective', diameter: 2, label: 'A', x: 36, y: 24, z: 0, yaw: 0 },
    { id: 'k9', kind: 'token', diameter: 1, x: 4, y: 4, z: 0, yaw: 0 },
  ],
  openAsks: [], rolls: [], scores: { t1: 3, t2: 0 }, passed: ['t2'],
};

describe('sceneOf', () => {
  it('draws a circle base as a circle of half the recorded diameter', () => {
    const model = sceneOf(board, frame).models.find((m) => m.id === 'm1')!;
    expect(model.shape).toEqual({ kind: 'circle', centre: { x: 10, y: 20 }, radius: 0.5 });
    expect(model.teamOrdinal).toBe(1);
    expect(model.wounds).toBe(1);
    expect(model.conditions).toEqual(['shaken']);
  });

  it('draws an oval as a 64-gon with its length along the facing axis', () => {
    const oval = sceneOf(board, frame).models.find((m) => m.id === 'm2')!;
    expect(oval.shape.kind).toBe('polygon');
    const points = (oval.shape as { points: readonly { x: number; y: number }[] }).points;
    expect(points).toHaveLength(OVAL_VERTICES);
    // yaw 90 turns the 3-inch length onto -x, so the extreme x is 1.5 from the centre.
    expect(Math.min(...points.map((p) => p.x))).toBeCloseTo(12 - 1.5);
    expect(Math.min(...points.map((p) => p.y))).toBeCloseTo(20 - 1);
  });

  it('gives every table model a facing tick half the base length long', () => {
    const model = sceneOf(board, frame).models.find((m) => m.id === 'm1')!;
    expect(model.facing).not.toBeNull();
    expect(model.facing![1]).toEqual({ x: 10, y: 20.5 });
    expect(sceneOf(board, frame).models.find((m) => m.id === 'm2')!.facing![1].x).toBeCloseTo(12 - 1.5);
  });

  it('leaves a model with no position off the table and says where it is', () => {
    const scene = sceneOf(board, frame);
    expect(scene.models.map((m) => m.id)).toEqual(['m1', 'm2']);
    expect(scene.offTable).toEqual([
      { id: 'm3', label: 'Walker', teamOrdinal: 2, where: 'inside', containerLabel: 'Sergeant' },
    ]);
  });

  it('marks a model standing above the slab as elevated', () => {
    const scene = sceneOf(board, frame);
    expect(scene.models.find((m) => m.id === 'm1')!.elevated).toBe(false);
    expect(scene.models.find((m) => m.id === 'm2')!.elevated).toBe(true);
  });

  it('places a terrain volume by the piece transform, exactly as WorldVolumes does', () => {
    const [solid, area] = sceneOf(board, frame).terrain;
    // centre = position + rotate(offset * scale, pieceYaw); yaw 90 sends +x to +y.
    expect(solid!.shape.kind).toBe('polygon');
    const corners = (solid!.shape as { points: readonly { x: number; y: number }[] }).points;
    // offset (1, 0) * scale 2, rotated by the piece's yaw 90, lands at (0, 2), so the centre is (20, 12).
    // The 3x1 footprint at scale 2 is 6 by 2 locally; yaw 90 turns (x, y) into (-y, x), so the local
    // corners (-3,-1), (3,-1), (3,1), (-3,1) become (1,-3), (1,3), (-1,3), (-1,-3) about the centre.
    const expected = [{ x: 21, y: 9 }, { x: 21, y: 15 }, { x: 19, y: 15 }, { x: 19, y: 9 }];
    corners.forEach((corner, i) => {
      expect(corner.x).toBeCloseTo(expected[i]!.x, 6);
      expect(corner.y).toBeCloseTo(expected[i]!.y, 6);
    });
    expect(solid!.declarations).toEqual(['impassable', 'blocks_line_of_sight']);
    expect(solid!.top).toBe(8);            // top * scale
    expect(area!.shape).toEqual({ kind: 'circle', centre: { x: 20, y: 10 }, radius: 4 });
    expect(area!.label).toBe('ruin');
  });

  it('wraps a zone ring as a polygon in table inches, and tolerates a zone with no rings', () => {
    expect(sceneOf(board, frame).zones).toEqual([{
      id: 'z1', shape: 'frontline/1', teamOrdinal: 1,
      polygons: [{ kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 72, y: 0 }, { x: 72, y: 12 }, { x: 0, y: 12 }] }],
    }]);
    // ZoneStyles.Resolve answers [] for a shape id it does not know, and the live feed resolves nothing.
    const unresolved = { ...board, zones: [{ ...board.zones[0]!, shape: 'made-up/1', polygons: [] }] };
    expect(sceneOf(unresolved, frame).zones[0]!.polygons).toEqual([]);
  });

  it('draws every marker from its own kind, diameter and label, falling back to the id when unlabelled', () => {
    const markers = sceneOf(board, frame).markers;
    expect(markers[0]).toEqual({ id: 'k1', kind: 'objective', label: 'A', centre: { x: 36, y: 24 }, radius: 1 });
    // k9 is in no board block; the frame carries its whole definition, so nothing is guessed.
    expect(markers[1]).toEqual({ id: 'k9', kind: 'token', label: 'k9', centre: { x: 4, y: 4 }, radius: 0.5 });
  });
});
```

- [ ] **Step 5: Write `frame.ts`**

```ts
// viewer/src/lib/board/frame.ts
import type {
  RecordedBoard, RecordedFrame, RecordedModel, RecordedTerrain, RecordedVolume,
} from '../protocol/recording.generated';
import { type Point, rotate } from './transform';

export type Shape =
  | { readonly kind: 'circle'; readonly centre: Point; readonly radius: number }
  | { readonly kind: 'polygon'; readonly points: readonly Point[] };

/** Tabletop.Geometry.Bodies.OvalVertices. */
export const OVAL_VERTICES = 64;

export type ModelShape = {
  readonly id: string; readonly unitId: string; readonly unitName: string; readonly label: string;
  readonly teamId: string; readonly teamOrdinal: number;
  readonly centre: Point; readonly shape: Shape; readonly facing: readonly [Point, Point] | null;
  readonly wounds: number; readonly spellTokens: number; readonly conditions: readonly string[];
  readonly elevated: boolean;
};
export type TerrainShape = {
  readonly id: string; readonly volumeId: string; readonly label: string;
  readonly kind: string; readonly declarations: readonly string[];
  readonly shape: Shape; readonly bottom: number; readonly top: number; readonly notes?: string;
};
export type MarkerShape = {
  readonly id: string; readonly kind: string; readonly label: string;
  readonly centre: Point; readonly radius: number;
};
export type ZoneShape = {
  readonly id: string; readonly shape: string; readonly teamOrdinal: number; readonly polygons: readonly Shape[];
};
export type OffTable = {
  readonly id: string; readonly label: string; readonly teamOrdinal: number;
  readonly where: string; readonly containerLabel?: string;
};
export type Scene = {
  readonly table: { readonly width: number; readonly length: number };
  readonly terrain: readonly TerrainShape[];
  readonly zones: readonly ZoneShape[];
  readonly markers: readonly MarkerShape[];
  readonly models: readonly ModelShape[];
  readonly offTable: readonly OffTable[];
};

/** Tabletop.Geometry.Bodies.BaseOutline, ported: circle, rect and a 64-gon oval, all placed by yaw. */
export function baseOutline(model: RecordedModel, centre: Point, yaw: number): Shape {
  const place = (local: readonly Point[]): Shape => ({
    kind: 'polygon',
    points: local.map((p) => {
      const turned = rotate(p, yaw);
      return { x: turned.x + centre.x, y: turned.y + centre.y };
    }),
  });
  if (model.baseShape === 'circle') return { kind: 'circle', centre, radius: model.baseA / 2 };
  if (model.baseShape === 'rect') {
    const [hw, hl] = [model.baseB / 2, model.baseA / 2];        // width across x, length along the facing axis y
    return place([{ x: -hw, y: -hl }, { x: hw, y: -hl }, { x: hw, y: hl }, { x: -hw, y: hl }]);
  }
  return place(
    Array.from({ length: OVAL_VERTICES }, (_, i) => {
      const t = (2 * Math.PI * i) / OVAL_VERTICES;
      return { x: (model.baseB / 2) * Math.cos(t), y: (model.baseA / 2) * Math.sin(t) };
    }),
  );
}

/** Tabletop.Geometry.WorldVolumes.WorldOutline, ported: the piece's position, yaw and uniform scale applied. */
export function volumeOutline(terrain: RecordedTerrain, volume: RecordedVolume): Shape {
  const s = terrain.scale;
  const offset = rotate({ x: volume.offsetX * s, y: volume.offsetY * s }, terrain.yaw);
  const centre = { x: terrain.x + offset.x, y: terrain.y + offset.y };
  if (volume.footprint === 'circle') return { kind: 'circle', centre, radius: volume.a * s };
  const hw = (volume.a * s) / 2;
  const hl = (volume.b * s) / 2;
  const yaw = terrain.yaw + volume.yaw;
  return {
    kind: 'polygon',
    points: [{ x: -hw, y: -hl }, { x: hw, y: -hl }, { x: hw, y: hl }, { x: -hw, y: hl }].map((p) => {
      const turned = rotate(p, yaw);
      return { x: turned.x + centre.x, y: turned.y + centre.y };
    }),
  };
}

export function sceneOf(board: RecordedBoard, frame: RecordedFrame): Scene {
  const definitions = new Map(board.models.map((m) => [m.id, m]));
  const models: ModelShape[] = [];
  const offTable: OffTable[] = [];

  for (const state of frame.models) {
    const definition = definitions.get(state.id);
    if (definition === undefined) continue;                    // read.ts already refused this file
    if (state.where !== 'table' || state.x === undefined || state.y === undefined) {
      offTable.push({
        id: state.id, label: definition.name, teamOrdinal: definition.teamOrdinal, where: state.where,
        ...(state.containerId !== undefined
          ? { containerLabel: definitions.get(state.containerId)?.name ?? state.containerId }
          : {}),
      });
      continue;
    }
    const centre = { x: state.x, y: state.y };
    const yaw = state.yaw ?? 0;
    const reach = rotate({ x: 0, y: definition.baseA / 2 }, yaw);
    models.push({
      id: state.id, unitId: definition.unitId, unitName: definition.unitName, label: definition.name,
      teamId: definition.teamId, teamOrdinal: definition.teamOrdinal,
      centre, shape: baseOutline(definition, centre, yaw),
      facing: [centre, { x: centre.x + reach.x, y: centre.y + reach.y }],
      wounds: state.wounds, spellTokens: state.spellTokens, conditions: state.conditions,
      elevated: (state.z ?? 0) > 0.01,
    });
  }

  const terrain: TerrainShape[] = board.terrain.flatMap((piece) =>
    piece.volumes.map((volume) => ({
      id: piece.id, volumeId: volume.id, label: piece.mesh, kind: volume.kind,
      declarations: volume.declarations, shape: volumeOutline(piece, volume),
      bottom: piece.z + volume.bottom * piece.scale, top: piece.z + volume.top * piece.scale,
      ...(volume.notes !== undefined ? { notes: volume.notes } : {}),
    })),
  );

  // A marker state carries its own definition, so board.markers is never consulted here: a marker
  // dropped during play is in no board block, and where both name one they agree.
  const markers: MarkerShape[] = frame.markers.map((state) => ({
    id: state.id, kind: state.kind, label: state.label ?? state.id,
    centre: { x: state.x, y: state.y }, radius: state.diameter / 2,
  }));

  // Rings are already resolved and already in the corner frame; an empty list draws no outline.
  const zones: ZoneShape[] = board.zones.map((zone) => ({
    id: zone.id, shape: zone.shape, teamOrdinal: zone.teamOrdinal,
    polygons: zone.polygons.map((points) => ({ kind: 'polygon', points }) as Shape),
  }));

  return { table: { width: board.width, length: board.length }, terrain, zones, markers, models, offTable };
}
```

`sceneOf` reads no optional or casted field: every field it touches — `zone.polygons`, `marker.kind`, `marker.diameter`, `marker.label` — is declared by `recording.generated.ts`, so a schema change is a type error here rather than a silent `undefined`.

- [ ] **Step 6: Run the tests; expect 6 + 8 passed**

Run: `cd viewer && npm test && npm run check`

- [ ] **Step 7: Commit**

```bash
git add viewer/src/lib/board viewer/tests/transform.test.ts viewer/tests/frame.test.ts
git commit -m "Project a recorded frame into table-space shapes and a fitted view"
```
