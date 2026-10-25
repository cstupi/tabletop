import { describe, expect, it } from 'vitest';
import { beside, cannotAddMarker, cannotAddTerrain, cannotClearTerrain, freeSpot, markersHeading, terrainHeading } from '../src/lib/setup/limits';
import type { Marker, TableState } from '../src/lib/protocol/wire.generated';

const size = { width: 72, length: 48 };
const draft = (terrain: number, markers: Marker[] = []): TableState => ({
  id: 'd', size, texture: { builtin: 'grass' }, zones: [], markers,
  terrain: Array.from({ length: terrain }, (_, i) => ({ id: `t${i}` })) as unknown as TableState['terrain'],
});
const marker = (x: number, y: number, diameter: number): Marker => ({
  id: `${x}`, kind: 'objective', position: { x, y, z: 0 }, yaw: { degrees: 0 }, diameter, visual: { builtin: 'objective-ring' },
});

describe('setup limits', () => {
  it('headings count against the caps', () => {
    expect(terrainHeading(draft(3))).toBe('Terrain (3/64)');
    expect(markersHeading(draft(0, [marker(1, 1, 1)]))).toBe('Markers (1/32)');
    expect(terrainHeading(null)).toBe('Terrain');
  });

  it('cannotAddTerrain at 64 and for a batch', () => {
    expect(cannotAddTerrain(null)).toBe('The table has not arrived yet.');
    expect(cannotAddTerrain(draft(63))).toBeNull();
    expect(cannotAddTerrain(draft(64))).toBe('The table holds at most 64 terrain pieces: remove some first.');
    expect(cannotAddTerrain(draft(60), 5)).toBe('These 5 pieces would take the table past 64 terrain pieces (it has 60): remove some first.');
    expect(cannotClearTerrain(draft(0))).toBe('There is no terrain to clear.');
    expect(cannotClearTerrain(draft(1))).toBeNull();
    expect(cannotAddMarker(draft(0, Array.from({ length: 32 }, () => marker(1, 1, 1))))).toBe('The table holds at most 32 markers: remove some first.');
  });

  it('freeSpot takes the centre, then the first free ring spot', () => {
    expect(freeSpot(size, [], 3)).toEqual({ x: 36, y: 24, z: 0 });
    expect(freeSpot(size, [marker(36, 24, 3)], 3)).toEqual({ x: 42, y: 24, z: 0 });
  });

  it('beside stays right of the table', () => {
    expect(beside(size, () => 0)).toEqual({ x: 76, y: 4, z: 0 });
    expect(beside(size, () => 0.999)).toEqual({ x: 92, y: 28, z: 0 });
    for (let i = 0; i < 20; i++) expect(beside(size).x).toBeGreaterThan(72);
  });
});
