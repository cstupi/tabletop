import { describe, expect, it } from 'vitest';
import type { Army, ArmyMeshUse, AssetDto, Base, FolderDto, ModelSlot, Unit } from '../src/lib/api/types';
import { buildIndex, search, stem, wordScore, words } from '../src/lib/meshpicker/search';
import { suggest } from '../src/lib/meshpicker/suggest';
import { changeLine, defaultScope, modelName, scopeChange, scopePatch, scopes } from '../src/lib/meshpicker/scope';
import { gridMove } from '../src/lib/meshpicker/nav';
import { RECENT_MAX, loadLastFolder, loadRecent, pushRecent, saveLastFolder } from '../src/lib/meshpicker/memory';
import { circle } from '../src/lib/units/bases';

const asset = (id: string, name: string, modelledBase?: Base): AssetDto =>
  ({ id, name, kind: 'miniature', uploaded: true, modelledBase } as AssetDto);
const folders: FolderDto[] = [
  { id: 'orcs', name: 'Orcs', assetIds: ['boss'] },
  { id: 'boyz', parentId: 'orcs', name: 'Boyz', assetIds: ['boy1', 'boy2'] },
  { id: 'elves', name: 'Elves', assetIds: ['elf'] },
];
const library = [
  asset('boss', 'OrcBoss_02', circle(32)), asset('boy1', 'orc-boy-1', circle(25)), asset('boy2', 'Boy with Choppa', circle(32)),
  asset('elf', 'Elf Archer'), asset('loose', 'Sentry Gun'),
];
const index = buildIndex(library, folders);
const ids = (xs: { entry: { asset: AssetDto } }[]) => xs.map((x) => x.entry.asset.id);

describe('words', () => {
  it('splits camelCase, digits, underscores and dashes, lower-cased, stemmed', () => {
    expect(words('OrcBoss_02')).toEqual(['orc', 'bos', '02']);
    expect(words('orc-boy-1')).toEqual(['orc', 'boy', '1']);
    expect(words('Orc Boyz')).toEqual(['orc', 'boy']);
    expect(words('Boss3D')).toEqual(['bos', '3', 'd']);
  });
  it('drops a trailing s or z from words of four letters or more', () => {
    expect([stem('boyz'), stem('orcs'), stem('gus'), stem('box')]).toEqual(['boy', 'orc', 'gus', 'box']);
  });
});

describe('wordScore', () => {
  it('ranks exact, prefix, inside, one edit', () => {
    expect(wordScore('boy', 'boy')).toBe(3);
    expect(wordScore('bo', 'boy')).toBe(2);
    expect(wordScore('rch', 'archer')).toBe(1);
    expect(wordScore('archr', 'archer')).toBe(0.5);
    expect(wordScore('arhc', 'archer')).toBe(0.5);     // a typo while typing: within one edit of the word's start
    expect(wordScore('xyz', 'archer')).toBe(0);
    expect(wordScore('boi', 'boy')).toBe(0);           // under four letters, no edits
    expect([wordScore('2', '02'), wordScore('0', '02'), wordScore('3', '02')]).toEqual([3, 2, 0]);   // numbers: equal value, or a typed prefix
  });
});

