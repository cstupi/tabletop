// viewer/src/lib/play/hoverLabels.ts
// client/src/Table/HoverLabels.cs on the 2D board: the hovered unit's name in its team colour over its first model, with the distance
// from my selected unit or `Click to target` when the open Ask offers it, and each multi-wound model's wounds left. The browser draws no
// line between the two closest bases.

import { teamColour } from '../board/palette';
import type { Scene } from '../board/frame';
import type { Overlay } from '../board/interaction';
import { drawLabel } from '../board/labelPlacement';
import { hoverLabel } from './cardText';
import type { HoverInfo } from './hoverView';

const radiusOf = (shape: { kind: string; radius?: number }): number => (shape.kind === 'circle' ? shape.radius ?? 0.5 : 0.5);

export function hoverOverlay(scene: Scene, info: HoverInfo, eligible: boolean): Overlay {
  return (ctx, project, pixelsPerInch) => {
    const first = scene.models.find((m) => m.unitId === info.unitId);
    if (first === undefined) return;
    ctx.save();
    ctx.font = '600 13px ui-monospace, SFMono-Regular, Menlo, monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.lineWidth = 3;
    ctx.strokeStyle = 'rgba(0, 0, 0, 0.85)';
    const text = (s: string, x: number, y: number, fill: string) => drawLabel(ctx, s, { x, y }, fill, { lineHeight: 16, grow: 'up', halo: 'rgba(0, 0, 0, 0.85)' });
    const at = project(first.centre);
    text(hoverLabel(info, eligible), at.x, at.y - (radiusOf(first.circle) * pixelsPerInch + 8), teamColour(info.teamOrdinal));
    for (const w of info.wounds) {
      const model = scene.models.find((m) => m.id === w.modelId);
      if (model === undefined) continue;
      const p = project(model.centre);
      text(`${w.left}/${w.max}`, p.x, p.y - (radiusOf(model.circle) * pixelsPerInch + 2), '#ffffff');
    }
    ctx.restore();
  };
}
