// web/src/lib/meshpicker/suggest.ts — the picker's Suggested row (mesh-picker spec → Suggested).
import type { ArmyMeshUse, Guid, ModelSlot, Unit } from '$lib/api/types';
import { modelName } from './scope';
import { bestScore, words, type Entry } from './search';

export type Reason = 'unit' | 'army' | 'name';
export interface Suggestion { entry: Entry; score: number; reason: Reason; /** The tile's short mark: `this unit`, a unit name, `name`. */ label: string }

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

  const out: Suggestion[] = [];
  for (const entry of i.index) {
    const id = entry.asset.id;
    if (id === held) continue;
    let byName = 0;
    for (const w of modelWords) byName += 2 * bestScore(w, entry.name, entry.folders);
    for (const w of unitWords) byName += bestScore(w, entry.name, entry.folders);
    const u = fromUnit.get(id) ?? 0; const a = fromArmies.get(id);
    if (!u && !a && byName < NAME_FLOOR) continue;
    const score = u + (a?.score ?? 0) + byName + (recent.has(id) ? 1 : 0);
    const reason: Reason = u ? 'unit' : a ? 'army' : 'name';
    out.push({ entry, score, reason, label: reason === 'unit' ? 'this unit' : reason === 'army' ? a!.unit : 'name' });
  }
  return out.sort((x, y) => y.score - x.score || x.entry.rank - y.entry.rank).slice(0, i.limit);
}
