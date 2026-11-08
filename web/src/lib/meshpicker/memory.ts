// web/src/lib/meshpicker/memory.ts — recent picks and the picker's folder, per account in this browser (mesh-picker spec → Remembered).
import type { FolderDto, Guid } from '$lib/api/types';

export const RECENT_MAX = 30;
export const recentKey = (accountId: Guid) => `tabletop.meshRecent.${accountId}`;
export const folderKey = (accountId: Guid) => `tabletop.meshFolder.${accountId}`;

const store = (): Storage | null => { try { return localStorage; } catch { return null; } };

export function loadRecent(accountId: Guid, s: Storage | null = store()): Guid[] {
  try {
    const v = JSON.parse(s?.getItem(recentKey(accountId)) ?? '[]') as unknown;
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string').slice(0, RECENT_MAX) : [];
  } catch { return []; }
}

/** Newest first, each id once. */
export function pushRecent(accountId: Guid, assetId: Guid, s: Storage | null = store()): Guid[] {
  const next = [assetId, ...loadRecent(accountId, s).filter((id) => id !== assetId)].slice(0, RECENT_MAX);
  try { s?.setItem(recentKey(accountId), JSON.stringify(next)); } catch { /* private window or blocked site data */ }
  return next;
}

/** The folder the picker last showed, or null (All) when none was or it no longer exists. */
export function loadLastFolder(accountId: Guid, folders: FolderDto[], s: Storage | null = store()): Guid | null {
  try { const id = s?.getItem(folderKey(accountId)); return id && folders.some((f) => f.id === id) ? id : null; } catch { return null; }
}

export function saveLastFolder(accountId: Guid, folderId: Guid | null, s: Storage | null = store()): void {
  try { if (folderId) s?.setItem(folderKey(accountId), folderId); else s?.removeItem(folderKey(accountId)); } catch { /* blocked */ }
}
