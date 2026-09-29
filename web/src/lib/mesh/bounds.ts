// web/src/lib/mesh/bounds.ts — every terrain asset gets its bounding box as one solid prism at confirm.
import type { Volume } from '$lib/api/types';
import { defaultVolume } from '$lib/units/volumes';
import type { Proposal } from './guess';
import { worldExtent } from './guess';
import type { Bounds } from './types';

export function terrainVolume(b: Bounds, p: Pick<Proposal, 'scale' | 'orientation'>): Volume {
  const e = worldExtent(b, p);
  // `+ 0` turns a -0 from a symmetric extent into 0.
  return defaultVolume(e.width, e.length, e.max[2], { x: (e.min[0] + e.max[0]) / 2 + 0, y: (e.min[1] + e.max[1]) / 2 + 0 }, e.min[2] + 0);
}
