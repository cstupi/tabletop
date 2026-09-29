import { describe, expect, it } from 'vitest';
import { eulerFor } from '../src/lib/mesh/three-frame';
describe('eulerFor', () => {
  it('maps pitch/roll/yaw degrees onto a ZYX Euler in radians', () => {
    expect(eulerFor({ pitch: 90, yaw: 0, roll: 0 })).toEqual({ x: Math.PI / 2, y: 0, z: 0, order: 'ZYX' });
    expect(eulerFor({ pitch: 0, yaw: -45, roll: 10 })).toEqual({ x: 0, y: (10 * Math.PI) / 180, z: (-45 * Math.PI) / 180, order: 'ZYX' });
  });
});
