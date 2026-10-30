// viewer/src/lib/board/table.ts
// A setup draft (a TableState) as a board Scene, and the drag ghost. No geometry import: the zone polygons come in as a function
// (Geometry.zonePolygons in setup), so the board stays free of the WebAssembly.

import type { Placement, TableSize, TableState, TerrainPlacement, ZoneLegs } from '../protocol/wire.generated';
import { type Scene, type Shape, type TerrainShape, worldOutline } from './frame';
import type { Overlay } from './interaction';
import { DECLARATIONS } from './palette';
import type { Point } from './transform';

/** `legs`: Side Battle's, from the game's Welcome (ConnectionState.zones); absent is the geometry's default. */
export type ZonePolygons = (shape: string, size: TableSize, legs?: ZoneLegs) => readonly (readonly Point[])[];

const declarationsOf = (wire: string): readonly string[] => {
  const present = new Set(wire.split(',').map((s) => s.trim()));
  return DECLARATIONS.filter((d) => present.has(d));
};

const outlines = (piece: TerrainPlacement, at: Placement): Shape[] => piece.volumes.map(({ volume: v }) => worldOutline(
  { x: at.position.x, y: at.position.y }, at.yaw.degrees, piece.scale,
  { circle: v.footprint.kind === 'circle', a: v.footprint.a, b: v.footprint.b, offsetX: v.offset.x, offsetY: v.offset.y, yaw: v.yaw.degrees },
));

/** One terrain piece's outlines at a placement other than its own: the drag's ghost. */
export const pieceOutlines = (piece: TerrainPlacement, at: Placement): Shape[] => outlines(piece, at);

/** A zone's polygons are fixed by its shape, the table's size and the legs: each source is asked once per triple, not on every scene rebuild. */
const zoneMemo = new WeakMap<ZonePolygons, Map<string, readonly (readonly Point[])[]>>();
function zonePolygonsOf(zones: ZonePolygons, shape: string, size: TableSize, legs: ZoneLegs | undefined): readonly (readonly Point[])[] {
  let kept = zoneMemo.get(zones);
  if (kept === undefined) { kept = new Map(); zoneMemo.set(zones, kept); }
  const key = `${shape}|${size.width}|${size.length}|${legs === undefined ? '' : `${legs.sideBattleLong}|${legs.sideBattleShort}`}`;
  let polygons = kept.get(key);
  if (polygons === undefined) { polygons = legs === undefined ? zones(shape, size) : zones(shape, size, legs); kept.set(key, polygons); }
  return polygons;
}

/** A TableState as a Scene: terrain outlines labelled with `name`, markers, zones resolved by `zones` with `legs`, no models. */
export function sceneOfTable(table: TableState, zones: ZonePolygons, name: (piece: TerrainPlacement) => string, legs?: ZoneLegs): Scene {
  const terrain: TerrainShape[] = table.terrain.flatMap((piece) => {
    const shapes = outlines(piece, { position: piece.position, yaw: piece.yaw });
    return piece.volumes.map(({ id, volume: v }, i) => ({
      id: piece.id, volumeId: id, label: name(piece), kind: v.kind, declarations: declarationsOf(v.declarations), shape: shapes[i]!,
      bottom: piece.position.z + v.bottom * piece.scale, top: piece.position.z + v.top * piece.scale,
      ...(v.notes !== undefined ? { notes: v.notes } : {}),
    }));
  });
  return {
    table: { width: table.size.width, length: table.size.length },
    terrain,
    zones: table.zones.map((z) => ({
      id: z.id, shape: z.shape, teamOrdinal: z.teamOrdinal,
      polygons: zonePolygonsOf(zones, z.shape, table.size, legs).map((points): Shape => ({ kind: 'polygon', points })),
    })),
    markers: table.markers.map((m) => ({
      id: m.id, kind: m.kind, label: m.label ?? m.kind, centre: { x: m.position.x, y: m.position.y }, radius: m.diameter / 2,
    })),
    models: [],
    offTable: [],
  };
}

/** The box fitBox shows: the table, widened to every terrain outline and marker beside it. */
export function extentOf(scene: Scene): { minX: number; minY: number; maxX: number; maxY: number } {
  const box = { minX: 0, minY: 0, maxX: scene.table.width, maxY: scene.table.length };
  const take = (p: Point, r = 0): void => {
    box.minX = Math.min(box.minX, p.x - r); box.maxX = Math.max(box.maxX, p.x + r);
    box.minY = Math.min(box.minY, p.y - r); box.maxY = Math.max(box.maxY, p.y + r);
  };
  for (const t of scene.terrain) {
    if (t.shape.kind === 'circle') take(t.shape.centre, t.shape.radius);
    else for (const p of t.shape.points) take(p);
  }
  for (const m of scene.markers) take(m.centre, m.radius);
  return box;
}

/** A see-through copy of shapes, outlined in `colour`: the piece a drag holds, over the board. */
export const ghostOverlay = (shapes: readonly Shape[], colour: string): Overlay => (ctx, project) => {
  ctx.fillStyle = colour;
  ctx.strokeStyle = colour;
  ctx.lineWidth = 2;
  for (const shape of shapes) {
    ctx.beginPath();
    if (shape.kind === 'circle') {
      const c = project(shape.centre);
      const edge = project({ x: shape.centre.x + shape.radius, y: shape.centre.y });
      ctx.arc(c.x, c.y, Math.abs(edge.x - c.x), 0, 2 * Math.PI);
    } else {
      shape.points.forEach((point, i) => {
        const p = project(point);
        if (i === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
      });
      ctx.closePath();
    }
    ctx.globalAlpha = 0.25;
    ctx.fill();
    ctx.globalAlpha = 0.9;
    ctx.stroke();
  }
};
