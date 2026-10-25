import { describe, expect, it } from 'vitest';
import packed from '@builtin-catalog';
import type { TableState } from '../src/lib/protocol/wire.generated';
import { parseCatalog } from '../src/lib/setup/catalog';
import { DECLARATION_WORDS, facingOf, inches, markerCardOf, terrainCardOf } from '../src/lib/setup/terrainCard';

const catalog = parseCatalog(packed);
const table = (): TableState => ({
  id: 't', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, zones: [],
  markers: [{ id: 'm1', kind: 'token', position: { x: 1, y: 1, z: 0 }, yaw: { degrees: 0 }, diameter: 1, label: 'token', visual: { builtin: 'chip' } }],
  terrain: [{
    id: 'p1', mesh: { builtin: 'barricade' }, position: { x: 10, y: 10, z: 1 }, yaw: { degrees: -15 }, scale: 2,
    volumes: [
      { id: 'v1', volume: { kind: 'solid', footprint: { kind: 'rect', a: 3, b: 1 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 3, declarations: 'cover, difficult' } },
      { id: 'v2', volume: { kind: 'area', footprint: { kind: 'circle', a: 2, b: 2 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 1, declarations: 'none', notes: 'n' } },
    ],
  }],
});

describe('the terrain card', () => {
  it('the card shows facing 0–359, heights and Open for no declaration', () => {
    const card = terrainCardOf(table(), 'setup', 'p1', catalog)!;
    expect(card).toMatchObject({ name: 'Barricade', source: 'Built-in terrain', facing: 345, scale: 2, height: '1–7"' });
    expect(card.volumes[0]).toMatchObject({ kind: 'Solid', footprint: '6 × 2"', heights: '1–7"', declarations: ['Cover', 'Difficult'], notes: null });
    expect(card.volumes[1]).toMatchObject({ footprint: '8" across', declarations: ['Open'], notes: 'n' });
  });

  it('the card is editable in setup only', () => {
    expect(terrainCardOf(table(), 'setup', 'p1', catalog)!.editable).toBe(true);
    expect(terrainCardOf(table(), 'playing', 'p1', catalog)!.editable).toBe(false);
    expect(terrainCardOf(table(), 'lobby', 'p1', catalog)).toBeNull();
    expect(terrainCardOf(table(), 'setup', 'zzz', catalog)).toBeNull();
    expect(terrainCardOf(table(), 'setup', null, catalog)).toBeNull();
  });

  it('a marker card is setup only', () => {
    expect(markerCardOf(table(), 'setup', 'm1')).toEqual({ pieceId: 'm1', name: 'Token', diameter: '1" across', label: null });
    const named = table();
    named.markers[0] = { ...named.markers[0]!, label: 'Relic' };
    expect(markerCardOf(named, 'setup', 'm1')!.label).toBe('Relic');
    expect(markerCardOf(table(), 'playing', 'm1')).toBeNull();
  });

  it('words, inches and facing', () => {
    expect(DECLARATION_WORDS).toHaveLength(7);
    expect(DECLARATION_WORDS.map(([, w]) => w)).toContain('Blocks line of sight');
    const library = table();
    library.terrain[0] = { ...library.terrain[0]!, mesh: { assetId: '0198-abcd' } };
    expect(terrainCardOf(library, 'setup', 'p1', catalog)!.source).toBe("From a player's library");
    expect(inches(2.25)).toBe('2.3');
    expect(inches(3)).toBe('3');
    expect(facingOf({ degrees: 360 })).toBe(0);
    expect(facingOf({ degrees: -1 })).toBe(359);
  });
});
