import { describe, expect, it } from 'vitest';
import { buildTree, descendantIds, filterAssets, moveTargets } from '../src/lib/library/folders';
import type { AssetDto, FolderDto } from '../src/lib/api/types';

const folders: FolderDto[] = [
  { id: 'orks', name: 'Orks', assetIds: ['a'] }, { id: 'boyz', parentId: 'orks', name: 'Boyz', assetIds: ['b'] },
  { id: 'winter', name: 'Winter', assetIds: ['b'] }, { id: 'nobz', parentId: 'orks', name: 'Nobz', assetIds: [] },
];
const asset = (id: string, kind: AssetDto['kind'] = 'miniature', a = 1.26): AssetDto => ({ id, name: id, kind, hash: id, byteSize: 1, contentType: 'model/gltf-binary', uploaded: true, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, shared: false, modelledBase: kind === 'miniature' ? { shape: 'circle', a, b: a } : undefined });

describe('folders', () => {
  it('builds a sorted tree and finds descendants', () => {
    const tree = buildTree(folders);
    expect(tree.map((n) => n.folder.name)).toEqual(['Orks', 'Winter']);
    expect(tree[0]!.children.map((n) => n.folder.name)).toEqual(['Boyz', 'Nobz']);
    expect(descendantIds(folders, 'orks')).toEqual(new Set(['orks', 'boyz', 'nobz']));
  });
  it('offers every folder outside the moving one’s subtree as a move target', () => {
    expect(moveTargets(folders, 'orks').map((f) => f.id)).toEqual(['winter']);
    expect(moveTargets(folders, 'boyz').map((f) => f.id)).toEqual(['nobz', 'orks', 'winter']);
  });
  it('filters by folder subtree, kind and modelled base', () => {
    const assets = [asset('a'), asset('b'), asset('c', 'terrain'), asset('d', 'miniature', 40 / 25.4)];
    expect(filterAssets(assets, folders, { folderId: 'orks' }).map((x) => x.id)).toEqual(['a', 'b']);
    expect(filterAssets(assets, folders, { folderId: null, kind: 'terrain' }).map((x) => x.id)).toEqual(['c']);
    expect(filterAssets(assets, folders, { folderId: null, base: { shape: 'circle', a: 40 / 25.4, b: 40 / 25.4 } }).map((x) => x.id)).toEqual(['d']);
  });
});
