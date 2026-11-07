// web/src/lib/armies/cards.ts — what the army cards and the army page's unit cards draw (UX redesign, web → Armies, Army page).
import type { ModelSlot, Unit } from '$lib/api/types';
import { sameBase } from '$lib/units/bases';

const SYSTEMS: Record<string, string> = { 'grimdark-future': 'Grimdark Future', 'age-of-fantasy': 'Age of Fantasy' };
/** The game system's name, for the system icon's tooltip. */
export const systemName = (id: string): string => SYSTEMS[id] ?? id;

/** Four pips, filled for the share of slots with a mesh: all only when every slot has one, none only when none has. */
export function meshPips(withMesh: number, slots: number, count = 4): boolean[] {
  let filled = slots > 0 ? Math.round((count * withMesh) / slots) : 0;
  if (withMesh > 0 && filled === 0) filled = 1;
  if (withMesh < slots && filled === count) filled = count - 1;
  return Array.from({ length: count }, (_, i) => i < filled);
}

export const meshLine = (withMesh: number, slots: number): string =>
  withMesh === slots ? 'all meshes' : withMesh === 0 ? 'no meshes' : `meshes ${withMesh} of ${slots}`;

const sameSlot = (a: ModelSlot, b: ModelSlot) => (a.assetId ?? null) === (b.assetId ?? null) && sameBase(a.base, b.base) && Math.abs(a.height - b.height) < 1e-6;

/** `shared`: two or more models with one mesh, base and height, drawn as one "every model" row that opens a row per model. */
export function slotRows(unit: Unit): { shared: boolean; rows: ModelSlot[] } {
  const first = unit.models[0];
  const shared = unit.models.length > 1 && !!first && unit.models.every((m) => sameSlot(m, first));
  return { shared, rows: unit.models };
}

export type StatIcon = 'quality' | 'defense';
const STAT_ICONS: Record<string, StatIcon> = { Quality: 'quality', Defense: 'defense' };
const ORDER = ['Quality', 'Defense'];

/** Quality and Defense first with their icons, then any other stat as the list has it. */
export function statBadges(unit: Unit): { key: string; icon: StatIcon | null; value: string }[] {
  const keys = Object.keys(unit.stats).sort((a, b) => (ORDER.indexOf(a) + 1 || 99) - (ORDER.indexOf(b) + 1 || 99));
  return keys.map((key) => ({ key, icon: STAT_ICONS[key] ?? null, value: unit.stats[key]! }));
}