describe('search', () => {
  it('matches every query word against name and folder words', () => {
    expect(ids(search(index, 'orc boss'))).toEqual(['boss']);
    expect(ids(search(index, 'boyz')).sort()).toEqual(['boy1', 'boy2']);
    expect(ids(search(index, 'ORC'))).toEqual(['boy1', 'boss', 'boy2']);   // boy2 by its folder's parent, Orcs
    expect(ids(search(index, 'elf gun'))).toEqual([]);
  });
  it('ranks name words above folder words', () => {
    expect(ids(search(index, 'orc'))[2]).toBe('boy2');
    expect(search(index, 'orc')[0]!.score).toBeGreaterThan(search(index, 'orc')[2]!.score);
  });
  it('keeps to the folder in view and its sub-folders', () => {
    expect(ids(search(index, '', { folder: 'orcs' })).sort()).toEqual(['boss', 'boy1', 'boy2']);
    expect(ids(search(index, '', { folder: 'boyz' })).sort()).toEqual(['boy1', 'boy2']);
    expect(ids(search(index, 'elf', { folder: 'orcs' }))).toEqual([]);
  });
  it('breaks ties by fitting the base, then recent, then name', () => {
    expect(ids(search(index, 'boy', { base: circle(32) }))).toEqual(['boy2', 'boy1']);
    expect(ids(search(index, 'boy', { base: circle(25) }))).toEqual(['boy1', 'boy2']);
    expect(ids(search(index, 'boy', { recent: ['boy2'] }))).toEqual(['boy2', 'boy1']);
    expect(ids(search(index, ''))).toEqual(['boy2', 'elf', 'boy1', 'boss', 'loose']);
  });
  it('searches 1000 assets in a few milliseconds', () => {
    const names = ['Orc', 'Boss', 'Boy', 'Elf', 'Archer', 'Knight', 'Dragon', 'Gun', 'Walker', 'Trooper', 'Sergeant', 'Rider'];
    const big = Array.from({ length: 1000 }, (_, i) => asset(`a${i}`, `${names[i % 12]}${names[(i * 7) % 12]}_${i}`, circle(25 + (i % 3) * 7)));
    const bigFolders: FolderDto[] = names.map((n, i) => ({ id: `f${i}`, name: `${n} models`, assetIds: big.filter((_, j) => j % 12 === i).map((a) => a.id) }));
    const bigIndex = buildIndex(big, bigFolders);
    for (const q of ['orc', 'kni']) search(bigIndex, q);   // warm
    const queries = ['o', 'or', 'orc', 'orc b', 'orc bos', 'drgaon', 'walker 12', 'sergeant rider', ''];
    const t0 = performance.now();
    const runs = 20;
    for (let r = 0; r < runs; r++) for (const q of queries) search(bigIndex, q, { base: circle(32) });
    const each = (performance.now() - t0) / (runs * queries.length);
    console.log(`search over 1000 assets: ${each.toFixed(3)} ms per query`);
    expect(each).toBeLessThan(5);
    expect(search(bigIndex, 'drgaon').length).toBeGreaterThan(0);
  });
});

const slot = (id: string, name: string, assetId?: string): ModelSlot =>
  ({ id, ordinal: 0, name, stats: {}, properties: [], base: circle(32), height: 1.5, assetId, weapons: [] });
const unit = (id: string, name: string, models: ModelSlot[]): Unit => ({ id, ordinal: 0, name, stats: {}, properties: [], models });

describe('suggest', () => {
  const lib = [asset('boss', 'Orc Boss'), asset('nob', 'Big Nob'), asset('boy', 'Orc Boy'), asset('elf', 'Elf Archer'), asset('choppa', 'Choppa Lad')];
  const idx = buildIndex(lib, []);
  const mob = unit('u1', 'Orc Boyz', [slot('s1', 'Boss'), slot('s2', 'Orc Boyz', 'choppa'), slot('s3', 'Orc Boyz', 'choppa')]);
  it('scores this unit\'s other meshes, other armies\' meshes for the same unit and name matches together', () => {
    const uses: ArmyMeshUse[] = [{ unitName: 'Orc Boyz', modelName: 'Boss', assetId: 'nob', slots: 2 }];
    const got = suggest({ index: idx, unit: mob, slots: [mob.models[0]!], uses, limit: 8 });
    expect(got.map((s) => [s.entry.asset.id, s.reason, s.label])).toEqual([
      ['boss', 'name', 'name'], ['choppa', 'unit', 'this unit'], ['nob', 'army', 'Orc Boyz'], ['boy', 'name', 'name'],
    ]);
  });
  it('leaves out the row\'s own mesh and assets that match nothing', () => {
    const got = suggest({ index: idx, unit: mob, slots: [mob.models[1]!], uses: [], limit: 8 });
    expect(got.map((s) => s.entry.asset.id)).not.toContain('choppa');
    expect(got.map((s) => s.entry.asset.id)).not.toContain('elf');
  });
  it('puts a mesh on another model of the same name in the unit first, and stops at the limit', () => {
    const got = suggest({ index: idx, unit: mob, slots: [mob.models[1]!], uses: [], limit: 8 });
    expect(got.map((s) => s.entry.asset.id)).toEqual(['boy', 'boss']);   // the row's own mesh, choppa, is left out
    const mixed = unit('u2', 'Orc Boyz', [slot('a', 'Boy', 'elf'), slot('b', 'Boy')]);
    expect(suggest({ index: idx, unit: mixed, slots: [mixed.models[1]!], uses: [], limit: 1 }).map((s) => s.entry.asset.id)).toEqual(['elf']);
  });
});

