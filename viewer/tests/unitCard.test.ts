import { describe, expect, it } from 'vitest';
import { groupLine, groupTitle, groupWounds, nextModel, sameCard, unitCardOf } from '../src/lib/play/unitCard';
import type { SheetModel, UnitSheetModel } from '../src/lib/play/unitSheet';
import { newId } from './play';

// UnitCardContentTests
const unit = newId();
const model = newId();
const ids = Array.from({ length: 12 }, () => newId());

const m = (id: string, label: string, left: number, max: number, removed: boolean, kind: string, loadout: string[], hero = false): SheetModel =>
  ({ id, label, left, max, removed, kind, loadout, hero });

/** Built fresh on each call, as unitSheetOf builds it on each render: equal values, new lists. */
const sheet = (o: { left?: number; chip?: string; weaponRule?: string; models?: SheetModel[] } = {}): UnitSheetModel => ({
  unitId: unit, team: 'Team 1', name: 'Squad', alive: 1, size: 1, quality: '4+', defense: '5+',
  chips: [{ kind: 'activated', text: o.chip ?? 'Activated' }],
  weapons: [{ name: 'Rifle', count: 2, range: '24"', attacks: '1', ap: '-', apRule: null, rules: [{ label: o.weaponRule ?? 'AP(1)', text: 'text', notAutomated: false }] }],
  rules: [{ label: 'Tough(3)', text: 'text', notAutomated: false }],
  models: o.models ?? [m(model, 'Trooper', o.left ?? 2, 3, false, 't', ['Rifle'])],
});

describe('UnitCardContent', () => {
  it('the same sheet built again is the same content', () => {
    expect(sameCard(unitCardOf(sheet(), 1, null), unitCardOf(sheet(), 1, null))).toBe(true);
    expect(sameCard(null, null)).toBe(true);
    expect(sameCard(unitCardOf(sheet(), 1, null), null)).toBe(false);
  });

  it('a wound a chip a weapon rule the team or the note is a change', () => {
    const before = unitCardOf(sheet(), 1, null);
    expect(sameCard(before, unitCardOf(sheet({ left: 1 }), 1, null))).toBe(false);
    expect(sameCard(before, unitCardOf(sheet({ chip: 'Shaken' }), 1, null))).toBe(false);
    expect(sameCard(before, unitCardOf(sheet({ weaponRule: 'AP(2)' }), 1, null))).toBe(false);
    expect(sameCard(before, unitCardOf(sheet(), 2, null))).toBe(false);
    expect(sameCard(before, unitCardOf(sheet(), 1, 'Shaken: may only Idle.'))).toBe(false);
  });

  // Eight warriors, one more with a Field Doctor upgrade, and a sergeant with a plasma pistol; two warriors removed.
  it('like models group and each group names only what sets it apart', () => {
    const warrior = ['Choppa', 'Shoota'], doctor = ['Choppa', 'Shoota', 'Field Doctor'], sergeant = ['Choppa', 'Plasma Pistol'];
    const models = [
      ...Array.from({ length: 8 }, (_, i) => m(ids[i]!, 'Orc Warrior', i < 2 ? 0 : 1, 1, i < 2, 'w', warrior)),
      m(ids[8]!, 'Orc Warrior', 1, 1, false, 'd', doctor), m(ids[9]!, 'Sergeant', 1, 1, false, 's', sergeant),
    ];
    const c = unitCardOf(sheet({ models }), 1, null);
    expect(c.groups.map((g) => [g.label, g.loadout, g.alive, g.size])).toEqual([['Orc Warrior', '', 6, 8], ['Orc Warrior', 'Field Doctor', 1, 1], ['Sergeant', 'Plasma Pistol', 1, 1]]);
    expect(c.groups[0]!.standing).toEqual(ids.slice(2, 8));                     // removed models are counted, not listed
    expect(c.groups.map(groupLine)).toEqual(['6/8 Orc Warrior', '1/1 Orc Warrior — Field Doctor', '1/1 Sergeant — Plasma Pistol']);
    expect([c.woundsLeft, c.woundsMax]).toEqual([8, 10]);                       // one-wound models: the wound total is the alive count
  });

  it('tough groups total their wounds and a joined hero has its own marked group', () => {
    const models = [
      m(ids[0]!, 'Brute', 3, 3, false, 'b', ['Pick']), m(ids[1]!, 'Brute', 1, 3, false, 'b', ['Pick']),
      m(ids[2]!, 'Brute', 0, 3, true, 'b', ['Pick']), m(ids[3]!, 'Brute', 3, 3, false, 'b', ['Pick']),
      m(ids[4]!, 'Champion', 2, 3, false, 'h', ['Great Weapon', 'Quality 3+'], true),
    ];
    const c = unitCardOf(sheet({ models }), 1, null);
    expect(c.groups.map((g) => [g.label, g.hero, g.alive, g.size, g.woundsLeft, g.woundsMax])).toEqual([['Brute', false, 3, 4, 7, 12], ['Champion', true, 1, 1, 2, 3]]);
    expect(c.groups.map(groupLine)).toEqual(['3/4 Brute · Wounds 7/12', '1/1 Champion — Great Weapon, Quality 3+ · Wounds 2/3']);
    expect(c.groups.map((g) => [groupTitle(g), groupWounds(g)])).toEqual([['3/4 Brute', 'Wounds 7/12'], ['1/1 Champion — Great Weapon, Quality 3+', 'Wounds 2/3']]);
    expect([c.woundsLeft, c.woundsMax]).toEqual([9, 15]);
  });

  it('a group click cycles through its standing models', () => {
    const models = Array.from({ length: 3 }, (_, i) => m(ids[i]!, 'Trooper', i === 1 ? 0 : 1, 1, i === 1, 't', ['Rifle']));
    const g = unitCardOf(sheet({ models }), 1, null).groups[0]!;
    expect(nextModel(g, null)).toBe(ids[0]);
    expect(nextModel(g, ids[0]!)).toBe(ids[2]);
    expect(nextModel(g, ids[2]!)).toBe(ids[0]);
    expect(nextModel(g, ids[11]!)).toBe(ids[0]);                              // a model of another group starts at the first
    const gone = unitCardOf(sheet({ models: [m(ids[0]!, 'Trooper', 0, 1, true, 't', [])] }), 1, null).groups[0]!;
    expect(nextModel(gone, null)).toBeNull();
  });

  it('a change in a group is a change', () => {
    const two = (kind: string) => [m(ids[0]!, 'Trooper', 1, 1, false, 't', ['Rifle']), m(ids[1]!, 'Trooper', 1, 1, false, kind, ['Rifle'])];
    expect(sameCard(unitCardOf(sheet({ models: two('t') }), 1, null), unitCardOf(sheet({ models: two('u') }), 1, null))).toBe(false);
  });
});
