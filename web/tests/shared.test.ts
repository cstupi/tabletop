import { afterEach, describe, expect, it, vi } from 'vitest';
import { copySummary, sharedFolders, usernameProblem } from '../src/lib/library/shared';
import { buildTree } from '../src/lib/library/folders';
import { api } from '../src/lib/api/client';
import type { FolderDto, SharedLibraryDto } from '../src/lib/api/types';

afterEach(() => vi.unstubAllGlobals());

describe('shared folders', () => {
  it('checks the username format', () => {
    expect(usernameProblem('Clay_S-1')).toBeNull();
    expect(usernameProblem('  clay  ')).toBeNull();
    expect(usernameProblem('')).toBe('Type a username.');
    for (const bad of ['ab', 'has space', 'x'.repeat(33), 'émile']) expect(usernameProblem(bad)).toMatch(/3 to 32/);
  });
  it('reads a shared tree as folders', () => {
    const view: SharedLibraryDto = { username: 'kim', name: 'Kim', assets: [], folders: [
      { id: 's', name: 'Shared', assetIds: [] }, { id: 'o', parentId: 's', name: 'Orks', assetIds: ['a'] }] };
    const tree = buildTree(sharedFolders(view));
    expect(tree.map((n) => n.folder.name)).toEqual(['Shared']);
    expect(tree[0]!.children[0]!.folder.assetIds).toEqual(['a']);
  });
  it('puts the Shared folder first in the library tree', () => {
    const folders: FolderDto[] = [{ id: 'a', name: 'Armies', assetIds: [] }, { id: 's', name: 'Zed', assetIds: [], shared: true }];
    expect(buildTree(folders).map((n) => n.folder.id)).toEqual(['s', 'a']);
  });
  it('summarises a copy', () => {
    const folder: FolderDto = { id: 'f', name: 'Orks', assetIds: [] };
    expect(copySummary({ assets: ['x'], folders: [], notFound: [] }, 'Kim', 'Borrowed')).toBe('Added 1 model from Kim to Borrowed.');
    expect(copySummary({ assets: ['x', 'y'], folders: [folder, folder], notFound: [] }, 'Kim', null)).toBe('Added 2 models and 2 folders from Kim to your library.');
    expect(copySummary({ assets: ['x'], folders: [], notFound: ['z'] }, 'Kim', null)).toBe('Added 1 model from Kim to your library. 1 model is no longer shared.');
  });
  it('calls the shared endpoints', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify({ assets: [], folders: [], notFound: [] }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', f);
    await api.account.setUsername('kim_1');
    await api.folders.createShared();
    await api.shared.browse('kim_1');
    await api.shared.download('kim_1', 'x');
    await api.shared.copy('kim_1', { assetIds: null, folderId: 'o', destinationId: 'd' });
    const calls = f.mock.calls as unknown as [string, RequestInit][];
    expect(calls.map(([u, i]) => `${i.method ?? 'GET'} ${u}`)).toEqual([
      'PUT /v1/account/username', 'POST /v1/folders/shared', 'GET /v1/shared/kim_1', 'GET /v1/shared/kim_1/assets/x/download', 'POST /v1/shared/kim_1/copy',
    ]);
    expect(JSON.parse(calls[0]![1].body as string)).toEqual({ username: 'kim_1' });
    expect(JSON.parse(calls[4]![1].body as string)).toEqual({ assetIds: null, folderId: 'o', destinationId: 'd' });
  });
});
