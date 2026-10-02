# Task 5: The board — a 2D canvas, and picking what is under the pointer

**Files:**
- Create: `viewer/src/lib/board/pick.ts`, `viewer/src/lib/board/draw.ts`, `viewer/src/lib/components/Board.svelte`
- Test: `viewer/tests/pick.test.ts`

**Interfaces:**
- Consumes: `Scene`, `Shape`, `ModelShape`, `TerrainShape` (Task 4); `View`, `Table`, `Point`, `toScreen`, `gridStep`, `fit`, `zoomAt`, `panBy`, `toTable` (Task 4); `teamColour`, `terrainStroke`, `markerColour` (Task 4).
- Produces (`pick.ts`): `function contains(shape: Shape, p: Point): boolean`; `type Hit = { readonly kind: 'model'; readonly id: string } | { readonly kind: 'terrain'; readonly id: string; readonly volumeId: string }`; `function pickAt(scene: Scene, p: Point): Hit | null`.
- Produces (`draw.ts`): `type DrawOptions = { readonly selected?: string; readonly hovered?: string; readonly showGrid: boolean; readonly showZones: boolean; readonly showLabels: boolean }`; `function draw(ctx: CanvasRenderingContext2D, scene: Scene, view: View, viewport: Viewport, options: DrawOptions): void`.
- Produces (`Board.svelte`): props `{ scene: Scene, selected: string | null, onPick: (hit: Hit | null) => void, showGrid: boolean, showZones: boolean, showLabels: boolean }`; owns the `<canvas>`, its `ResizeObserver`, the device-pixel-ratio scaling, wheel zoom, drag pan, and a `Reset view` button.

**Canvas, not SVG.** Frames carry whole state, so a scrub to any frame invalidates every model, every marker and every position on the board at once — there is no partial update for a retained-mode tree to exploit. At a few hundred models the SVG document is roughly 300 base nodes plus 300 facing ticks plus terrain outlines plus 120 grid lines, and moving the timeline one step rewrites an attribute on every one of them, so the browser pays style recalculation and layout for the whole tree per frame; dragging the scrubber does that at pointer rate. A single `<canvas>` pays one `clearRect` and about 800 path operations per frame, which is well inside a 16 ms budget and does not grow a DOM. The two things SVG would have given away free — hit testing and text layout — are cheap here because a base is a circle, a square or a convex 64-gon in table space, so `pickAt` is a point test in inches with no screen-space maths, and the only text is a short label drawn with `fillText`. WebGL and any drawing library are refused outright: the workload does not need them and the spec's whole point about the viewer is that it is schematic (spec → *`viewer/` is a developer web app*: "Nothing is rendered in 3D and no mesh is ever loaded").

- [ ] **Step 1: Write `viewer/tests/pick.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { contains, pickAt } from '../src/lib/board/pick';
import type { Scene } from '../src/lib/board/frame';

const scene: Scene = {
  table: { width: 72, length: 48 },
  terrain: [{
    id: 'tp1', volumeId: 'v1', label: 'ruin', kind: 'solid', declarations: ['impassable'],
    shape: { kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 10 }] },
    bottom: 0, top: 4,
  }],
  zones: [], markers: [],
  models: [
    { id: 'm1', unitId: 'u1', unitName: 'Rifles', label: 'A', teamId: 't1', teamOrdinal: 1,
      centre: { x: 5, y: 5 }, shape: { kind: 'circle', centre: { x: 5, y: 5 }, radius: 1 },
      facing: [{ x: 5, y: 5 }, { x: 5, y: 6 }], wounds: 0, spellTokens: 0, conditions: [], elevated: false },
    { id: 'm2', unitId: 'u1', unitName: 'Rifles', label: 'B', teamId: 't1', teamOrdinal: 1,
      centre: { x: 5.5, y: 5 }, shape: { kind: 'circle', centre: { x: 5.5, y: 5 }, radius: 1 },
      facing: null, wounds: 0, spellTokens: 0, conditions: [], elevated: false },
  ],
  offTable: [],
};

describe('picking', () => {
  it('tests a circle by distance and a polygon boundary-inclusive, as Polygon.Contains does', () => {
    expect(contains({ kind: 'circle', centre: { x: 0, y: 0 }, radius: 2 }, { x: 1.9, y: 0 })).toBe(true);
    expect(contains({ kind: 'circle', centre: { x: 0, y: 0 }, radius: 2 }, { x: 2.1, y: 0 })).toBe(false);
    const square: Parameters<typeof contains>[0] = { kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 2, y: 2 }, { x: 0, y: 2 }] };
    expect(contains(square, { x: 1, y: 1 })).toBe(true);
    expect(contains(square, { x: 0, y: 1 })).toBe(true);
    expect(contains(square, { x: -0.1, y: 1 })).toBe(false);
  });

  it('prefers the last model drawn where two bases overlap', () => {
    expect(pickAt(scene, { x: 5.4, y: 5 })).toEqual({ kind: 'model', id: 'm2' });
    expect(pickAt(scene, { x: 4.2, y: 5 })).toEqual({ kind: 'model', id: 'm1' });
  });

  it('falls through to terrain and then to nothing', () => {
    expect(pickAt(scene, { x: 9, y: 9 })).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });
    expect(pickAt(scene, { x: 40, y: 40 })).toBeNull();
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `pick.ts`**

```ts
// viewer/src/lib/board/pick.ts
import type { Scene, Shape } from './frame';
import type { Point } from './transform';

