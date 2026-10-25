import type { Scene, Shape } from './frame';
import type { Overlay } from './interaction';
import { GLOW, markerColour, teamColour, terrainStroke } from './palette';
import type { Glow } from '../play/decisionBox';
import { type View, type Viewport, gridStep, toScreen } from './transform';

export type DrawOptions = {
  readonly selected: ReadonlySet<string>;
  readonly hovered?: string | undefined;
  /** A model's real base outline, or a circle as wide as its longer side. */
  readonly models: 'bases' | 'circles';
  /** Drawn after the scene, in table inches. */
  readonly overlays?: readonly Overlay[];
  readonly showGrid: boolean;
  readonly showZones: boolean;
  readonly showLabels: boolean;
  /** What a model glows in (the open Ask's pieces, a hovered option's, a pick's ghost); null draws none. */
  readonly glow?: ((modelId: string, unitId: string) => Glow | null) | undefined;
  /** A label over a model's base: a casualty pick's ✕. */
  readonly marks?: ReadonlyMap<string, string> | undefined;
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
    const picked = options.selected.has(piece.id);
    ctx.strokeStyle = picked ? '#ffffff' : terrainStroke(piece.declarations);
    ctx.lineWidth = picked ? 3 : piece.kind === 'solid' ? 2 : 1;
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
    ctx.strokeStyle = marker.teamOrdinal === undefined ? markerColour(marker.kind) : teamColour(marker.teamOrdinal);
    if (options.selected.has(marker.id)) ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = options.selected.has(marker.id) ? 3 : marker.teamOrdinal === undefined ? 1.5 : 3;
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
    const circles = options.models === 'circles';
    const picked = options.selected.has(model.id);
    path(ctx, circles ? model.circle : model.shape, view, table);
    ctx.fillStyle = model.wounds > 0 ? '#1b1e24' : colour;
    ctx.globalAlpha = model.wounds > 0 ? 1 : 0.65;
    ctx.fill();
    ctx.globalAlpha = 1;
    ctx.strokeStyle = picked ? '#ffffff' : model.id === options.hovered ? '#e6e8ec' : colour;
    ctx.lineWidth = picked ? 2.5 : model.elevated ? 2 : 1;
    if (model.elevated) ctx.setLineDash([2, 2]);
    ctx.stroke();
    ctx.setLineDash([]);
    const glow = options.glow?.(model.id, model.unitId) ?? null;
    if (glow !== null) {
      const c = at(model.centre.x, model.centre.y);
      ctx.strokeStyle = GLOW[glow];
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.arc(c.x, c.y, Math.max(5, (model.circle.kind === 'circle' ? model.circle.radius : 0.5) * view.scale + 3), 0, 2 * Math.PI);
      ctx.stroke();
    }
    if (model.facing !== null) {
      ctx.strokeStyle = colour;
      ctx.lineWidth = 1.5;
      line(ctx, at(model.facing[0].x, model.facing[0].y), at(...tickEnd(model, circles)));
    }
    const mark = options.marks?.get(model.id);
    if (mark !== undefined) {
      const c = at(model.centre.x, model.centre.y);
      ctx.fillStyle = '#ff6b5c';
      ctx.font = 'bold 14px ui-monospace, monospace';
      ctx.textAlign = 'center';
      ctx.fillText(mark, c.x, c.y + 5);
    }
    if (model.z > 0.01 && view.scale > 6) {
      const p = at(model.centre.x, model.centre.y);
      ctx.fillStyle = '#e6e8ec';
      ctx.font = '9px ui-monospace, monospace';
      ctx.textAlign = 'center';
      ctx.fillText(`${+model.z.toFixed(1)}"`, p.x, p.y + 10);
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

  for (const overlay of options.overlays ?? []) {
    ctx.save();
    overlay(ctx, (p) => toScreen(view, table, p), view.scale);
    ctx.restore();
  }

  ctx.restore();
}

/** Where a model's facing tick ends: the base's own reach, or the circle's edge. */
function tickEnd(model: Scene['models'][number], circles: boolean): [number, number] {
  const [from, to] = model.facing!;
  if (!circles || model.circle.kind !== 'circle') return [to.x, to.y];
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  if (length === 0) return [to.x, to.y];
  const k = model.circle.radius / length;
  return [from.x + (to.x - from.x) * k, from.y + (to.y - from.y) * k];
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
