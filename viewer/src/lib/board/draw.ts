import { circle, haloText, line, path, shapeCentre, SMALL_FONT } from './canvas';
import type { Scene } from './frame';
import type { Overlay } from './interaction';
import { GLOW, teamColour } from './palette';
import { BOARD_DEFAULTS, type BoardColours, LABEL_SCALE, labelText, terrainStyle, withAlpha } from './style';
import type { Glow } from '../play/decisionBox';
import { type View, type Viewport, gridStep, toScreen } from './transform';

const WARNING = '#ffc44e';
const RIM = 'rgba(0, 0, 0, 0.45)';

export type DrawOptions = {
  readonly selected: ReadonlySet<string>;
  readonly hovered?: string | undefined;
  /** A model's real base outline, or a circle as wide as its longer side. */
  readonly models: 'bases' | 'circles';
  /** Drawn after the scene, in table inches. */
  readonly overlays?: readonly Overlay[];
  readonly showGrid: boolean;
  readonly showZones: boolean;
  /** Terrain and marker names (when zoomed in or selected) and a model's wound and condition tags. */
  readonly showLabels: boolean;
  /** What a model glows in (the open Ask's pieces, a hovered option's, a pick's ghost); null draws none. */
  readonly glow?: ((modelId: string, unitId: string) => Glow | null) | undefined;
  /** A label over a model's base: a casualty pick's ✕. */
  readonly marks?: ReadonlyMap<string, string> | undefined;
  /** Models in breach (PieceNode.SetWarning): their circle in the warning colour. */
  readonly warned?: ReadonlySet<string> | undefined;
  /** Models that cannot shoot the sight check's target: drawn darkened. */
  readonly dimmed?: ReadonlySet<string> | undefined;
  /** The theme's board colours (style.ts boardColoursOf); default Sci-fi's. */
  readonly colours?: BoardColours | undefined;
};

type Ctx = CanvasRenderingContext2D;
type At = (x: number, y: number) => { x: number; y: number };

export function draw(ctx: Ctx, scene: Scene, view: View, viewport: Viewport, options: DrawOptions): void {
  const c = options.colours ?? BOARD_DEFAULTS;
  const at: At = (x, y) => toScreen(view, scene.table, { x, y });
  const named = options.showLabels && view.scale >= LABEL_SCALE;

  ctx.save();
  ctx.clearRect(0, 0, viewport.width, viewport.height);
  table(ctx, scene, view, at, c, options.showGrid);
  if (options.showZones) zones(ctx, scene, view);
  terrain(ctx, scene, view, at, c, options, named);
  markers(ctx, scene, view, at, c, options, named);
  for (const model of scene.models) drawModel(ctx, model, scene, view, at, c, options);
  for (const overlay of options.overlays ?? []) {
    ctx.save();
    overlay(ctx, (p) => toScreen(view, scene.table, p), view.scale);
    ctx.restore();
  }
  ctx.restore();
}

function table(ctx: Ctx, scene: Scene, view: View, at: At, c: BoardColours, grid: boolean): void {
  const { width, length } = scene.table;
  const origin = at(0, length);
  ctx.fillStyle = c.bg;
  ctx.fillRect(origin.x, origin.y, width * view.scale, length * view.scale);
  if (grid) {
    const step = gridStep(view);
    ctx.lineWidth = 1;
    for (let x = step; x < width - 1e-9; x += step) {
      ctx.strokeStyle = x % 12 === 0 ? c.gridMajor : c.grid;
      line(ctx, at(x, 0), at(x, length));
    }
    for (let y = step; y < length - 1e-9; y += step) {
      ctx.strokeStyle = y % 12 === 0 ? c.gridMajor : c.grid;
      line(ctx, at(0, y), at(width, y));
    }
  }
  ctx.strokeStyle = c.edge;
  ctx.lineWidth = 1.5;
  ctx.strokeRect(origin.x, origin.y, width * view.scale, length * view.scale);
}

/** A deployment zone: a tinted band in its team's colour with a solid edge. */
function zones(ctx: Ctx, scene: Scene, view: View): void {
  for (const zone of scene.zones) {
    const colour = teamColour(zone.teamOrdinal);
    for (const polygon of zone.polygons) {
      path(ctx, polygon, view, scene.table);
      ctx.fillStyle = withAlpha(colour, 0.12);
      ctx.fill();
      ctx.strokeStyle = withAlpha(colour, 0.7);
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }
  }
}

function terrain(ctx: Ctx, scene: Scene, view: View, at: At, c: BoardColours, o: DrawOptions, named: boolean): void {
  for (const piece of scene.terrain) {
    const style = terrainStyle(piece.kind, piece.declarations);
    const picked = o.selected.has(piece.id);
    path(ctx, piece.shape, view, scene.table);
    ctx.fillStyle = picked ? style.picked : style.fill;     // selected: each volume tinted in its strongest effect's colour
    ctx.fill();
    ctx.strokeStyle = RIM;                          // a dark rim under the edge separates a piece from any table colour
    ctx.lineWidth = style.width + 2;
    ctx.stroke();
    ctx.strokeStyle = picked ? c.select : style.stroke;
    ctx.lineWidth = picked ? 2.5 : style.width;
    ctx.stroke();
  }
  // One name per piece, over its first volume.
  const labelled = new Set<string>();
  for (const piece of scene.terrain) {
    if (labelled.has(piece.id) || !(named || (o.showLabels && o.selected.has(piece.id)))) continue;
    labelled.add(piece.id);
    const centre = shapeCentre(piece.shape);
    haloText(ctx, labelText(piece.label), at(centre.x, centre.y), c.label, withAlpha(c.bg, 0.75));
  }
}

