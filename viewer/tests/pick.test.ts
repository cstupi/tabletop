import { describe, expect, it } from 'vitest';
import { contains, pickAt } from '../src/lib/board/pick';
import type { Scene } from '../src/lib/board/frame';

const scene: Scene = {
  table: { width: 72, length: 48 },
  terrain: [{
    id: 'tp1', volumeId: 'v1', label: 'ruin', kind: 'solid', declarations: ['impassable'],
    shape: { kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 10 }] },
    bottom: 0, top: 4,
  }],
  zones: [], markers: [],
  models: [
    { id: 'm1', unitId: 'u1', unitName: 'Rifles', label: 'A', teamId: 't1', teamOrdinal: 1,
      centre: { x: 5, y: 5 }, shape: { kind: 'circle', centre: { x: 5, y: 5 }, radius: 1 },
      facing: [{ x: 5, y: 5 }, { x: 5, y: 6 }], wounds: 0, spellTokens: 0, conditions: [], elevated: false, z: 0, circle: { kind: 'circle', centre: { x: 5, y: 5 }, radius: 1 } },
    { id: 'm2', unitId: 'u1', unitName: 'Rifles', label: 'B', teamId: 't1', teamOrdinal: 1,
      centre: { x: 5.5, y: 5 }, shape: { kind: 'circle', centre: { x: 5.5, y: 5 }, radius: 1 },
      facing: null, wounds: 0, spellTokens: 0, conditions: [], elevated: false, z: 0, circle: { kind: 'circle', centre: { x: 5.5, y: 5 }, radius: 1 } },
  ],
  offTable: [],
};

describe('picking', () => {
  it('tests a circle by distance and a polygon boundary-inclusive, as Polygon.Contains does', () => {
    expect(contains({ kind: 'circle', centre: { x: 0, y: 0 }, radius: 2 }, { x: 1.9, y: 0 })).toBe(true);
    expect(contains({ kind: 'circle', centre: { x: 0, y: 0 }, radius: 2 }, { x: 2.1, y: 0 })).toBe(false);
    const square: Parameters<typeof contains>[0] = { kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 2, y: 2 }, { x: 0, y: 2 }] };
    expect(contains(square, { x: 1, y: 1 })).toBe(true);
    expect(contains(square, { x: 0, y: 1 })).toBe(true);
    expect(contains(square, { x: -0.1, y: 1 })).toBe(false);
  });

  it('prefers the last model drawn where two bases overlap', () => {
    expect(pickAt(scene, { x: 5.4, y: 5 })).toEqual({ kind: 'model', id: 'm2' });
    expect(pickAt(scene, { x: 4.2, y: 5 })).toEqual({ kind: 'model', id: 'm1' });
  });

  it('markers pick after models and before terrain', () => {
    const withMarker: Scene = {
      ...scene, markers: [{ id: 'k1', kind: 'objective', label: 'A', centre: { x: 4, y: 8 }, radius: 1.5 }],
    };
    expect(pickAt(withMarker, { x: 4, y: 8 })).toEqual({ kind: 'marker', id: 'k1' });      // over terrain
    expect(pickAt(withMarker, { x: 5, y: 5.9 })).toEqual({ kind: 'model', id: 'm1' });     // models first
    expect(pickAt(withMarker, { x: 4, y: 6.7 })).toEqual({ kind: 'marker', id: 'k1' });
  });

  it('falls through to terrain and then to nothing', () => {
    expect(pickAt(scene, { x: 9, y: 9 })).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });
    expect(pickAt(scene, { x: 40, y: 40 })).toBeNull();
  });
});
