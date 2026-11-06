import { describe, expect, it } from 'vitest';
import { extentOf, pieceOutlines, sceneOfTable } from '../src/lib/board/table';
import type { TableState, TerrainPlacement, Volume } from '../src/lib/protocol/wire.generated';

const volume = (over: Partial<Volume> = {}): Volume => ({
  kind: 'solid', footprint: { kind: 'rect', a: 6, b: 6 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 4,
  declarations: 'cover, difficult', ...over,
});
const piece = (x: number, y: number, yaw: number, v: Volume = volume()): TerrainPlacement => ({
  id: 'p', mesh: { builtin: 'b' }, position: { x, y, z: 0 }, yaw: { degrees: yaw }, scale: 1, volumes: [{ id: 'v', volume: v }],
});
const table = (terrain: TerrainPlacement[]): TableState => ({
  id: 't', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, terrain, zones: [], markers: [],
});
const corners = (shape: unknown) => (shape as { points: { x: number; y: number }[] }).points
  .map((p) => [Math.round(p.x * 100) / 100, Math.round(p.y * 100) / 100]);

describe('the draft as a scene', () => {
  it('a draft becomes terrain, markers and zones', () => {
    const scene = sceneOfTable({
      ...table([piece(10, 10, 90)]),
      markers: [{ id: 'm', kind: 'objective', position: { x: 5, y: 6, z: 0 }, yaw: { degrees: 0 }, diameter: 3, visual: { builtin: 'o' } }],
      zones: [{ id: 'z', shape: 'frontline/1', teamOrdinal: 1 }],
    }, () => [[{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }]], () => 'Name');
    expect(scene.table).toEqual({ width: 72, length: 48 });
    expect(scene.terrain[0]).toMatchObject({ id: 'p', volumeId: 'v', label: 'Name', kind: 'solid', bottom: 0, top: 4 });
    expect(new Set(corners(scene.terrain[0]!.shape).map((c) => c.join(',')))).toEqual(new Set(['7,7', '13,7', '13,13', '7,13']));
    expect(scene.markers[0]).toMatchObject({ id: 'm', radius: 1.5, centre: { x: 5, y: 6 } });
    expect(scene.zones[0]).toMatchObject({ id: 'z', shape: 'frontline/1', teamOrdinal: 1 });
    expect(scene.zones[0]!.polygons[0]).toMatchObject({ kind: 'polygon' });
    expect(scene.models).toEqual([]);
  });

  it('markers carry the welcome\'s marker range as their reach, and none without it', () => {
    const draft: TableState = { ...table([]), markers: [{ id: 'm', kind: 'objective' as const, position: { x: 5, y: 6, z: 0 }, yaw: { degrees: 0 }, diameter: 3, visual: { builtin: 'o' } }] };
    expect(sceneOfTable(draft, () => [], () => 'Name', undefined, 3).markers[0]).toMatchObject({ radius: 1.5, reach: 3 });
    expect(sceneOfTable(draft, () => [], () => 'Name').markers[0]!.reach).toBeUndefined();
  });

  it('zone polygons are asked once per shape and table size for the same source', () => {
    const asked: string[] = [];
    const zones = (shape: string, size: { width: number; length: number }) => { asked.push(`${shape} ${size.width}x${size.length}`); return [[{ x: 0, y: 0 }]]; };
    const draft = { ...table([]), zones: [{ id: 'a', shape: 'frontline/1', teamOrdinal: 1 }, { id: 'b', shape: 'frontline/2', teamOrdinal: 2 }] };
    const first = sceneOfTable(draft, zones, () => '');
    const again = sceneOfTable(draft, zones, () => '');
    expect(again.zones[0]!.polygons).toEqual(first.zones[0]!.polygons);
    expect(asked).toEqual(['frontline/1 72x48', 'frontline/2 72x48']);
    sceneOfTable({ ...draft, size: { width: 44, length: 30 } }, zones, () => '');
    expect(asked).toHaveLength(4);
  });

  it('zone polygons are asked with the welcome\'s legs, and other legs are asked again', () => {
    const asked: unknown[] = [];
    const zones = (_shape: string, _size: { width: number; length: number }, legs?: { sideBattleLong: number; sideBattleShort: number }) => {
      asked.push(legs); return [[{ x: 0, y: 0 }]];
    };
    const draft = { ...table([]), zones: [{ id: 'a', shape: 'side-battle/1', teamOrdinal: 1 }] };
    const legs = { sideBattleLong: 2 / 3, sideBattleShort: 0.75 };
    sceneOfTable(draft, zones, () => '', legs);
    sceneOfTable(draft, zones, () => '', legs);
    sceneOfTable(draft, zones, () => '');
    expect(asked).toEqual([legs, undefined]);
  });

  it('declarations read from the wire string', () => {
    const scene = sceneOfTable(table([piece(10, 10, 0), piece(1, 1, 0, volume({ declarations: 'none' }))]), () => [], () => '');
    expect(scene.terrain[0]!.declarations).toEqual(['cover', 'difficult']);
    expect(scene.terrain[1]!.declarations).toEqual([]);
  });

  it('extentOf widens to a piece beside the table', () => {
    const inside = extentOf(sceneOfTable(table([piece(10, 10, 0)]), () => [], () => ''));
    expect(inside).toEqual({ minX: 0, minY: 0, maxX: 72, maxY: 48 });
    const beside = extentOf(sceneOfTable(table([piece(80, 8, 0)]), () => [], () => ''));
    expect(beside.maxX).toBeGreaterThanOrEqual(83);
  });

  it('pieceOutlines moves and turns the footprint', () => {
    const p = piece(0, 0, 0, volume({ footprint: { kind: 'rect', a: 6, b: 1 } }));
    const [shape] = pieceOutlines(p, { position: { x: 20, y: 20, z: 0 }, yaw: { degrees: 90 } });
    expect(new Set(corners(shape).map((c) => c.join(',')))).toEqual(new Set(['19.5,17', '20.5,17', '20.5,23', '19.5,23']));
  });
});