describe('scopes', () => {
  const boyz = unit('u1', 'Orc Boyz', [slot('b0', 'Boss', 'old'), slot('b1', 'Boy'), slot('b2', 'Boy', 'old'), slot('b3', 'Boy')]);
  const mob = unit('u2', 'Orc Mob', [slot('m0', 'Boy'), slot('m1', 'Boy')]);
  const solo = unit('u3', 'Warboss', [slot('w0', 'Warboss')]);
  const army = { id: 'a', name: 'Orcs', gameSystem: 'grimdark-future', edition: '3.6', source: 'x', units: [boyz, mob, solo] } as Army;
  it('offers this model, every one of its name in the unit and in the army', () => {
    const s = scopes(army, boyz, [boyz.models[1]!], 'single');
    expect(s.map((x) => [x.kind, x.label, x.slotIds])).toEqual([
      ['row', 'this model', ['b1']], ['type', 'every Boy in Orc Boyz', ['b1', 'b2', 'b3']], ['army', 'every Boy in the army', ['b1', 'b2', 'b3', 'm0', 'm1']],
    ]);
    expect(defaultScope(s, 'single')).toBe('row');   // one model's row sets that model alone
    expect(defaultScope(s, 'sub')).toBe('row');
  });
  it('leaves out a scope with the same models as a narrower one', () => {
    expect(scopes(army, solo, solo.models, 'single').map((x) => x.kind)).toEqual(['row']);
    expect(scopes(army, boyz, [boyz.models[0]!], 'single').map((x) => x.kind)).toEqual(['row']);
    const shared = scopes(army, mob, mob.models, 'shared');
    expect(shared.map((x) => [x.kind, x.label])).toEqual([['row', 'every model'], ['army', 'every Boy in the army']]);
    expect(defaultScope(shared, 'shared')).toBe('row');
    expect(defaultScope(scopes(army, solo, solo.models, 'single'), 'single')).toBe('row');
  });
  it('groups numbered slots by their model name, as the import names them', () => {
    expect(modelName('Orc Boyz 10')).toBe('Orc Boyz');
    expect(modelName('Big Boss Nob')).toBe('Big Boss Nob');
    const mob2 = unit('u4', 'Orc Boyz', [slot('n1', 'Orc Boyz 1'), slot('n2', 'Orc Boyz 2'), slot('w1', 'Warboss 1')]);
    const army2 = { ...army, units: [mob2, unit('u5', 'Orc Boyz', [slot('n3', 'Orc Boyz 1')])] } as Army;
    expect(scopes(army2, mob2, [mob2.models[1]!], 'single').map((x) => [x.label, x.slotIds])).toEqual([
      ['this model', ['n2']], ['every Orc Boyz in Orc Boyz', ['n1', 'n2']], ['every Orc Boyz in the army', ['n1', 'n2', 'n3']],
    ]);
  });
  it('patches one slot each, clearing for no mesh', () => {
    const s = scopes(army, boyz, [boyz.models[1]!], 'single')[1]!;
    expect(scopePatch(s, 'new').slots!.map((p) => [p.slotId, p.assetId, p.clearAsset])).toEqual([['b1', 'new', false], ['b2', 'new', false], ['b3', 'new', false]]);
    expect(scopePatch(s, null).slots!.every((p) => p.clearAsset && p.assetId === null && p.base === null && p.height === null)).toBe(true);
  });
  it('says what will change', () => {
    const wide = scopes(army, boyz, [boyz.models[1]!], 'single')[2]!;
    const c = scopeChange(army, wide, 'new');
    expect(c).toEqual({ models: 5, units: ['Orc Boyz', 'Orc Mob'], replaces: 1 });
    expect(changeLine(c)).toBe('5 models in Orc Boyz, Orc Mob · replaces 1 mesh');
    expect(changeLine(scopeChange(army, wide, 'old'))).toBe('5 models in Orc Boyz, Orc Mob');
    expect(changeLine({ models: 1, units: ['A'], replaces: 2 })).toBe('1 model in A · replaces 2 meshes');
  });
});

