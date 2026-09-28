// web/src/lib/mesh/bounds.ts — every terrain asset gets its bounding box as one solid prism at confirm.
import type { Volume } from '$lib/api/types';
import { defaultVolume } from '$lib/units/volumes';
import type { Proposal } from './guess';
import { worldExtent } from './guess';
import type { Bounds } from './types';

export function terrainVolume(b: Bounds, p: Pick<Proposal, 'scale' | 'orientation'>): Volume {
  const e = worldExtent(b, p);
  return defaultVolume(e.width, e.length, e.height);
}
