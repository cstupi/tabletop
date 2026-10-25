import { describe, expect, it } from 'vitest';
import type { TableState, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import { builtinLabel, layoutLabel, loadEdit, randomEdit, randomRequest, randomSummary, saveRequest } from '../src/lib/setup/layouts';

const piece = (id: string): TerrainPlacement => ({ id, mesh: { builtin: 'b' }, position: { x: 1, y: 1, z: 0 }, yaw: { degrees: 0 }, scale: 1, volumes: [] });
const draft = (zones = true): TableState => ({
  id: 'd', size: { width: 60, length: 44 }, texture: { builtin: 'snow' }, terrain: [piece('old')],
  markers: [{ id: 'm', kind: 'objective', position: { x: 3, y: 3, z: 0 }, yaw: { degrees: 0 }, diameter: 3, visual: { builtin: 'objective-ring' } }],
  zones: zones ? [{ id: 'z1', shape: 'frontline/1', teamOrdinal: 1 }, { id: 'z2', shape: 'frontline/2', teamOrdinal: 2 }] : [],
});

describe('layouts', () => {
  it('loading a layout replaces the whole draft, size included', () => {
    const layout: TableState = { ...draft(), id: 'l', size: { width: 72, length: 48 } };
    expect(loadEdit(layout)).toEqual({ kind: 'replace_draft', draft: layout });
  });

  it('randomRequest carries size and zones', () => {
    expect(randomRequest(draft(), 'ruins')).toEqual({ size: { width: 60, length: 44 }, planet: 'ruins', zones: draft().zones });
  });

  it('randomRequest leaves zones out when the draft has none', () => {
    const request = randomRequest(draft(false), 'death');
    expect(request).toEqual({ size: { width: 60, length: 44 }, planet: 'death' });
    expect('zones' in request).toBe(false);
    expect('seed' in request).toBe(false);
  });

  it('randomEdit replaces only the terrain', () => {
    const d = draft();
    const edit = randomEdit(d, { seed: 1, terrain: [piece('a'), piece('b')], unplaced: [], unlisted: [] });
    expect(edit.draft.terrain.map((t) => t.id)).toEqual(['a', 'b']);
    expect(edit.draft.texture).toEqual(d.texture);
    expect(edit.draft.markers).toEqual(d.markers);
    expect(edit.draft.zones).toEqual(d.zones);
    expect(edit.draft.size).toEqual(d.size);
  });

  it('randomSummary names what did not fit', () => {
    expect(randomSummary({ seed: 1, terrain: [piece('a')], unplaced: [], unlisted: [] })).toBe('Placed 1 piece.');
    expect(randomSummary({ seed: 1, terrain: [piece('a'), piece('b')], unplaced: ['hill', 'forest'], unlisted: ['x'] }))
      .toBe('Placed 2 pieces. 2 did not fit: hill, forest. Not in the catalog: x.');
  });

  it("saveRequest refuses a blank name with Godot's text and trims a good one", () => {
    expect(saveRequest(draft(), '   ')).toEqual({ problem: 'A layout name is required.' });
    expect(saveRequest(draft(), 'x'.repeat(65))).toEqual({ problem: 'A layout name is at most 64 characters.' });
    const ok = saveRequest(draft(), '  Mine ');
    expect('table' in ok && ok.table.name).toBe('Mine');
  });

  it('layoutLabel', () => {
    expect(layoutLabel({ id: 'l', name: 'Mine', size: { width: 72, length: 48 }, terrain: 12, markers: 4 })).toBe('Mine (72×48, 12 terrain)');
    expect(builtinLabel({ ...draft(), name: 'Death' })).toBe('Death (built-in, 60×44, 1 terrain)');
  });
});
