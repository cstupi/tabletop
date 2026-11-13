// web/src/lib/armies/cards.ts — what the army cards and the army page's unit cards draw (UX redesign, web → Armies, Army page).
import type { Army, ModelSlot, Unit } from '$lib/api/types';
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

const label = (p: { name: string; parameter?: number }) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`);

/** The imported entry a slot came from: its name without the upgrade it is named for and the import's number. */
const entry = (name: string): string => name.replace(/(\s+\([^)]*\))?\s+\d+$/, '');

/** What sets a model apart among the models it was imported with (a joined Hero is its own entry): its weapons, then its rules, that not
 *  every one of them carries (a banner, a special weapon). */
export function slotMarks(unit: Unit, m: ModelSlot): string[] {
  const peers = unit.models.filter((x) => entry(x.name) === entry(m.name));
  if (peers.length < 2) return [];
  const everyWeapon = (name: string) => peers.every((x) => x.weapons.some((w) => w.name === name));
  const everyRule = (l: string) => peers.every((x) => x.properties.some((p) => label(p) === l));
  const weapons = [...new Set(m.weapons.map((w) => w.name))].filter((n) => !everyWeapon(n));
  const rules = [...new Set(m.properties.map(label))].filter((l) => !everyRule(l));
  return [...weapons, ...rules];
}

export interface SlotGroup { /** The first slot's id: stable while meshes change. */ id: string; label: string; marks: string[]; slots: ModelSlot[] }

/** A unit's rows: models alike in name, in what sets them apart and in mesh, base and height are one row (`Infantry Squad ×12`),
 *  opened per model on demand; a model unlike the rest is its own row. In slot order of each group's first model. */
export function slotGroups(unit: Unit): SlotGroup[] {
  const groups: SlotGroup[] = [];
  const kind = (name: string) => name.replace(/\s+\d+$/, '');
  for (const m of unit.models) {
    const marks = slotMarks(unit, m);
    const g = groups.find((x) => kind(x.slots[0]!.name) === kind(m.name) && x.marks.join('|') === marks.join('|') && sameSlot(x.slots[0]!, m));
    if (g) g.slots.push(m); else groups.push({ id: m.id, label: m.name, marks, slots: [m] });
  }
  for (const g of groups) if (g.slots.length > 1) g.label = `${kind(g.slots[0]!.name)} ×${g.slots.length}`;
  return groups;
}

/** The units the army page draws: every unit but a reserve copy of another (Reinforcement's "new copy": same name, as many models),
 *  which stands on its original's miniatures and is given none of its own. */
export function shownUnits(army: Army): Unit[] {
  const copy = (u: Unit) => { const of = u.reserve && army.units.find((x) => x.id === u.reserve!.of); return !!of && of.name === u.name && of.models.length === u.models.length; };
  return army.units.filter((u) => !copy(u));
}
