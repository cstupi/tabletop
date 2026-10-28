import { describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import type { TableState, TerrainPlacement, Volume, Yaw } from '../src/lib/protocol/wire.generated';
import {
  clearTerrain, objective, overrideVolume, removeFromDraft, setupMenu, turnInDraft, withDeclaration, withFootprint, withNotes,
  zoneShapes,
} from '../src/lib/setup/edits';

const volume = (over: Partial<Volume> = {}): Volume => ({
  kind: 'solid', footprint: { kind: 'rect', a: 4, b: 2 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 2,
  declarations: 'cover', ...over,
});
const piece = (id: string, yaw = 0): TerrainPlacement => ({
  id, mesh: { builtin: 'barricade' }, position: { x: 10, y: 10, z: 0 }, yaw: { degrees: yaw }, scale: 1,
  volumes: [{ id: `${id}-v1`, volume: volume() }, { id: `${id}-v2`, volume: volume({ footprint: { kind: 'circle', a: 2, b: 2 } }) }],
});
const draft = (): TableState => ({
  id: 'd', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, terrain: [piece('p1', 10)],
  markers: [{ id: 'm1', kind: 'objective', position: { x: 36, y: 24, z: 0 }, yaw: { degrees: 0 }, diameter: 3, visual: { builtin: 'objective-ring' } }],
  zones: [{ id: 'z1', shape: 'frontline/1', teamOrdinal: 1 }],
});
const fakeGeometry = () => {
  const calls: { id: string; yaw: Yaw }[] = [];
  const g = { turnPiece: (_t: unknown, id: string, yaw: Yaw) => { calls.push({ id, yaw }); return { x: 11, y: 12, z: 0 }; } } as unknown as Geometry;
  return { g, calls };
};

describe('setup edits', () => {
  it('zoneShapes gives two, four for free-for-all', () => {
    expect(zoneShapes('spearhead')).toEqual(['spearhead/1', 'spearhead/2']);
    expect(zoneShapes('free-for-all')).toHaveLength(4);
  });

  it("objective builds Godot's marker", () => {
    const o = objective({ width: 72, length: 48 }, []);
    expect(o.marker).toMatchObject({ kind: 'objective', diameter: 3, position: { x: 36, y: 24, z: 0 }, visual: { builtin: 'objective-ring' } });
    expect(o.marker.label).toBeUndefined();
    expect(objective({ width: 72, length: 48 }, [o.marker]).marker.id).not.toBe(o.marker.id);
  });

  it('turnInDraft wraps the facing and asks turnPiece', () => {
    const { g, calls } = fakeGeometry();
    const turn = turnInDraft(g, draft(), 'p1', -15)!;
    expect(calls).toEqual([{ id: 'p1', yaw: { degrees: 345 } }]);
    expect(turn).toEqual({ kind: 'move_terrain', placementId: 'p1', position: { x: 11, y: 12, z: 0 }, yaw: { degrees: 345 } });
    expect(turnInDraft(g, draft(), 'nope', 5)).toBeNull();
    expect(turnInDraft(g, draft(), 'p1', Number.NaN)).toBeNull();
  });

  it('setupMenu for terrain, a marker, and an unknown id', () => {
    const { g, calls } = fakeGeometry();
    const menu = setupMenu(g, draft(), 'p1');
    expect(menu.map((m) => m.label)).toEqual(['Turn left 15°', 'Turn right 15°', 'Set facing…', 'Remove']);
    expect(menu[0]!.edit('')).toMatchObject({ yaw: { degrees: 25 } });
    expect(menu[1]!.edit('')).toMatchObject({ yaw: { degrees: 355 } });
    expect(menu[2]!.edit('90')).toMatchObject({ yaw: { degrees: 90 } });
    expect(menu[2]!.edit('abc')).toBeNull();
    expect(menu[3]!.edit('')).toEqual({ kind: 'remove_terrain', placementId: 'p1' });
    expect(calls).toHaveLength(3);
    expect(setupMenu(g, draft(), 'm1').map((m) => m.label)).toEqual(['Remove']);
    expect(setupMenu(g, draft(), 'm1')[0]!.edit('')).toEqual({ kind: 'remove_setup_marker', markerId: 'm1' });
    expect(setupMenu(g, draft(), 'zzz')).toEqual([]);
    expect(removeFromDraft(draft(), 'zzz')).toBeNull();
  });

  it('overrideVolume changes only that volume', () => {
    const p = piece('p1');
    const edit = overrideVolume(p, 'p1-v1', (v) => ({ ...v, top: 9 }));
    expect(edit.kind).toBe('override_terrain_volumes');
    expect(edit.volumes.map((v) => v.id)).toEqual(['p1-v1', 'p1-v2']);
    expect(edit.volumes[0]!.volume.top).toBe(9);
    expect(edit.volumes[1]).toEqual(p.volumes[1]);
  });

  it('volume notes are clipped to 500 and empty notes clear', () => {
    expect(withNotes('x'.repeat(600))(volume()).notes).toHaveLength(500);
    const cleared = withNotes('')(volume({ notes: 'old' }));
    expect(cleared.notes).toBeUndefined();
    expect('notes' in cleared).toBe(false);
  });

  it('declarations are written in canonical order, none when empty', () => {
    expect(withDeclaration('impassable', true)(volume()).declarations).toBe('impassable, cover');
    expect(withDeclaration('cover', false)(volume()).declarations).toBe('none');
  });

  it('a circle footprint edit sets both sizes', () => {
    expect(withFootprint('a', 5)(volume({ footprint: { kind: 'circle', a: 2, b: 2 } })).footprint).toEqual({ kind: 'circle', a: 5, b: 5 });
    expect(withFootprint('b', 7)(volume()).footprint).toEqual({ kind: 'rect', a: 4, b: 7 });
  });

  it('clearTerrain keeps markers and zones', () => {
    const d = draft();
    const edit = clearTerrain(d);
    expect(edit.draft.terrain).toEqual([]);
    expect(edit.draft.markers).toEqual(d.markers);
    expect(edit.draft.zones).toEqual(d.zones);
    expect(edit.draft.size).toEqual(d.size);
  });
});
