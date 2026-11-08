import { describe, expect, it } from 'vitest';
import { contains, pickAt } from '../src/lib/board/pick';
import { bestHit } from '../src/lib/board/tablePick';
import { baseOutline, type Scene } from '../src/lib/board/frame';

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

  it('a point just beside a model on terrain picks the model within the slop, else the terrain', () => {
    expect(pickAt(scene, { x: 6.7, y: 5 }, 'bases', 0.5)).toEqual({ kind: 'model', id: 'm2' });   // 0.2" off m2's base, inside the ruin
    expect(pickAt(scene, { x: 6.7, y: 5 })).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });   // no slop
    expect(pickAt(scene, { x: 8, y: 5 }, 'bases', 0.5)).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });   // 1" off: the terrain
  });

  it('ports TablePick: a model entered, then a model within the slop, then the nearest other hit', () => {
    expect(bestHit([{ kind: 'other', along: 10, miss: 0 }, { kind: 'model', along: 14, miss: 0 }], 12)).toBe(1);
    expect(bestHit([{ kind: 'other', along: 10, miss: 0 }, { kind: 'model', along: 14, miss: 20 }, { kind: 'model', along: 15, miss: 6 }], 12)).toBe(2);
    expect(bestHit([{ kind: 'other', along: 10, miss: 0 }, { kind: 'model', along: 14, miss: 20 }], 12)).toBe(0);
    expect(bestHit([{ kind: 'model', along: 10, miss: 3 }, { kind: 'model', along: 20, miss: 0 }], 12)).toBe(1);
    expect(bestHit([{ kind: 'other', along: 20, miss: 0 }, { kind: 'other', along: 12, miss: 0 }, { kind: 'other', along: 5, miss: 4 }], 12)).toBe(1);
    expect(bestHit([], 12)).toBeNull();
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

  it('a board drawing circles picks an oval-based model anywhere in its drawn circle', () => {
    // A 60x35 mm oval facing +y: 0.69" wide each side of its centre, drawn as a circle of 1.18".
    const oval = baseOutline({ baseShape: 'oval', baseA: 2.36, baseB: 1.38 } as never, { x: 8, y: 5 }, 0);
    const bike: Scene = { ...scene, terrain: [{ ...scene.terrain[0]!, shape: { kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 20, y: 0 }, { x: 20, y: 20 }, { x: 0, y: 20 }] } }],
      models: [{ ...scene.models[0]!, id: 'bike', centre: { x: 8, y: 5 }, shape: oval, circle: { kind: 'circle', centre: { x: 8, y: 5 }, radius: 1.18 } }] };
    const side = { x: 8.9, y: 5 };
    expect(contains(oval, side)).toBe(false);
    expect(pickAt(bike, side)).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });
    expect(pickAt(bike, side, 'circles')).toEqual({ kind: 'model', id: 'bike' });
  });

  it('falls through to terrain and then to nothing', () => {
    expect(pickAt(scene, { x: 9, y: 9 })).toEqual({ kind: 'terrain', id: 'tp1', volumeId: 'v1' });
    expect(pickAt(scene, { x: 40, y: 40 })).toBeNull();
  });
});
