// viewer/src/lib/play/unitSheet.ts
// client/src/Core/UnitSheet.cs: the unit card (client UX spec → Unit card) from the army snapshot, the projection, the pinned catalog
// and the server's model facts. Names no rule: a label is the snapshot's, a model's wounds are the ModelFacts value, and a rule with no
// catalog entry is not automated.

import type { ModelInPlay, Projection, UnitInPlay } from '../live/apply';
import type { ModelSlot, PropertyInstance, Unit, Weapon } from '../protocol/wire.generated';
import type { Names } from './names';
import { onCard, reserveOf, words } from './rosterReserve';
import type { RuleTexts } from './ruleTexts';

/** A rule as the sheet shows it: its label with the parameter, its catalog text, and the *not automated* tag (a loaded catalog without it). */
export interface SheetRule { readonly label: string; readonly text: string | null; readonly notAutomated: boolean }
export type ChipKind = 'activated' | 'condition' | 'spell_tokens' | 'grant' | 'reserve';
/** A status chip; a condition keeps the raw condition name, the key an icon is looked up by. */
export interface SheetChip { readonly kind: ChipKind; readonly text: string; readonly condition?: string }
export interface SheetWeapon {
  readonly name: string; readonly count: number; readonly range: string; readonly attacks: string; readonly ap: string;
  readonly apRule: SheetRule | null; readonly rules: readonly SheetRule[];
}
/** A model: label is the slot's name without Army Forge's trailing number; kind is equal for models alike in name, weapons, rules, stats
 * and max wounds; loadout lists its weapons (×n for one carried twice), its rule labels but Hero, and each slot stat that differs from
 * the card's badge; hero marks a slot carrying the army list's Hero entry. */
export interface SheetModel {
  readonly id: string; readonly label: string; readonly left: number; readonly max: number; readonly removed: boolean; readonly kind: string;
  readonly loadout: readonly string[]; readonly hero: boolean;
}
export interface UnitSheetModel {
  readonly unitId: string; readonly team: string; readonly name: string; readonly alive: number; readonly size: number;
  readonly quality: string | null; readonly defense: string | null; readonly chips: readonly SheetChip[]; readonly weapons: readonly SheetWeapon[];
  readonly rules: readonly SheetRule[]; readonly models: readonly SheetModel[];
}

/** The army list's Hero entry: the card marks the model's group with it and computes nothing from it. */
export const HERO_LABEL = 'Hero';
/** The printed army list's AP column label: the card moves this property's parameter into its own column and computes nothing from it. */
export const AP_LABEL = 'AP';

const ordinal = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);
const byOrdinal = <T extends { ordinal: number }>(xs: readonly T[]): T[] => [...xs].sort((a, b) => a.ordinal - b.ordinal);
/** Army Forge numbers a unit's models ("Specialists 3", "Orc Leader 1"), the simulator "Red 1.2"; the card groups them under the name alone. */
const TRAILING_NUMBER = /\s+\d+(\.\d+)*$/;

const labelOf = (x: PropertyInstance): string => (x.parameter !== undefined ? `${x.name}(${x.parameter})` : x.name);
const pairs = (stats: { readonly [key: string]: string }): string => Object.keys(stats).sort(ordinal).map((k) => `${k}=${stats[k]}`).join(',');
const stat = (unit: Unit, first: ModelSlot | undefined, name: string): string | null =>
  (Object.hasOwn(unit.stats, name) ? unit.stats[name] : undefined) ?? (first !== undefined && Object.hasOwn(first.stats, name) ? first.stats[name] : undefined) ?? null;

function groupBy<T>(xs: readonly T[], key: (x: T) => string): T[][] {
  const groups = new Map<string, T[]>();
  for (const x of xs) {
    const k = key(x);
    const g = groups.get(k);
    if (g === undefined) groups.set(k, [x]); else g.push(x);
  }
  return [...groups.values()];
}