/** An objective marker: a washed disc, a ring and a centre dot, in its controller's colour or neutral. */
function markers(ctx: Ctx, scene: Scene, view: View, at: At, c: BoardColours, o: DrawOptions, named: boolean): void {
  for (const marker of scene.markers) {
    const p = at(marker.centre.x, marker.centre.y);
    const radius = Math.max(5, marker.radius * view.scale);
    const colour = marker.teamOrdinal !== undefined ? teamColour(marker.teamOrdinal) : c.label;
    const picked = o.selected.has(marker.id);
    if (marker.reach !== undefined && marker.reach > 0) {   // the marker range: a base edge on this ring is in range
      circle(ctx, p, marker.reach * view.scale);
      ctx.setLineDash([6, 4]);
      ctx.strokeStyle = withAlpha(colour, 0.8);
      ctx.lineWidth = 1.5;
      ctx.stroke();
      ctx.setLineDash([]);
    }
    circle(ctx, p, radius);
    ctx.fillStyle = withAlpha(c.bg, 0.55);
    ctx.fill();
    ctx.fillStyle = withAlpha(colour, marker.teamOrdinal !== undefined ? 0.35 : 0.14);
    ctx.fill();
    ctx.strokeStyle = picked ? c.select : colour;
    ctx.lineWidth = picked ? 3 : 2.5;
    ctx.stroke();
    circle(ctx, p, Math.max(1.5, radius * 0.5));
    ctx.lineWidth = 1.5;
    ctx.stroke();
    circle(ctx, p, Math.max(1.5, radius * 0.16));
    ctx.fillStyle = colour;
    ctx.fill();
    if (named || (o.showLabels && picked)) {
      const text = labelText(marker.label, '');
      if (text.length > 0) haloText(ctx, text, { x: p.x, y: p.y - radius - 9 }, c.label, withAlpha(c.bg, 0.75), SMALL_FONT);
    }
  }
}

function drawModel(ctx: Ctx, model: Scene['models'][number], scene: Scene, view: View, at: At, c: BoardColours, o: DrawOptions): void {
  const colour = o.warned?.has(model.id) === true ? WARNING : teamColour(model.teamOrdinal);
  const circles = o.models === 'circles';
  const picked = o.selected.has(model.id);
  const centre = at(model.centre.x, model.centre.y);
  const radius = (model.circle.kind === 'circle' ? model.circle.radius : 0.5) * view.scale;
  path(ctx, circles ? model.circle : model.shape, view, scene.table);
  ctx.fillStyle = withAlpha(colour, 0.88);
  ctx.fill();
  if (o.dimmed?.has(model.id) === true) { ctx.fillStyle = 'rgba(0, 0, 0, 0.55)'; ctx.fill(); }
  ctx.strokeStyle = model.id === o.hovered && !picked ? c.label : RIM;
  ctx.lineWidth = model.elevated ? 2 : 1.25;
  if (model.elevated) ctx.setLineDash([2, 2]);
  ctx.stroke();
  ctx.setLineDash([]);
  const glow = o.glow?.(model.id, model.unitId) ?? null;
  if (glow !== null) {
    circle(ctx, centre, Math.max(5, radius + 3));
    ctx.strokeStyle = GLOW[glow];
    ctx.lineWidth = 3;
    ctx.stroke();
  }
  if (picked) {                                                    // outside an Ask's glow, so a picked model stays outlined in a move
    circle(ctx, centre, glow === null ? radius + 2.5 : Math.max(5, radius + 3) + 3.5);
    ctx.strokeStyle = c.select;
    ctx.lineWidth = 2;
    ctx.stroke();
  }
  if (model.facing !== null && radius >= 4) {
    const [from, to] = model.facing;
    const end = circles ? tickEnd(model) : to;
    const start = { x: from.x + (end.x - from.x) * 0.45, y: from.y + (end.y - from.y) * 0.45 };
    ctx.strokeStyle = 'rgba(0, 0, 0, 0.6)';
    ctx.lineWidth = Math.min(2.5, Math.max(1.25, radius / 5));
    ctx.lineCap = 'round';
    line(ctx, at(start.x, start.y), at(end.x, end.y));
    ctx.lineCap = 'butt';
  }
  const mark = o.marks?.get(model.id);
  if (mark !== undefined) haloText(ctx, mark, centre, '#ff6b5c', withAlpha(c.bg, 0.8), 'bold 14px system-ui, sans-serif');
  if (view.scale > 6) {
    const tags = o.showLabels
      ? [model.wounds > 0 ? `-${model.wounds}` : '', ...model.conditions.map((x) => x[0]!.toUpperCase())].filter(Boolean).join(' ')
      : '';
    if (tags.length > 0) haloText(ctx, tags, { x: centre.x, y: centre.y - radius - 7 }, c.label, withAlpha(c.bg, 0.8), SMALL_FONT);
    if (model.z > 0.01) haloText(ctx, `${+model.z.toFixed(1)}"`, { x: centre.x, y: centre.y + radius + 7 }, c.label, withAlpha(c.bg, 0.8), SMALL_FONT);
  }
}

/** Where a model's facing tick ends on its circle's edge. */
function tickEnd(model: Scene['models'][number]): { x: number; y: number } {
  const [from, to] = model.facing!;
  if (model.circle.kind !== 'circle') return to;
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  if (length === 0) return to;
  const k = model.circle.radius / length;
  return { x: from.x + (to.x - from.x) * k, y: from.y + (to.y - from.y) * k };
}