export type Hit =
  | { readonly kind: 'model'; readonly id: string }
  | { readonly kind: 'terrain'; readonly id: string; readonly volumeId: string };

/** Boundary inclusive, any vertex order, concave allowed — Tabletop.Geometry.Polygon.Contains's contract. */
export function contains(shape: Shape, p: Point): boolean {
  if (shape.kind === 'circle') {
    const dx = p.x - shape.centre.x;
    const dy = p.y - shape.centre.y;
    return dx * dx + dy * dy <= shape.radius * shape.radius;
  }
  const points = shape.points;
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const a = points[i]!;
    const b = points[j]!;
    const cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    const along = (p.x - a.x) * (b.x - a.x) + (p.y - a.y) * (b.y - a.y);
    const lengthSquared = (b.x - a.x) ** 2 + (b.y - a.y) ** 2;
    if (Math.abs(cross) <= 1e-9 && along >= -1e-9 && along <= lengthSquared + 1e-9) return true;   // on an edge
    if (a.y > p.y !== b.y > p.y && p.x < a.x + ((p.y - a.y) / (b.y - a.y)) * (b.x - a.x)) inside = !inside;
  }
  return inside;
}

/** Models first, last drawn wins, then terrain. Table inches in, never screen pixels. */
export function pickAt(scene: Scene, p: Point): Hit | null {
  for (let i = scene.models.length - 1; i >= 0; i--) {
    const model = scene.models[i]!;
    if (contains(model.shape, p)) return { kind: 'model', id: model.id };
  }
  for (let i = scene.terrain.length - 1; i >= 0; i--) {
    const piece = scene.terrain[i]!;
    if (contains(piece.shape, p)) return { kind: 'terrain', id: piece.id, volumeId: piece.volumeId };
  }
  return null;
}
```

- [ ] **Step 3: Write `draw.ts`**

```ts
// viewer/src/lib/board/draw.ts
import type { Scene, Shape } from './frame';
import { markerColour, teamColour, terrainStroke } from './palette';
import { type View, type Viewport, gridStep, toScreen } from './transform';

export type DrawOptions = {
  readonly selected?: string | undefined;
  readonly hovered?: string | undefined;
  readonly showGrid: boolean;
  readonly showZones: boolean;
  readonly showLabels: boolean;
};

