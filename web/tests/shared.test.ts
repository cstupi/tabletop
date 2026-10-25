import { afterEach, describe, expect, it, vi } from 'vitest';
import { alreadyHeld, appendPage, folderSharing, markShared } from '../src/lib/library/shared';
import { api } from '../src/lib/api/client';
import type { AssetDto, FolderDto, SharedAssetDto } from '../src/lib/api/types';

const asset = (id: string, shared = false, hash = id): AssetDto => ({
  id, name: id, kind: 'miniature', hash, byteSize: 1, contentType: 'model/gltf-binary', uploaded: true, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, shared,
});
const item = (id: string, hash = id): SharedAssetDto => ({
  id, name: id, kind: 'miniature', hash, byteSize: 1, contentType: 'model/gltf-binary', scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, ownerName: 'A', folders: [], sharedAt: '',
});
const folders: FolderDto[] = [{ id: 'orks', name: 'Orks', assetIds: ['a'] }, { id: 'boyz', parentId: 'orks', name: 'Boyz', assetIds: ['b'] }, { id: 'other', name: 'Other', assetIds: ['c'] }];

afterEach(() => vi.unstubAllGlobals());

describe('shared', () => {
  it('splits a folder and its sub-folders by share state', () => {
    expect(folderSharing([asset('a', true), asset('b'), asset('c')], folders, 'orks')).toEqual({ shared: ['a'], unshared: ['b'] });
  });
  it('marks only the ids the server answered', () => {
    expect(markShared([asset('a'), asset('b')], ['b'], true).map((a) => a.shared)).toEqual([false, true]);
  });
  it('finds shared items whose bytes are already in the library', () => {
    expect(alreadyHeld([item('x', 'h1'), item('y', 'h2')], [asset('mine', false, 'h2')])).toEqual(new Set(['y']));
  });
  it('appends a page without repeating items', () => {
    expect(appendPage([item('x')], [item('x'), item('y')]).map((i) => i.id)).toEqual(['x', 'y']);
  });
  it('calls the shared endpoints', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify({ items: [], copied: [], notFound: [], done: [] }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', f);
    await api.shared.list({ q: 'ork boy', kind: 'miniature' });
    await api.shared.copy(['x'], null);
    await api.assets.share(['a']);
    const calls = f.mock.calls as unknown as [string, RequestInit][];
    expect(calls.map(([u, i]) => `${i.method ?? 'GET'} ${u}`)).toEqual(['GET /v1/shared-assets?q=ork%20boy&kind=miniature', 'POST /v1/shared-assets/copy', 'POST /v1/assets/share']);
    expect(JSON.parse(calls[1]![1].body as string)).toEqual({ assetIds: ['x'], folderId: null });
  });
});
