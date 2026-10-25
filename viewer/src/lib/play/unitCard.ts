// viewer/src/lib/play/unitCard.ts
// client/src/Core/UnitCardContent.cs: what the unit card draws. Equal by value, lists element by element, so the card rebuilds only
// when what it shows changed and otherwise keeps its scroll and tooltip.

import type { SheetModel, SheetWeapon, UnitSheetModel } from './unitSheet';

/** Like models of a unit (SheetModel.kind equal), in the order of their first model. loadout is what sets the group apart from the
 * unit's main group (the most models, the first on a tie): its loadout items the main group lacks, empty for the main group. Wounds are
 * summed over every model of the group; standing lists the models not removed, which a click on the row cycles through. */
export interface ModelGroup {
  readonly label: string; readonly loadout: string; readonly hero: boolean; readonly alive: number; readonly size: number;
  readonly woundsLeft: number; readonly woundsMax: number; readonly standing: readonly string[];
}

/** Some model of the group has more than one wound: the row shows the wound total, else the alive count is it. */
const showsWounds = (g: ModelGroup): boolean => g.woundsMax > g.size;
/** `6/8 Orc Warrior — Field Doctor`: the row's text, which clips. */
export const groupTitle = (g: ModelGroup): string => `${g.alive}/${g.size} ${g.label}` + (g.loadout.length > 0 ? ` — ${g.loadout}` : '');
/** `Wounds 9/12`, beside the title so a long loadout never clips it; null for one-wound models. */
export const groupWounds = (g: ModelGroup): string | null => (showsWounds(g) ? `Wounds ${g.woundsLeft}/${g.woundsMax}` : null);
/** The whole row: the title and the wounds, the row's tooltip. */
export const groupLine = (g: ModelGroup): string => groupTitle(g) + (groupWounds(g) !== null ? ` · ${groupWounds(g)}` : '');

/** The standing model after the selected one, wrapping; the first when the selection is not of this group; null when none stands. */
export function nextModel(g: ModelGroup, selected: string | null): string | null {
  if (g.standing.length === 0) return null;
  const at = selected !== null ? g.standing.indexOf(selected) : -1;
  return g.standing[(at + 1) % g.standing.length]!;
}

/** The sheet, the team's ordinal for its colour bar, the limited-choice note, the model groups and the unit's wound total. */
export interface UnitCardContent {
  readonly sheet: UnitSheetModel; readonly team: number; readonly note: string | null;
  readonly groups: readonly ModelGroup[]; readonly woundsLeft: number; readonly woundsMax: number;
}

export function unitCardOf(sheet: UnitSheetModel, team: number, note: string | null): UnitCardContent {
  const groups = groupModels(sheet.models);
  return { sheet, team, note, groups, woundsLeft: groups.reduce((s, g) => s + g.woundsLeft, 0), woundsMax: groups.reduce((s, g) => s + g.woundsMax, 0) };
}

function groupModels(models: readonly SheetModel[]): ModelGroup[] {
  const byKind = new Map<string, SheetModel[]>();
  for (const m of models) {
    const g = byKind.get(m.kind);
    if (g === undefined) byKind.set(m.kind, [m]); else g.push(m);
  }
  const kinds = [...byKind.values()];
  if (kinds.length === 0) return [];
  const main = kinds.reduce((best, k) => (k.length > best.length ? k : best));   // stable: the first on a tie
  const common = new Set(main[0]!.loadout);
  return kinds.map((k): ModelGroup => ({
    label: k[0]!.label, loadout: k === main ? '' : k[0]!.loadout.filter((i) => !common.has(i)).join(', '), hero: k[0]!.hero,
    alive: k.filter((m) => !m.removed).length, size: k.length, woundsLeft: k.reduce((s, m) => s + m.left, 0), woundsMax: k.reduce((s, m) => s + m.max, 0),
    standing: k.filter((m) => !m.removed).map((m) => m.id),
  }));
}

const sameList = <T>(a: readonly T[], b: readonly T[], eq: (x: T, y: T) => boolean): boolean => a.length === b.length && a.every((x, i) => eq(b[i]!, x));
const sameRule = (a: { label: string; text: string | null; notAutomated: boolean } | null, b: { label: string; text: string | null; notAutomated: boolean } | null): boolean =>
  a === null || b === null ? a === b : a.label === b.label && a.text === b.text && a.notAutomated === b.notAutomated;
const sameChip = (a: { kind: string; text: string; condition?: string | undefined }, b: { kind: string; text: string; condition?: string | undefined }): boolean =>
  a.kind === b.kind && a.text === b.text && a.condition === b.condition;

const sameWeapon = (a: SheetWeapon, b: SheetWeapon): boolean =>
  a.name === b.name && a.count === b.count && a.range === b.range && a.attacks === b.attacks && a.ap === b.ap && sameRule(a.apRule, b.apRule)
  && sameList(a.rules, b.rules, sameRule);

const sameGroup = (a: ModelGroup, b: ModelGroup): boolean =>
  a.label === b.label && a.loadout === b.loadout && a.hero === b.hero && a.alive === b.alive && a.size === b.size && a.woundsLeft === b.woundsLeft
  && a.woundsMax === b.woundsMax && sameList(a.standing, b.standing, (x, y) => x === y);

function sameSheet(a: UnitSheetModel, b: UnitSheetModel): boolean {
  return a.unitId === b.unitId && a.team === b.team && a.name === b.name && a.alive === b.alive && a.size === b.size && a.quality === b.quality
    && a.defense === b.defense && sameList(a.chips, b.chips, sameChip) && sameList(a.rules, b.rules, sameRule) && sameList(a.weapons, b.weapons, sameWeapon);
}

export function sameCard(a: UnitCardContent | null, b: UnitCardContent | null): boolean {
  if (a === null || b === null) return a === b;
  return a.team === b.team && a.note === b.note && sameSheet(a.sheet, b.sheet) && sameList(a.groups, b.groups, sameGroup);
}
