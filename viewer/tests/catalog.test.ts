import { describe, expect, it } from 'vitest';
import packed from '@builtin-catalog';
import type { Api } from '../src/lib/api/client';
import { declarationsWire, loadCatalog, nameOf, parseCatalog, placeAsset, placeBuiltin } from '../src/lib/setup/catalog';

const base = (terrain: unknown[]) => ({ meshes: ['m'], textures: ['grass'], terrain });
const piece = (over: object = {}) => ({ id: 'x', label: 'X', mesh: 'm', kind: 'solid', footprint: { kind: 'rect', a: 2, b: 3 }, top: 1, declarations: [], ...over });

describe('the built-in catalog', () => {
  it('parses the packed catalog', () => {
    const c = parseCatalog(packed);
    expect(c.terrain.size).toBe(38);
    expect(c.textures[0]).toBe('grass');
    expect(c.terrain.get('barricade')!.volume).toMatchObject({
      kind: 'solid', footprint: { kind: 'rect', a: 3, b: 1 }, bottom: 0, top: 1, declarations: 'cover, difficult',
    });
  });

  it('a circle keeps a as the radius', () => {
    const v = parseCatalog(packed).terrain.get('hill')!.volume;
    expect(v.footprint).toMatchObject({ kind: 'circle', a: 3 });
  });

  it('faults name the entry', () => {
    expect(() => parseCatalog(base([piece({ declarations: ['lava'] })]))).toThrow("Terrain 'x' declares 'lava', which is not a terrain declaration.");
    expect(() => parseCatalog(base([piece({ kind: 'gas' })]))).toThrow("Terrain 'x' has kind 'gas', not solid or area.");
    expect(() => parseCatalog(base([piece({ top: 0 })]))).toThrow("Terrain 'x' has top 0, not above 0 and up to 144\".");
    expect(() => parseCatalog(base([piece(), piece()]))).toThrow("Terrain 'x' is listed twice.");
    expect(() => parseCatalog(base([piece({ mesh: 'nope' })]))).toThrow("Terrain 'x' names mesh 'nope', which the catalog does not list.");
  });

  it('declarationsWire orders and joins', () => {
    expect(declarationsWire(['difficult', 'cover'])).toBe('cover, difficult');
    expect(declarationsWire([])).toBe('none');
  });

  it('placeBuiltin copies the volume with fresh ids', () => {
    const entry = parseCatalog(packed).terrain.get('barricade')!;
    const a = placeBuiltin(entry, { x: 1, y: 2, z: 0 }, { degrees: 0 });
    const b = placeBuiltin(entry, { x: 1, y: 2, z: 0 }, { degrees: 0 });
    expect(a.id).not.toBe(b.id);
    expect(a.volumes[0]!.id).not.toBe(b.volumes[0]!.id);
    expect(a.mesh).toEqual({ builtin: 'barricade' });
    expect(a.scale).toBe(1);
    expect(a.volumes[0]!.volume).toEqual(entry.volume);
  });

  it('placeAsset keeps the volumes with new ids at scale 1', () => {
    const entry = parseCatalog(packed).terrain.get('hill')!;
    const volumes = [{ id: 'old', volume: entry.volume }];
    const p = placeAsset('asset', volumes, { x: 0, y: 0, z: 0 });
    expect(p.mesh).toEqual({ assetId: 'asset' });
    expect(p.scale).toBe(1);
    expect(p.volumes[0]!.id).not.toBe('old');
    expect(p.volumes[0]!.volume).toEqual(entry.volume);
  });

  it('names a piece by its built-in label, else Terrain', () => {
    const c = parseCatalog(packed);
    const p = placeBuiltin(c.terrain.get('barricade')!, { x: 0, y: 0, z: 0 }, { degrees: 0 });
    expect(nameOf(p, c)).toBe('Barricade');
    expect(nameOf({ ...p, mesh: { assetId: 'a' } }, c)).toBe('Terrain');
  });

  it('loadCatalog falls back to the packed file on 404', async () => {
    const none = { builtinCatalog: async () => null } as unknown as Api;
    expect((await loadCatalog(none)).terrain.size).toBe(38);
    const served = { builtinCatalog: async () => base([piece()]) } as unknown as Api;
    expect((await loadCatalog(served)).terrain.size).toBe(1);
  });
});
