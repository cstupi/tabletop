import { describe, expect, it } from 'vitest';
import { cardPips, chipText, countNote, groupName, groupPips, groupWoundCount, groupWoundPips, unitCardOf } from '../src/lib/play/unitCard';
import type { SheetModel } from '../src/lib/play/unitSheet';
import { newId } from './play';

// UnitCardPipsTests
const m = (label: string, left: number, max: number, removed: boolean, kind: string, ...loadout: string[]): SheetModel =>
  ({ id: newId(), label, left, max, removed, kind, loadout, hero: false });
const card = (...models: SheetModel[]) => unitCardOf({
  unitId: newId(), team: 'Team 1', name: 'Warriors', alive: models.filter((x) => !x.removed).length, size: models.length, quality: '4+', defense: '4+',
  chips: [{ kind: 'activated', text: 'Activated: Advance' }, { kind: 'condition', text: 'Shaken', condition: 'shaken' }], weapons: [], rules: [], models,
}, 1, null);

describe('UnitCardContent pips', () => {
  it('wound pips as the roster draws them and a note naming the one tough model\'s wounds', () => {
    const c = card(m('Warrior', 1, 1, false, 'w', 'Rifle'), m('Warrior', 1, 1, false, 'w', 'Rifle'), m('Warrior', 0, 1, true, 'w', 'Rifle'),
      m('Sergeant', 2, 3, false, 's', 'Plasma Pistol'));
    expect(cardPips(c)).toBe('●●●●○○');
    expect(countNote(c)).toBe('3 models · 2 wounds left on Sergeant');
    const sgt = c.groups[1]!;
    expect([groupPips(sgt), groupName(sgt), groupWoundPips(sgt), groupWoundCount(sgt)]).toEqual(['●', 'Sergeant · Plasma Pistol', '●●○', '2/3']);
    expect([groupPips(c.groups[0]!), groupName(c.groups[0]!), groupWoundPips(c.groups[0]!)]).toEqual(['●●○', 'Warrior', null]);
  });

  it('the pips put survivors first as the models row and the roster do', () => {
    const c = card(m('Warrior', 0, 1, true, 'w'), m('Warrior', 0, 1, true, 'w'), m('Warrior', 0, 1, true, 'w'), m('Warrior', 0, 1, true, 'w'),
      m('Sergeant', 1, 1, false, 's'));
    expect(cardPips(c)).toBe('●○○○○');
  });

  it('the note is the count alone without tough models and sums several', () => {
    expect(countNote(card(m('Warrior', 1, 1, false, 'w')))).toBe('1 model');
    const priced = card(m('Warrior', 1, 1, false, 'w'));
    expect(countNote({ ...priced, sheet: { ...priced.sheet, points: 170 } })).toBe('1 model · 170 pts');   // the army list's points
    expect(countNote(card(m('Ogre', 2, 3, false, 'o'), m('Ogre', 3, 3, false, 'o')))).toBe('2 models · 5 wounds left');
  });

  it('a lone tough model\'s note does not name it again', () => {
    expect(countNote(card(m('Light APC', 6, 6, false, 'apc')))).toBe('1 model · 6 wounds left');
  });

  it('several tough models among others count every wound the pips count', () => {
    const c = card(...Array.from({ length: 18 }, () => m('Trooper', 1, 1, false, 't')), ...Array.from({ length: 3 }, () => m('Weapon Team', 3, 3, false, 'wt')));
    expect([cardPips(c), countNote(c)]).toEqual(['27/27', '21 models · 27 wounds left']);
  });

  it('past twelve pips draw a count', () => {
    const c = card(...Array.from({ length: 13 }, (_, i) => m('Gaunt', 1, 1, i === 0, 'g')));
    expect([cardPips(c), groupPips(c.groups[0]!)]).toEqual(['12/13', '12/13']);
  });

  it('the activated chip reads its actions', () => {
    const c = card(m('Warrior', 1, 1, false, 'w'));
    expect(c.sheet.chips.map(chipText)).toEqual(['Advance', 'Shaken']);
  });
});
