// web/src/lib/meshpicker/suggest.ts — the picker's Suggested row (mesh-picker spec → Suggested).
import type { ArmyMeshUse, Guid, ModelSlot, Unit } from '$lib/api/types';
import { modelName } from './scope';
import { bestScore, words, type Entry } from './search';

export type Reason = 'unit' | 'army' | 'product' | 'name';
export interface Suggestion { entry: Entry; score: number; reason: Reason; /** The tile's short mark: `this unit`, a unit name, a product name, `name`. */ label: string }

export interface SuggestInput {
  index: Entry[];
  unit: Unit;
  /** The slots of the row the picker was opened from. */
  slots: ModelSlot[];
  /** Every slot of the account's armies with a mesh, grouped (`GET /v1/armies/meshes`). */
  uses: ArmyMeshUse[];
  recent?: Guid[];
  limit: number;
}

const STOP = new Set(['with', 'and', 'the', 'of', 'w', 'on', 'in', 'a']);
const meaningful = (text: string) => words(text).filter((w) => !STOP.has(w) && !/^\d+$/.test(w));
const same = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

/** Name words alone must reach this to suggest: one word matched as a prefix. */
const NAME_FLOOR = 2;
/** A product match's base, above any score of the name words alone that it adds to; an asset named exactly for the product adds EXACT. */
const PRODUCT = 6, EXACT = 4;

interface Product { label: string; /** Every word, the bracketed suffix's too (`[Epic]`). */ full: string[]; /** Without bracketed suffixes. */ plain: string[]; /** Its meaningful words, which an asset must all match. */ need: string[] }
const sameWords = (a: string[], b: string[]) => a.length === b.length && a.every((w, k) => w === b[k]);

/** The army list's product names for the unit (`Avatar of Change [Epic]`): an asset whose name or folders match every meaningful word of a
 *  name without its bracketed suffix scores PRODUCT plus the words' score; one named exactly for it (with or without the suffix) adds EXACT. */
function productScore(products: Product[], entry: Entry): { score: number; label: string } | null {
  let best: { score: number; label: string } | null = null;
  for (const p of products) {
    let s = 0;
    for (const w of p.need) { const b = bestScore(w, entry.name, entry.folders); if (b === 0) { s = -1; break; } s += 2 * b; }
    if (s <= 0) continue;
    s += PRODUCT + (sameWords(entry.name, p.full) || sameWords(entry.name, p.plain) ? EXACT : 0);
    if (!best || s > best.score) best = { score: s, label: p.label };
  }
  return best;
}

export function suggest(i: SuggestInput): Suggestion[] {
  const rowIds = new Set(i.slots.map((s) => s.id));
  const names = [...new Set(i.slots.map((s) => modelName(s.name)))];
  const current = new Set(i.slots.map((s) => s.assetId).filter((x): x is Guid => !!x));
  const held = current.size === 1 ? [...current][0] : null;   // a row's one mesh is not a suggestion; a mix of meshes leaves all in

  const fromUnit = new Map<Guid, number>();
  for (const m of i.unit.models) {
    if (rowIds.has(m.id) || !m.assetId) continue;
    const s = 8 + (names.some((n) => same(n, modelName(m.name))) ? 4 : 0);
    fromUnit.set(m.assetId, Math.max(fromUnit.get(m.assetId) ?? 0, s));
  }
  const fromArmies = new Map<Guid, { score: number; unit: string }>();
  for (const u of i.uses) {
    const unitMatch = same(u.unitName, i.unit.name); const modelMatch = names.some((n) => same(n, modelName(u.modelName)));
    const s = unitMatch && modelMatch ? 5 + Math.min(u.slots, 5) : modelMatch ? 3 : unitMatch ? 2 : 0;
    if (s > (fromArmies.get(u.assetId)?.score ?? 0)) fromArmies.set(u.assetId, { score: s, unit: u.unitName });
  }
  const modelWords = [...new Set(names.flatMap(meaningful))];
  const unitWords = [...new Set(meaningful(i.unit.name))].filter((w) => !modelWords.includes(w));
  const recent = new Set(i.recent ?? []);
  const products: Product[] = (i.unit.productNames ?? []).map((label) => label.trim()).filter(Boolean)
    .map((label) => { const plain = label.replace(/\[[^\]]*\]/g, ' '); return { label, full: words(label), plain: words(plain), need: [...new Set(meaningful(plain))] }; })
    .filter((p) => p.need.length > 0);

  const out: Suggestion[] = [];
  for (const entry of i.index) {
    const id = entry.asset.id;
    if (id === held) continue;
    let byName = 0;
    for (const w of modelWords) byName += 2 * bestScore(w, entry.name, entry.folders);
    for (const w of unitWords) byName += bestScore(w, entry.name, entry.folders);
    const u = fromUnit.get(id) ?? 0; const a = fromArmies.get(id); const p = products.length ? productScore(products, entry) : null;
    if (!u && !a && !p && byName < NAME_FLOOR) continue;
    const score = u + (a?.score ?? 0) + (p?.score ?? 0) + byName + (recent.has(id) ? 1 : 0);
    const reason: Reason = u ? 'unit' : a ? 'army' : p ? 'product' : 'name';
    out.push({ entry, score, reason, label: reason === 'unit' ? 'this unit' : reason === 'army' ? a!.unit : reason === 'product' ? p!.label : 'name' });
  }
  return out.sort((x, y) => y.score - x.score || x.entry.rank - y.entry.rank).slice(0, i.limit);
}
