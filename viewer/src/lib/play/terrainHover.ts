// viewer/src/lib/play/terrainHover.ts
// client/src/Table/HoverLabels.cs DrawTerrain on the 2D board: in play the hovered terrain piece's TerrainLabel over the piece, one line
// per volume, except for the selected piece, whose details are the terrain card's.

import type { Scene } from '../board/frame';
import type { Overlay } from '../board/interaction';
import type { Point } from '../board/transform';
import type { TableState } from '../protocol/wire.generated';
import type { Catalog } from '../setup/catalog';
import type { TerrainWords } from '../setup/terrainCard';
import { terrainLabelOf } from './terrainLabel';

/** The label's lines for the hovered piece; null when nothing is hovered, the piece is selected or is not on the table. */
export function terrainHoverText(table: TableState, catalog: Catalog, hovered: string | null, selected: string | null, words?: TerrainWords | null): string[] | null {
  if (hovered === null || hovered === selected) return null;
  const piece = table.terrain.find((t) => t.id === hovered);
  if (piece === undefined || piece.volumes.length === 0) return null;
  return terrainLabelOf(piece, catalog, words).split('\n');
}

const LINE = 16;

/** The lines centred over the piece's outline, above its top edge on screen. */
export function terrainHoverOverlay(scene: Scene, pieceId: string, lines: readonly string[]): Overlay {
  return (ctx, project) => {
    const points: Point[] = scene.terrain.filter((t) => t.id === pieceId).flatMap((t) => t.shape.kind === 'circle'
      ? [{ x: t.shape.centre.x - t.shape.radius, y: t.shape.centre.y - t.shape.radius }, { x: t.shape.centre.x + t.shape.radius, y: t.shape.centre.y + t.shape.radius },
        { x: t.shape.centre.x - t.shape.radius, y: t.shape.centre.y + t.shape.radius }, { x: t.shape.centre.x + t.shape.radius, y: t.shape.centre.y - t.shape.radius }]
      : [...t.shape.points]).map(project);
    if (points.length === 0) return;
    const xs = points.map((p) => p.x), top = Math.min(...points.map((p) => p.y));
    const x = (Math.min(...xs) + Math.max(...xs)) / 2;
    ctx.save();
    ctx.font = '600 13px ui-monospace, SFMono-Regular, Menlo, monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.lineWidth = 3;
    ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)';
    ctx.fillStyle = '#ffffff';
    lines.forEach((line, i) => {
      const y = Math.max(LINE * (i + 1), top - 6 - LINE * (lines.length - 1 - i));
      ctx.strokeText(line, x, y);
      ctx.fillText(line, x, y);
    });
    ctx.restore();
  };
}
