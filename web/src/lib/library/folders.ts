// web/src/lib/library/folders.ts — folders are a client-side grouping over the whole library; nothing else reads them.
import type { AssetDto, AssetKind, Base, FolderDto, Guid } from '$lib/api/types';
import { sameBase } from '$lib/units/bases';
export interface FolderNode { folder: FolderDto; children: FolderNode[] }

export function buildTree(folders: FolderDto[]): FolderNode[] {
  const byParent = new Map<Guid | undefined, FolderDto[]>();
  for (const f of folders) byParent.set(f.parentId, [...(byParent.get(f.parentId) ?? []), f]);
  const build = (parent: Guid | undefined): FolderNode[] =>
    (byParent.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name)).map((f) => ({ folder: f, children: build(f.id) }));
  return build(undefined);
}
export function descendantIds(folders: FolderDto[], id: Guid): Set<Guid> {
  const out = new Set<Guid>([id]);
  let grew = true;
  while (grew) { grew = false; for (const f of folders) if (f.parentId && out.has(f.parentId) && !out.has(f.id)) { out.add(f.id); grew = true; } }
  return out;
}
/** A folder shows its own assets and its sub-folders'; an asset in no folder shows only under *All*. */
export function filterAssets(assets: AssetDto[], folders: FolderDto[], f: { folderId: Guid | null; kind?: AssetKind; base?: Base }): AssetDto[] {
  const subtree = f.folderId ? descendantIds(folders, f.folderId) : null;
  const inFolders = subtree ? new Set(folders.filter((x) => subtree.has(x.id)).flatMap((x) => x.assetIds)) : null;
  return assets.filter((a) => (!inFolders || inFolders.has(a.id)) && (!f.kind || a.kind === f.kind) && (!f.base || (a.modelledBase !== undefined && sameBase(a.modelledBase, f.base))));
}
