// web/src/lib/library/shared.ts — Shared folders (shared-folders spec): usernames, a player's shared tree, copy results.
import type { CopySharedResponse, FolderDto, SharedLibraryDto } from '$lib/api/types';

/** Management's username format (`Limits.Username`). */
export const USERNAME = /^[A-Za-z0-9_-]{3,32}$/;

/** Null when the username is 3 to 32 letters, digits, - or _. */
export function usernameProblem(text: string): string | null {
  const t = text.trim();
  if (!t) return 'Type a username.';
  return USERNAME.test(t) ? null : 'A username is 3 to 32 letters, digits, - or _.';
}

/** The shared tree as folders, so the library's tree and filters read it. */
export const sharedFolders = (view: SharedLibraryDto): FolderDto[] =>
  view.folders.map((f) => ({ id: f.id, parentId: f.parentId ?? undefined, name: f.name, assetIds: f.assetIds }));

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** "Added 3 models and 2 folders from Kim to Borrowed." */
export function copySummary(r: CopySharedResponse, from: string, into: string | null): string {
  const what = r.folders.length ? `${plural(r.assets.length, 'model')} and ${plural(r.folders.length, 'folder')}` : plural(r.assets.length, 'model');
  const missing = r.notFound.length ? ` ${plural(r.notFound.length, 'model')} ${r.notFound.length === 1 ? 'is' : 'are'} no longer shared.` : '';
  return `Added ${what} from ${from} to ${into ?? 'your library'}.${missing}`;
}
