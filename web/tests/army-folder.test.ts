import { describe, expect, it } from 'vitest';
import type { AssetDto, FolderDto } from '../src/lib/api/types';
import { folderKey, loadFolder, pickerAssets, saveFolder } from '../src/lib/armies/folder';

const asset = (id: string): AssetDto => ({ id, name: id, kind: 'miniature', uploaded: true } as AssetDto);
const folders: FolderDto[] = [
  { id: 'orcs', name: 'Orcs', assetIds: ['boy'] },
  { id: 'nobs', parentId: 'orcs', name: 'Nobs', assetIds: ['nob'] },
  { id: 'elves', name: 'Elves', assetIds: ['elf'] },
];
const all = ['boy', 'nob', 'elf', 'loose'].map(asset);

function memory(): Storage {
  const m = new Map<string, string>();
  return { getItem: (k) => m.get(k) ?? null, setItem: (k, v) => void m.set(k, v), removeItem: (k) => void m.delete(k), clear: () => m.clear(), key: () => null, get length() { return m.size; } };
}

describe('the army page picker folder', () => {
  it('shows a folder and its sub-folders', () => {
    expect(pickerAssets(all, folders, 'orcs', null).map((a) => a.id)).toEqual(['boy', 'nob']);
  });
  it('shows everything with no folder', () => {
    expect(pickerAssets(all, folders, null, null)).toHaveLength(4);
  });
  it('keeps the mesh a model already has, outside the folder', () => {
    expect(pickerAssets(all, folders, 'elves', 'boy').map((a) => a.id)).toEqual(['elf', 'boy']);
  });
  it('remembers the folder per army', () => {
    const s = memory();
    saveFolder('army-1', 'orcs', s);
    expect(loadFolder('army-1', folders, s)).toBe('orcs');
    expect(loadFolder('army-2', folders, s)).toBeNull();
    expect(s.getItem(folderKey('army-1'))).toBe('orcs');
  });
  it('forgets a folder that no longer exists, and All clears it', () => {
    const s = memory();
    saveFolder('army-1', 'gone', s);
    expect(loadFolder('army-1', folders, s)).toBeNull();
    saveFolder('army-1', null, s);
    expect(s.getItem(folderKey('army-1'))).toBeNull();
  });
  it('works without storage', () => {
    const broken = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); }, removeItem: () => { throw new Error('blocked'); } } as unknown as Storage;
    expect(loadFolder('army-1', folders, broken)).toBeNull();
    expect(() => saveFolder('army-1', 'orcs', broken)).not.toThrow();
  });
});
