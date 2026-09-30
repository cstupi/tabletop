// web/src/lib/units/volumes.ts — the six declarations and the flags wire format; the bounding-box default.
import type { Declaration, Volume } from '$lib/api/types';

export const DECLARATIONS: Declaration[] = ['impassable', 'blocks_line_of_sight', 'see_in_and_out', 'cover', 'difficult', 'dangerous'];
export const DECLARATION_LABELS: Record<Declaration, string> = {
  impassable: 'Impassable', blocks_line_of_sight: 'Blocks line of sight', see_in_and_out: 'See in and out',
  cover: 'Cover', difficult: 'Difficult', dangerous: 'Dangerous',
};

export function parseDeclarations(wire: string): Set<Declaration> {
  return new Set(wire.split(',').map((s) => s.trim()).filter((s): s is Declaration => (DECLARATIONS as string[]).includes(s)));
}
export function formatDeclarations(set: Set<Declaration>): string {
  const names = DECLARATIONS.filter((d) => set.has(d));
  return names.length === 0 ? 'none' : names.join(', ');
}

/** Every terrain asset gets this at upload: the mesh's bounding box as one solid prism (geometry values → Where volumes come from). */
export const defaultVolume = (width: number, length: number, top: number, offset: { x: number; y: number } = { x: 0, y: 0 }, bottom = 0): Volume =>
  ({ kind: 'solid', footprint: { kind: 'rect', a: width, b: length }, offset, yaw: { degrees: 0 }, bottom, top, declarations: 'none' });
