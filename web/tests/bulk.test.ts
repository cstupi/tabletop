import { describe, expect, it } from 'vitest';
import { chunk, folderOptions, holdersIn, runBulk, uploadTarget } from '../src/lib/library/bulk';
import type { FolderDto } from '../src/lib/api/types';

const folders: FolderDto[] = [
  { id: 'orks', name: 'Orks', assetIds: ['a'] }, { id: 'boyz', parentId: 'orks', name: 'Boyz', assetIds: ['b', 'c'] },
  { id: 'winter', name: 'Winter', assetIds: ['b'] },
];

describe('bulk', () => {
  it('files an upload into the selected folder, or none under All or a folder that is gone', () => {
    expect(uploadTarget(folders, 'boyz')).toBe('boyz');
    expect(uploadTarget(folders, null)).toBeNull();
    expect(uploadTarget(folders, 'deleted')).toBeNull();
  });
  it('lists folders in tree order with their paths', () => {
    expect(folderOptions(folders)).toEqual([{ id: 'orks', label: 'Orks' }, { id: 'boyz', label: 'Orks / Boyz' }, { id: 'winter', label: 'Winter' }]);
  });
  it('finds which folders of a subtree hold the selected assets, skipping one', () => {
    expect(holdersIn(folders, 'orks', ['a', 'b', 'x'])).toEqual([{ folderId: 'orks', ids: ['a'] }, { folderId: 'boyz', ids: ['b'] }]);
    expect(holdersIn(folders, 'orks', ['b'], 'boyz')).toEqual([]);
    expect(holdersIn(folders, 'winter', ['a'])).toEqual([]);
  });
  it('chunks to the server cap', () => {
    expect(chunk(['a', 'b', 'c'], 2)).toEqual([['a', 'b'], ['c']]);
    expect(chunk([], 2)).toEqual([]);
  });
  it('sends chunks in turn and gathers done, not found and failures', async () => {
    const sent: string[][] = [];
    const r = await runBulk(['a', 'b', 'c', 'd', 'e'], async (ids) => {
      sent.push(ids);
      if (ids.includes('e')) throw new Error('offline');
      return { done: ids.filter((i) => i !== 'b'), notFound: ids.filter((i) => i === 'b') };
    }, 2);
    expect(sent).toEqual([['a', 'b'], ['c', 'd'], ['e']]);
    expect(r).toEqual({ done: ['a', 'c', 'd'], notFound: ['b'], failed: ['e'], error: expect.any(Error) });
  });
});
