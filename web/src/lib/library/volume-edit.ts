import type { Declaration, FootprintKind, Volume, VolumeKind } from '$lib/api/types';
import { formatDeclarations, parseDeclarations } from '$lib/units/volumes';

export interface VolumeDraft { kind: VolumeKind; footprintKind: FootprintKind; a: number; b: number; offsetX: number; offsetY: number; yawDegrees: number; bottom: number; top: number; declarations: Set<Declaration>; notes: string }

export const toDraft = (v: Volume): VolumeDraft => ({ kind: v.kind, footprintKind: v.footprint.kind, a: v.footprint.a, b: v.footprint.b, offsetX: v.offset.x, offsetY: v.offset.y, yawDegrees: v.yaw.degrees, bottom: v.bottom, top: v.top, declarations: parseDeclarations(v.declarations), notes: v.notes ?? '' });

export function fromDraft(d: VolumeDraft): Volume {
  const notes = d.notes.trim();
  return { kind: d.kind, footprint: { kind: d.footprintKind, a: d.a, b: d.footprintKind === 'circle' ? d.a : d.b }, offset: { x: d.offsetX, y: d.offsetY }, yaw: { degrees: d.yawDegrees }, bottom: d.bottom, top: d.top, declarations: formatDeclarations(d.declarations), ...(notes ? { notes } : {}) };
}

export function validate(d: VolumeDraft): string[] {
  const errors: string[] = [];
  if (![d.a, d.b, d.offsetX, d.offsetY, d.yawDegrees, d.bottom, d.top].every(Number.isFinite)) errors.push('values must be numbers');
  if (!(d.top > d.bottom)) errors.push('top must be above bottom');
  if (!(d.a > 0) || (d.footprintKind === 'rect' && !(d.b > 0))) errors.push('dimensions must be positive');
  if (d.declarations.has('see_in_and_out') && !d.declarations.has('blocks_line_of_sight')) errors.push('see in and out needs blocks line of sight');
  return errors;
}

/** The top-down camera shows [minX, maxX] × [minY, maxY] across the viewport with +Y up the screen. */
export function screenToTable(px: number, py: number, viewport: { width: number; height: number }, e: { minX: number; maxX: number; minY: number; maxY: number }) {
  return { x: e.minX + (px / viewport.width) * (e.maxX - e.minX), y: e.maxY - (py / viewport.height) * (e.maxY - e.minY) };
}