export function draw(
  ctx: CanvasRenderingContext2D, scene: Scene, view: View, viewport: Viewport, options: DrawOptions,
): void {
  const table = scene.table;
  const at = (x: number, y: number) => toScreen(view, table, { x, y });

  ctx.save();
  ctx.clearRect(0, 0, viewport.width, viewport.height);

  // The slab.
  const origin = at(0, table.length);
  ctx.fillStyle = '#1b1e24';
  ctx.fillRect(origin.x, origin.y, table.width * view.scale, table.length * view.scale);

  if (options.showGrid) {
    const step = gridStep(view);
    ctx.lineWidth = 1;
    for (let x = 0; x <= table.width + 1e-9; x += step) {
      ctx.strokeStyle = x % 12 === 0 ? '#39404c' : '#252a32';
      line(ctx, at(x, 0), at(x, table.length));
    }
    for (let y = 0; y <= table.length + 1e-9; y += step) {
      ctx.strokeStyle = y % 12 === 0 ? '#39404c' : '#252a32';
      line(ctx, at(0, y), at(table.width, y));
    }
  }

  if (options.showZones) {
    ctx.setLineDash([6, 4]);
    for (const zone of scene.zones) {
      ctx.strokeStyle = teamColour(zone.teamOrdinal);
      ctx.lineWidth = 1.5;
      for (const polygon of zone.polygons) path(ctx, polygon, view, table), ctx.stroke();
    }
    ctx.setLineDash([]);
  }

  for (const piece of scene.terrain) {
    ctx.strokeStyle = terrainStroke(piece.declarations);
    ctx.lineWidth = piece.kind === 'solid' ? 2 : 1;
    if (piece.kind === 'area') ctx.setLineDash([3, 3]);
    path(ctx, piece.shape, view, table);
    ctx.stroke();
    ctx.setLineDash([]);
    if (options.showLabels) {
      const centre = shapeCentre(piece.shape);
      const p = at(centre.x, centre.y);
      ctx.fillStyle = terrainStroke(piece.declarations);
      ctx.font = '10px ui-monospace, monospace';
      ctx.textAlign = 'center';
      ctx.fillText(piece.label, p.x, p.y - 4);
      ctx.fillText(piece.declarations.join(' · ') || piece.kind, p.x, p.y + 8);
    }
  }

  for (const marker of scene.markers) {
    const p = at(marker.centre.x, marker.centre.y);
    ctx.strokeStyle = markerColour(marker.kind);
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(3, marker.radius * view.scale), 0, 2 * Math.PI);
    ctx.stroke();
    if (options.showLabels) {
      ctx.fillStyle = markerColour(marker.kind);
      ctx.font = '10px ui-monospace, monospace';
      ctx.textAlign = 'center';
      ctx.fillText(marker.label, p.x, p.y - Math.max(3, marker.radius * view.scale) - 3);
    }
  }

  for (const model of scene.models) {
    const colour = teamColour(model.teamOrdinal);
    path(ctx, model.shape, view, table);
    ctx.fillStyle = model.wounds > 0 ? '#1b1e24' : colour;
    ctx.globalAlpha = model.wounds > 0 ? 1 : 0.65;
    ctx.fill();
    ctx.globalAlpha = 1;
    ctx.strokeStyle = model.id === options.selected ? '#ffffff' : model.id === options.hovered ? '#e6e8ec' : colour;
    ctx.lineWidth = model.id === options.selected ? 2.5 : model.elevated ? 2 : 1;
    if (model.elevated) ctx.setLineDash([2, 2]);
    ctx.stroke();
    ctx.setLineDash([]);
    if (model.facing !== null) {
      ctx.strokeStyle = colour;
      ctx.lineWidth = 1.5;
      line(ctx, at(model.facing[0].x, model.facing[0].y), at(model.facing[1].x, model.facing[1].y));
    }
    if (options.showLabels && view.scale > 6) {
      const p = at(model.centre.x, model.centre.y);
      ctx.fillStyle = '#e6e8ec';
      ctx.font = '9px ui-monospace, monospace';
      ctx.textAlign = 'center';
      const tags = [model.wounds > 0 ? `-${model.wounds}` : '', ...model.conditions.map((c) => c[0]!)].filter(Boolean);
      if (tags.length > 0) ctx.fillText(tags.join(''), p.x, p.y - 2);
    }
  }

  ctx.restore();
}

function line(ctx: CanvasRenderingContext2D, a: { x: number; y: number }, b: { x: number; y: number }): void {
  ctx.beginPath();
  ctx.moveTo(a.x, a.y);
  ctx.lineTo(b.x, b.y);
  ctx.stroke();
}

function path(ctx: CanvasRenderingContext2D, shape: Shape, view: View, table: { width: number; length: number }): void {
  ctx.beginPath();
  if (shape.kind === 'circle') {
    const p = toScreen(view, table, shape.centre);
    ctx.arc(p.x, p.y, shape.radius * view.scale, 0, 2 * Math.PI);
    return;
  }
  shape.points.forEach((point, i) => {
    const p = toScreen(view, table, point);
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  });
  ctx.closePath();
}