/** Throws for a unit the projection does not hold; the caller shows a sheet only for a unit it can see. */
export function unitSheetOf(p: Projection, n: Names, unitId: string, rules: RuleTexts, maxWounds: ReadonlyMap<string, number>): UnitSheetModel {
  const unit = p.units.get(unitId);
  if (unit === undefined) throw new Error(`unit ${unitId} is not in the projection`);
  const slots = byOrdinal(unit.definition.models);
  const first = slots[0];
  const models = slots.map((slot) => modelOf(p.models.get(slot.id)!, unit.definition, first, maxWounds));
  const standing = slots.filter((_, i) => !models[i]!.removed);
  const t = first !== undefined ? n.teamOfModel(first.id) : null;
  return {
    unitId, team: t !== null ? n.team(t) : '', name: n.unit(unitId), alive: models.filter((m) => !m.removed).length, size: slots.length,
    quality: stat(unit.definition, first, 'Quality'), defense: stat(unit.definition, first, 'Defense'),
    chips: chipsOf(p, n, unit), weapons: weaponsOf(standing, rules), rules: rulesOf(unit.definition, standing, rules), models,
  };
}

function modelOf(m: ModelInPlay, unit: Unit, first: ModelSlot | undefined, maxWounds: ReadonlyMap<string, number>): SheetModel {
  const max = maxWounds.get(m.id) ?? 1;
  const slot = m.slot;
  const label = slot.name.replace(TRAILING_NUMBER, '');
  const loadout = [
    ...groupBy(slot.weapons, (w) => w.name).map((g) => (g.length > 1 ? `${g[0]!.name} ×${g.length}` : g[0]!.name)),
    ...slot.properties.filter((x) => x.name !== HERO_LABEL).map(labelOf),
    ...Object.keys(slot.stats).sort(ordinal).filter((k) => stat(unit, first, k) !== slot.stats[k]).map((k) => `${k} ${slot.stats[k]}`),
  ];
  const kind = [
    label, max.toString(),
    ...slot.weapons.map((w) => `${w.name}|${pairs(w.stats)}|${w.properties.map(labelOf).join(',')}`).sort(ordinal),
    slot.properties.map(labelOf).sort(ordinal).join(','), pairs(slot.stats),
  ].join('\u001f');
  return {
    id: m.id, label, left: Math.max(0, max - m.wounds), max, removed: m.location.kind === 'shelf' && m.wounds >= max, kind, loadout,
    hero: slot.properties.some((x) => x.name === HERO_LABEL),
  };
}

function chipsOf(p: Projection, n: Names, unit: UnitInPlay): SheetChip[] {
  const chips: SheetChip[] = [];
  const reserve = reserveOf(p, unit);
  if (reserve !== null) chips.push({ kind: 'reserve', text: reserve });
  if (unit.actions.size > 0) chips.push({ kind: 'activated', text: `Activated: ${[...unit.actions].sort(ordinal).join(', ')}` });
  for (const c of onCard(unit.conditions).sort(ordinal)) chips.push({ kind: 'condition', text: words(c), condition: c });
  let tokens = 0;
  for (const m of p.models.values()) if (m.unitId === unit.id) tokens += m.spellTokens;
  if (tokens > 0) chips.push({ kind: 'spell_tokens', text: `${tokens} spell token${tokens === 1 ? '' : 's'}` });
  for (const g of unit.grants)
    chips.push({ kind: 'grant', text: `${g.entryName} from ${n.unit(g.casterUnitId)} · ${g.remainingUses} use${g.remainingUses === 1 ? '' : 's'}` });
  return chips;
}

const weaponsOf = (standing: readonly ModelSlot[], rules: RuleTexts): SheetWeapon[] =>
  groupBy(standing.flatMap((s) => s.weapons), (w) => w.name).map((g) => weaponRow(g[0]!.name, g.length, g[0]!, rules));

function weaponRow(name: string, count: number, w: Weapon, rules: RuleTexts): SheetWeapon {
  const at = w.properties.findIndex((x) => x.name === AP_LABEL && x.parameter !== undefined);
  const ap = at >= 0 ? w.properties[at]! : null;
  return {
    name, count, range: w.stats['Range'] ?? '-', attacks: w.stats['Attacks'] ?? '-', ap: ap !== null ? String(ap.parameter) : '-',
    apRule: ap !== null ? ruleOf(ap, rules) : null, rules: w.properties.filter((_, i) => i !== at).map((x) => ruleOf(x, rules)),
  };
}

function rulesOf(unit: Unit, standing: readonly ModelSlot[], rules: RuleTexts): SheetRule[] {
  const seen = new Set<string>();
  return [...unit.properties, ...standing.flatMap((s) => s.properties)].map((x) => ruleOf(x, rules)).filter((r) => !seen.has(r.label) && !!seen.add(r.label));
}

const ruleOf = (x: PropertyInstance, rules: RuleTexts): SheetRule => ({ label: labelOf(x), text: rules.textOf(x.name), notAutomated: rules.notAutomated(x.name) });
