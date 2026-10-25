// viewer/src/lib/play/cardText.ts
// The copy UnitCard.cs, HoverLabels.cs and the enemy strip build by hand, as functions the components bind.

import type { EnemyStripModel, RangeCheck } from './enemyStrip';
import type { HoverInfo } from './hoverView';
import type { UnitCardContent } from './unitCard';
import { AP_LABEL, type SheetRule, type SheetWeapon, type UnitSheetModel } from './unitSheet';

/** `{name} · {alive}/{size} models · Wounds {left}/{max}` */
export const cardHeadline = (c: UnitCardContent): string =>
  `${c.sheet.name} · ${c.sheet.alive}/${c.sheet.size} models · Wounds ${c.woundsLeft}/${c.woundsMax}`;

export const badges = (s: UnitSheetModel): [string, string] => [`Quality ${s.quality ?? '-'}`, `Defense ${s.defense ?? '-'}`];

/** `{name} ×{count}` past one. */
export const weaponName = (w: SheetWeapon): string => (w.count > 1 ? `${w.name} ×${w.count}` : w.name);

export const WEAPON_HEADINGS: readonly string[] = ['Weapon', 'Range', 'Attacks', AP_LABEL, 'Rules'];

/** `{label} (not automated)` when tagged. */
export const ruleChip = (r: SheetRule): string => (r.notAutomated ? `${r.label} (not automated)` : r.label);

/** Its text, else `Not automated: no rule text.` when tagged, else ''. */
export const ruleTooltip = (r: SheetRule): string => (r.text !== null && r.text.length > 0 ? r.text : r.notAutomated ? 'Not automated: no rule text.' : '');

/** `Defense {d} · {alive} alive · {distance:0.0}"` */
export const stripLine = (m: EnemyStripModel): string =>
  `Defense ${m.defense ?? '-'} · ${m.alive} alive` + (m.distance !== null ? ` · ${m.distance.toFixed(1)}"` : '');

/** `{weapon} {range} within|beyond`. */
export function rangeLine(r: RangeCheck): { text: string; tone: 'success' | 'warning' | 'plain' } {
  return {
    text: `${r.weapon} ${r.range}${r.within === true ? ' within' : r.within === false ? ' beyond' : ''}`,
    tone: r.within === true ? 'success' : r.within === false ? 'warning' : 'plain',
  };
}

/** `{name} · Click to target`, `{name} · {d:0.0}"`, `{name}`. */
export const hoverLabel = (h: HoverInfo, eligible: boolean): string =>
  eligible ? `${h.name} · Click to target` : h.distance !== null ? `${h.name} · ${h.distance.toFixed(1)}"` : h.name;