function shapeCentre(shape: Shape): { x: number; y: number } {
  if (shape.kind === 'circle') return shape.centre;
  const n = shape.points.length;
  return {
    x: shape.points.reduce((s, p) => s + p.x, 0) / n,
    y: shape.points.reduce((s, p) => s + p.y, 0) / n,
  };
}
```

- [ ] **Step 4: Write `Board.svelte`**

```svelte
<!-- viewer/src/lib/components/Board.svelte -->
<script lang="ts">
  import type { Scene } from '../board/frame';
  import { draw } from '../board/draw';
  import { type Hit, pickAt } from '../board/pick';
  import { type View, fit, panBy, toTable, zoomAt } from '../board/transform';

  let {
    scene, selected = null, onPick, showGrid = true, showZones = true, showLabels = true,
  }: {
    scene: Scene; selected?: string | null; onPick: (hit: Hit | null) => void;
    showGrid?: boolean; showZones?: boolean; showLabels?: boolean;
  } = $props();

  let canvas: HTMLCanvasElement | undefined = $state();
  let box = $state({ width: 800, height: 600 });
  let view: View | null = $state(null);
  let hovered: string | undefined = $state(undefined);
  let dragging: { x: number; y: number } | null = null;
  let downAt: { x: number; y: number } | null = null;

  // A new recording is a different table: the old pan and zoom mean nothing on it.
  $effect(() => { void scene.table.width; void scene.table.length; view = null; });

  const current = $derived(view ?? fit(scene.table, box));

  $effect(() => {
    if (canvas === undefined) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(box.width * dpr);
    canvas.height = Math.round(box.height * dpr);
    const ctx = canvas.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    draw(ctx, scene, current, box, { selected: selected ?? undefined, hovered, showGrid, showZones, showLabels });
  });

  $effect(() => {
    if (canvas === undefined) return;
    const observer = new ResizeObserver(([entry]) => {
      const rect = entry!.contentRect;
      box = { width: Math.max(1, rect.width), height: Math.max(1, rect.height) };
    });
    observer.observe(canvas);
    return () => observer.disconnect();
  });

  function local(event: PointerEvent | WheelEvent): { x: number; y: number } {
    const rect = canvas!.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }
</script>

<div class="board">
  <canvas
    bind:this={canvas}
    onwheel={(e) => { e.preventDefault(); view = zoomAt(current, scene.table, local(e), e.deltaY < 0 ? 1.15 : 1 / 1.15); }}
    onpointerdown={(e) => { dragging = downAt = local(e); canvas!.setPointerCapture(e.pointerId); }}
    onpointermove={(e) => {
      const p = local(e);
      if (dragging !== null) { view = panBy(current, p.x - dragging.x, p.y - dragging.y); dragging = p; return; }
      const hit = pickAt(scene, toTable(current, scene.table, p));
      hovered = hit !== null && hit.kind === 'model' ? hit.id : undefined;
    }}
    onpointerup={(e) => {
      const p = local(e);
      const moved = downAt !== null && (Math.abs(p.x - downAt.x) > 2 || Math.abs(p.y - downAt.y) > 2);
      dragging = downAt = null;
      canvas!.releasePointerCapture(e.pointerId);
      if (!moved) onPick(pickAt(scene, toTable(current, scene.table, p)));
    }}
    onpointercancel={() => { dragging = downAt = null; }}
    onpointerleave={() => { hovered = undefined; }}
  ></canvas>
  <button onclick={() => (view = null)}>Reset view</button>
</div>

<style>
  .board { position: relative; width: 100%; height: 100%; }
  canvas { display: block; width: 100%; height: 100%; touch-action: none; cursor: crosshair; }
  button { position: absolute; top: 0.5rem; right: 0.5rem; }
</style>
```

- [ ] **Step 5: Run the tests and the type check; expect 3 more passed**

Run: `cd viewer && npm test && npm run check`

- [ ] **Step 6: Commit**

```bash
git add viewer/src/lib/board/pick.ts viewer/src/lib/board/draw.ts viewer/src/lib/components/Board.svelte viewer/tests/pick.test.ts
git commit -m "Draw the board on a 2D canvas and pick what is under the pointer"
```
