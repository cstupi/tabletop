// web/src/lib/library/bulk.ts — filing and deleting many assets: where an upload lands, which folders to unfile from, chunked bulk requests.
import type { BulkAssetsResponse, FolderDto, Guid } from '$lib/api/types';
import { buildTree, descendantIds, type FolderNode } from './folders';

/** The drag-and-drop type carrying dragged asset ids (a JSON array) from the grid to the folder tree. */
export const ASSET_DRAG = 'application/x-tabletop-assets';

/** Management's cap on the assets one bulk request names (`Limits.Bulk`). */
export const BULK_MAX = 200;

/** The folder an upload is filed into: the one selected in the tree, or none under *All* or when it no longer exists. */
export const uploadTarget = (folders: FolderDto[], selected: Guid | null): Guid | null => (selected && folders.some((f) => f.id === selected) ? selected : null);

/** Every folder in tree order, labelled with its path ("Orks / Boyz"), for pickers. */
export function folderOptions(folders: FolderDto[]): { id: Guid; label: string }[] {
  const out: { id: Guid; label: string }[] = [];
  const walk = (nodes: FolderNode[], prefix: string) => { for (const n of nodes) { const label = prefix + n.folder.name; out.push({ id: n.folder.id, label }); walk(n.children, label + ' / '); } };
  walk(buildTree(folders), '');
  return out;
}

/** The folders of `root`'s subtree (except `skip`) that hold any of `ids`, with the ids each holds: what unfiling from the folder in view removes. */
export function holdersIn(folders: FolderDto[], root: Guid, ids: Guid[], skip?: Guid): { folderId: Guid; ids: Guid[] }[] {
  const subtree = descendantIds(folders, root);
  return folders.filter((f) => subtree.has(f.id) && f.id !== skip)
    .map((f) => ({ folderId: f.id, ids: ids.filter((id) => f.assetIds.includes(id)) }))
    .filter((h) => h.ids.length > 0);
}

export function chunk<T>(items: T[], size = BULK_MAX): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size));
  return out;
}

export interface BulkOutcome { done: Guid[]; notFound: Guid[]; failed: Guid[]; error?: unknown }
/** Sends `ids` in chunks of `size`, one request at a time; a failed chunk is recorded and the rest still go. */
export async function runBulk(ids: Guid[], send: (ids: Guid[]) => Promise<BulkAssetsResponse>, size = BULK_MAX): Promise<BulkOutcome> {
  const out: BulkOutcome = { done: [], notFound: [], failed: [] };
  for (const part of chunk(ids, size)) {
    try { const r = await send(part); out.done.push(...r.done); out.notFound.push(...r.notFound); }
    catch (e) { out.failed.push(...part); out.error ??= e; }
  }
  return out;
}
