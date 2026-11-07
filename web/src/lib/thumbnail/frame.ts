// web/src/lib/thumbnail/frame.ts — where the thumbnail camera stands: the model's box seen from the front-right and above (table
// frame, Z up, the facing arrow along +Y), as the preview starts, far enough that the bounding sphere fills the square.
import type { Bounds, Vec3 } from '$lib/mesh/types';

export const THUMBNAIL_SIZE = 256;
const POLAR = (55 * Math.PI) / 180, AZIMUTH = (30 * Math.PI) / 180;

export function thumbnailView(box: Bounds, fovDegrees: number): { position: Vec3; target: Vec3 } {
  const empty = box.min.some((v, i) => !(v <= box.max[i]!));
  const min: Vec3 = empty ? [-0.5, -0.5, 0] : box.min, max: Vec3 = empty ? [0.5, 0.5, 1] : box.max;
  const target: Vec3 = [(min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2];
  const radius = Math.max(Math.hypot(max[0] - min[0], max[1] - min[1], max[2] - min[2]) / 2, 0.25);
  const distance = (radius / Math.sin((fovDegrees * Math.PI) / 360)) * 1.02;
  const d: Vec3 = [Math.sin(POLAR) * Math.sin(AZIMUTH), Math.sin(POLAR) * Math.cos(AZIMUTH), Math.cos(POLAR)];
  return { position: [target[0] + d[0] * distance, target[1] + d[1] * distance, target[2] + d[2] * distance], target };
}