describe('gridMove', () => {
  const shape = { cols: 4, sections: [3, 10], page: 2 };   // suggested: 0..2; grid rows 3..6, 7..10, 11..12
  it('moves within and between the suggested row and the grid', () => {
    expect(gridMove(-1, 'ArrowDown', shape)).toBe(0);
    expect(gridMove(2, 'ArrowDown', shape)).toBe(5);
    expect(gridMove(6, 'ArrowUp', shape)).toBe(2);        // column 3 clamps to the suggested row's last
    expect(gridMove(4, 'ArrowUp', shape)).toBe(1);
    expect(gridMove(9, 'ArrowDown', shape)).toBe(12);     // the short last row
    expect(gridMove(12, 'ArrowDown', shape)).toBe(12);
    expect(gridMove(0, 'ArrowUp', shape)).toBe(0);
  });
  it('steps left and right through the whole list, pages, Home and End', () => {
    expect(gridMove(2, 'ArrowRight', shape)).toBe(3);
    expect(gridMove(0, 'ArrowLeft', shape)).toBe(0);
    expect(gridMove(1, 'PageDown', shape)).toBe(8);
    expect(gridMove(8, 'PageUp', shape)).toBe(1);
    expect(gridMove(5, 'Home', shape)).toBe(0);
    expect(gridMove(5, 'End', shape)).toBe(12);
    expect(gridMove(5, 'a', shape)).toBe(5);
  });
  it('works with no suggestions and with nothing', () => {
    expect(gridMove(0, 'ArrowDown', { cols: 3, sections: [0, 7], page: 1 })).toBe(3);
    expect(gridMove(0, 'ArrowDown', { cols: 3, sections: [0, 0], page: 1 })).toBe(-1);
  });
});

function memory(): Storage {
  const m = new Map<string, string>();
  return { getItem: (k) => m.get(k) ?? null, setItem: (k, v) => void m.set(k, v), removeItem: (k) => void m.delete(k), clear: () => m.clear(), key: () => null, get length() { return m.size; } };
}

describe('memory', () => {
  it('keeps recent picks newest first, once each, capped, per account', () => {
    const s = memory();
    pushRecent('me', 'a', s); pushRecent('me', 'b', s); pushRecent('me', 'a', s);
    expect(loadRecent('me', s)).toEqual(['a', 'b']);
    expect(loadRecent('you', s)).toEqual([]);
    for (let i = 0; i < 40; i++) pushRecent('me', `x${i}`, s);
    expect(loadRecent('me', s)).toHaveLength(RECENT_MAX);
    expect(loadRecent('me', s)[0]).toBe('x39');
  });
  it('remembers the folder, back to All when it is gone', () => {
    const s = memory();
    saveLastFolder('me', 'orcs', s);
    expect(loadLastFolder('me', folders, s)).toBe('orcs');
    expect(loadLastFolder('me', [], s)).toBeNull();
    saveLastFolder('me', null, s);
    expect(loadLastFolder('me', folders, s)).toBeNull();
  });
  it('survives blocked or garbled storage', () => {
    const blocked = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); }, removeItem: () => { throw new Error('blocked'); } } as unknown as Storage;
    expect(loadRecent('me', blocked)).toEqual([]);
    expect(pushRecent('me', 'a', blocked)).toEqual(['a']);
    expect(loadLastFolder('me', folders, blocked)).toBeNull();
    const garbled = memory(); garbled.setItem('tabletop.meshRecent.me', '{nope');
    expect(loadRecent('me', garbled)).toEqual([]);
  });
});
