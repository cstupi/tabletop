// web/src/lib/library/shared.ts — sharing one's own assets and browsing other accounts' (shared-models spec).
import type { AssetDto, FolderDto, Guid, SharedAssetDto } from '$lib/api/types';
import { filterAssets } from './folders';

/** A folder's share state: every asset in it and its sub-folders, split by whether it is shared. */
export function folderSharing(assets: AssetDto[], folders: FolderDto[], folderId: Guid): { shared: Guid[]; unshared: Guid[] } {
  const inFolder = filterAssets(assets, folders, { folderId });
  return { shared: inFolder.filter((a) => a.shared).map((a) => a.id), unshared: inFolder.filter((a) => !a.shared).map((a) => a.id) };
}

/** Marks the ids the server answered `done` shared or not in the loaded list. */
export function markShared(assets: AssetDto[], done: Guid[], shared: boolean): AssetDto[] {
  const set = new Set(done);
  return assets.map((a) => (set.has(a.id) ? { ...a, shared } : a));
}

/** The shared items whose bytes the caller already has in their library (same hash). */
export function alreadyHeld(items: SharedAssetDto[], mine: AssetDto[]): Set<Guid> {
  const hashes = new Set(mine.map((a) => a.hash));
  return new Set(items.filter((i) => hashes.has(i.hash)).map((i) => i.id));
}

/** Appends a page, skipping items already listed. */
export function appendPage(items: SharedAssetDto[], page: SharedAssetDto[]): SharedAssetDto[] {
  const seen = new Set(items.map((i) => i.id));
  return [...items, ...page.filter((i) => !seen.has(i.id))];
}
