import type { Scene, Shape } from './frame';
import type { Point } from './transform';

import type { BoardHit } from './interaction';

export type Hit = BoardHit;

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

/** Models first, then markers, then terrain; the last drawn wins within each. Table inches in, never screen pixels. A model is tested by
 *  what the board draws for it: its base, or its circle when the board draws circles. */
export function pickAt(scene: Scene, p: Point, models: 'bases' | 'circles' = 'bases'): Hit | null {
  for (let i = scene.models.length - 1; i >= 0; i--) {
    const model = scene.models[i]!;
    if (contains(models === 'circles' ? model.circle : model.shape, p)) return { kind: 'model', id: model.id };
  }
  for (let i = scene.markers.length - 1; i >= 0; i--) {
    const marker = scene.markers[i]!;
    if (contains({ kind: 'circle', centre: marker.centre, radius: marker.radius }, p)) return { kind: 'marker', id: marker.id };
  }
  for (let i = scene.terrain.length - 1; i >= 0; i--) {
    const piece = scene.terrain[i]!;
    if (contains(piece.shape, p)) return { kind: 'terrain', id: piece.id, volumeId: piece.volumeId };
  }
  return null;
}
