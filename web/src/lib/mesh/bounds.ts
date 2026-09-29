// web/src/lib/mesh/bounds.ts — every terrain asset gets its bounding box as one solid prism at confirm.
import type { Volume } from '$lib/api/types';
import { defaultVolume } from '$lib/units/volumes';
import type { Proposal } from './guess';
import { worldExtent } from './guess';
import type { Bounds } from './types';

export function terrainVolume(b: Bounds, p: Pick<Proposal, 'scale' | 'orientation'>): Volume {
  const e = worldExtent(b, p);
  // Rounded to 1e-9 so a rotated mesh stores exact zeros rather than 1e-16 residue; `+ 0` turns a -0 into 0.
  const r = (v: number) => Math.round(v * 1e9) / 1e9 + 0;
  return defaultVolume(e.width, e.length, r(e.max[2]), { x: r((e.min[0] + e.max[0]) / 2), y: r((e.min[1] + e.max[1]) / 2) }, r(e.min[2]));
}
