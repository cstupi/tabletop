// viewer/src/lib/play/hoverLabels.ts
// client/src/Table/HoverLabels.cs on the 2D board: the hovered unit's name in its team colour over its first model, with the distance
// from my selected unit or `Click to target` when the open Ask offers it, and under it the multi-wound models' wounds left as one line. The browser draws no
// line between the two closest bases.

import { teamColour } from '../board/palette';
import type { Scene } from '../board/frame';
import type { Overlay } from '../board/interaction';
import { drawLabel } from '../board/labelPlacement';
import type { UnitLabel } from './unitActionsView';
import { hoverLabel } from './cardText';
import { woundsLine, type HoverInfo } from './hoverView';

const radiusOf = (shape: { kind: string; radius?: number }): number => (shape.kind === 'circle' ? shape.radius ?? 0.5 : 0.5);

const LINE = 16;

/** Where the label's bottom line stands: above the unit's first model by its radius and 8 px. */
function anchorOf(scene: Scene, unitId: string, project: (p: { x: number; y: number }) => { x: number; y: number }, pixelsPerInch: number) {
  const first = scene.models.find((m) => m.unitId === unitId);
  if (first === undefined) return null;
  const at = project(first.centre);
  return { x: at.x, y: at.y - (radiusOf(first.circle) * pixelsPerInch + 8) };
}

/** The label over a unit as hoverOverlay draws it, shown or not: its bottom and the height of its lines (the name, and the wounds line when
 *  any model has several wounds); null with no model drawn. */
export function hoverLabelBox(scene: Scene, unitId: string, woundLine: boolean,
  project: (p: { x: number; y: number }) => { x: number; y: number }, pixelsPerInch: number): UnitLabel | null {
  const at = anchorOf(scene, unitId, project, pixelsPerInch);
  return at === null ? null : { bottom: at.y, height: (woundLine ? 2 : 1) * LINE };
}

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
    const text = (s: string, x: number, y: number, fill: string) => drawLabel(ctx, s, { x, y }, fill, { lineHeight: LINE, grow: 'up', halo: 'rgba(0, 0, 0, 0.85)' });
    const at = anchorOf(scene, info.unitId, project, pixelsPerInch)!;
    const above = at.y;
    // The wounds are one line under the name (woundsLine), so they never overlap it.
    const wounds = woundsLine(info.wounds);
    if (wounds !== null) text(wounds, at.x, above, '#ffffff');
    text(hoverLabel(info, eligible), at.x, wounds === null ? above : above - LINE, teamColour(info.teamOrdinal));
    ctx.restore();
  };
}
