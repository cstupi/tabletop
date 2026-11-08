// web/src/lib/meshpicker/search.ts — the mesh picker's search over the library the army page already holds (mesh-picker spec → Search).
import type { AssetDto, Base, FolderDto, Guid } from '$lib/api/types';
import { sameBase } from '$lib/units/bases';

/** A trailing s or z goes, so Boyz, boys and boy are one word. */
export const stem = (w: string): string => (w.length > 3 && /[sz]$/.test(w) ? w.slice(0, -1) : w);

/** Lower-cased words, split on anything not a letter or digit, camelCase and letter–digit boundaries. */
export function words(text: string): string[] {
  const spaced = text.replace(/([\p{Ll}\p{N}])(\p{Lu})/gu, '$1 $2').replace(/(\p{L})(\p{N})/gu, '$1 $2').replace(/(\p{N})(\p{L})/gu, '$1 $2');
  return spaced.toLowerCase().split(/[^\p{L}\p{N}]+/u).filter(Boolean).map((w) => (/^\d+$/.test(w) ? w : stem(w)));
}
const NUMBER = /^\d+$/;

/** Edit distance with a swap of neighbours as one edit, or max + 1 once it is past max. */
function within(a: string, b: string, max: number): number {
  if (Math.abs(a.length - b.length) > max) return max + 1;
  let before: number[] = []; let prev = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) {
    const row = [i]; let best = i;
    for (let j = 1; j <= b.length; j++) {
      let v = Math.min(prev[j]! + 1, row[j - 1]! + 1, prev[j - 1]! + (a[i - 1] === b[j - 1] ? 0 : 1));
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) v = Math.min(v, before[j - 2]! + 1);
      row.push(v); if (v < best) best = v;
    }
    if (best > max) return max + 1;
    before = prev; prev = row;
  }
  return prev[b.length]!;
}

/** How well a query word matches a word: 3 exact (2 and 02 are one number), 2 prefix, 1 inside, 0.5 within one edit (two from 8
 *  letters), else 0. */
export function wordScore(q: string, w: string): number {
  if (q === w || (NUMBER.test(q) && NUMBER.test(w) && Number(q) === Number(w))) return 3;
  if (w.startsWith(q)) return 2;
  if (q.length >= 2 && w.includes(q)) return 1;
  if (q.length < 4) return 0;
  const max = q.length >= 8 ? 2 : 1;
  return within(q, w, max) <= max || (w.length > q.length && within(q, w.slice(0, q.length), max) <= max) ? 0.5 : 0;
}

/** Folder words count for half a name word. */
const FOLDER_WEIGHT = 0.5;

/** The best score of a query word over an asset's name words, then its folder words at half. */
export function bestScore(q: string, name: string[], folders: string[]): number {
  let best = 0;
  for (const w of name) { const s = wordScore(q, w); if (s > best) { best = s; if (s === 3) return 3; } }
  for (const w of folders) { const s = wordScore(q, w) * FOLDER_WEIGHT; if (s > best) best = s; }
  return best;
}

export interface Entry {
  asset: AssetDto;
  name: string[];
  /** The words of every folder the asset is filed in and of their parents. */
  folders: string[];
  /** Those folders' ids, parents included: an entry shows under each of them. */
  under: Set<Guid>;
  /** Position by name, for a cheap last tie-break. */
  rank: number;
}

/** One entry per asset, built once per library load. */
export function buildIndex(assets: AssetDto[], folders: FolderDto[]): Entry[] {
  const byId = new Map(folders.map((f) => [f.id, f]));
  const chain = (id: Guid): FolderDto[] => {
    const out: FolderDto[] = [];
    for (let f = byId.get(id); f && !out.includes(f); f = f.parentId ? byId.get(f.parentId) : undefined) out.push(f);
    return out;
  };
  const filed = new Map<Guid, Set<Guid>>();
  for (const f of folders) for (const a of f.assetIds) for (const up of chain(f.id)) {
    let s = filed.get(a); if (!s) filed.set(a, (s = new Set())); s.add(up.id);
  }
  const sorted = [...assets].sort((a, b) => a.name.localeCompare(b.name));
  const rank = new Map(sorted.map((a, i) => [a.id, i]));
  return assets.map((asset) => {
    const under = filed.get(asset.id) ?? new Set<Guid>();
    return { asset, name: words(asset.name), folders: [...new Set([...under].flatMap((id) => words(byId.get(id)!.name)))], under, rank: rank.get(asset.id)! };
  });
}

export interface Hit { entry: Entry; score: number; fits: boolean }
export interface SearchOptions {
  /** The folder in view (its sub-folders included); null is the whole library. */
  folder?: Guid | null;
  /** The slot's base: fitting assets rank first among equal scores. */
  base?: Base | null;
  /** Asset ids, newest first: recent picks rank first among equal scores and fit. */
  recent?: Guid[];
}

export const fits = (a: AssetDto, base: Base | null | undefined): boolean => !!base && !!a.modelledBase && sameBase(a.modelledBase, base);

/** Every entry in the folder whose words match every query word, best first; an empty query matches all. */
export function search(index: Entry[], query: string, o: SearchOptions = {}): Hit[] {
  const qs = [...new Set(words(query))];
  const recent = new Map((o.recent ?? []).map((id, i) => [id, i]));
  const out: Hit[] = [];
  for (const entry of index) {
    if (o.folder && !entry.under.has(o.folder)) continue;
    let score = 0;
    for (const q of qs) { const s = bestScore(q, entry.name, entry.folders); if (s === 0) { score = -1; break; } score += s; }
    if (score < 0) continue;
    out.push({ entry, score, fits: fits(entry.asset, o.base) });
  }
  const r = (h: Hit) => recent.get(h.entry.asset.id) ?? Infinity;
  return out.sort((a, b) => b.score - a.score || Number(b.fits) - Number(a.fits) || r(a) - r(b) || a.entry.rank - b.entry.rank);
}
