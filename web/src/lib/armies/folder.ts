// web/src/lib/armies/folder.ts — the army page's mesh pickers list one library folder, remembered per army in this browser.
import type { AssetDto, FolderDto, Guid } from '$lib/api/types';
import { filterAssets } from '$lib/library/folders';

export const folderKey = (armyId: Guid) => `tabletop.armyFolder.${armyId}`;

/** The folder last picked for the army, or null (All) when none was or it no longer exists. */
export function loadFolder(armyId: Guid, folders: FolderDto[], store: Storage = localStorage): Guid | null {
  try { const id = store.getItem(folderKey(armyId)); return id && folders.some((f) => f.id === id) ? id : null; } catch { return null; }
}

export function saveFolder(armyId: Guid, folderId: Guid | null, store: Storage = localStorage): void {
  try { if (folderId) store.setItem(folderKey(armyId), folderId); else store.removeItem(folderKey(armyId)); } catch { /* private window or blocked site data */ }
}

/** The folder's assets and its sub-folders' (all with no folder), plus the mesh the picker already holds wherever it is filed. */
export function pickerAssets(assets: AssetDto[], folders: FolderDto[], folderId: Guid | null, value: Guid | null): AssetDto[] {
  const shown = filterAssets(assets, folders, { folderId });
  const held = value && !shown.some((a) => a.id === value) ? assets.find((a) => a.id === value) : undefined;
  return held ? [...shown, held] : shown;
}
