import { describe, expect, it } from 'vitest';
import {
  WEAPON_HEADINGS, badges, cardHeadline, hoverLabel, rangeLine, ruleChip, ruleTooltip, stripLine, weaponName,
} from '../src/lib/play/cardText';
import type { HoverInfo } from '../src/lib/play/hoverView';
import { unitCardOf } from '../src/lib/play/unitCard';
import type { SheetModel, SheetRule, SheetWeapon, UnitSheetModel } from '../src/lib/play/unitSheet';
import { newId } from './play';

// The copy UnitCard.cs, HoverLabels.cs and the strip build by hand.
const model = (left: number, max: number, removed = false): SheetModel => ({ id: newId(), label: 'Trooper', left, max, removed, kind: 'k', loadout: [], hero: false });
const sheet = (models: SheetModel[], over: Partial<UnitSheetModel> = {}): UnitSheetModel => ({
  unitId: newId(), team: 'Team 1', name: 'Squad', alive: models.filter((m) => !m.removed).length, size: models.length, quality: '4+', defense: null,
  chips: [], weapons: [], rules: [], models, ...over,
});
const weapon = (count: number): SheetWeapon => ({ name: 'Rifle', count, range: '24"', attacks: '1', ap: '-', apRule: null, rules: [] });

describe('cardText', () => {
  it('the headline counts models and wounds', () => {
    const c = unitCardOf(sheet([model(3, 3), model(1, 3), model(0, 3, true)]), 1, null);
    expect(cardHeadline(c)).toBe('Squad · 2/3 models · Wounds 4/9');
    expect(badges(c.sheet)).toEqual(['Quality 4+', 'Defense -']);
    expect(WEAPON_HEADINGS).toEqual(['Weapon', 'Range', 'Attacks', 'AP', 'Rules']);
  });

  it('a weapon carried twice reads ×2', () => {
    expect(weaponName(weapon(2))).toBe('Rifle ×2');
    expect(weaponName(weapon(1))).toBe('Rifle');
  });

  it('a tagged rule reads not automated and its tooltip falls back', () => {
    const tagged: SheetRule = { label: 'Fearless', text: null, notAutomated: true };
    const plain: SheetRule = { label: 'Tough(3)', text: 'Must take X wounds.', notAutomated: false };
    const bare: SheetRule = { label: 'Odd', text: null, notAutomated: false };
    expect(ruleChip(tagged)).toBe('Fearless (not automated)');
    expect(ruleChip(plain)).toBe('Tough(3)');
    expect(ruleTooltip(tagged)).toBe('Not automated: no rule text.');
    expect(ruleTooltip({ ...tagged, text: 'Gets +1.' })).toBe('Gets +1.');
    expect(ruleTooltip(plain)).toBe('Must take X wounds.');
    expect(ruleTooltip(bare)).toBe('');
  });

  it('the strip reads defense alive and the distance to a tenth', () => {
    const base = { unitId: newId(), name: 'Red', defense: '5+', alive: 2, ranges: [] };
    expect(stripLine({ ...base, distance: 12.04 })).toBe('Defense 5+ · 2 alive · 12.0"');
    expect(stripLine({ ...base, distance: null, defense: null })).toBe('Defense - · 2 alive');
  });

  it('a range within reads success and beyond warning', () => {
    expect(rangeLine({ weapon: 'Rifle', range: '24"', within: true })).toEqual({ text: 'Rifle 24" within', tone: 'success' });
    expect(rangeLine({ weapon: 'Pistol', range: '12"', within: false })).toEqual({ text: 'Pistol 12" beyond', tone: 'warning' });
    expect(rangeLine({ weapon: 'Rifle', range: '24"', within: null })).toEqual({ text: 'Rifle 24"', tone: 'plain' });
  });

  it('the hover label offers a click on an eligible unit, else the distance', () => {
    const info = (distance: number | null): HoverInfo => ({ unitId: newId(), name: 'Red Warriors', teamOrdinal: 2, distance, wounds: [] });
    expect(hoverLabel(info(9), true)).toBe('Red Warriors · Click to target');
    expect(hoverLabel(info(9), false)).toBe('Red Warriors · 9.0"');
    expect(hoverLabel(info(null), false)).toBe('Red Warriors');
  });
});
