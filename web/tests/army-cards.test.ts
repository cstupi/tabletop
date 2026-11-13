import { describe, expect, it } from 'vitest';
import type { ModelSlot, Unit } from '$lib/api/types';
import { meshLine, meshPips, slotGroups, slotMarks, slotRows, statBadges, systemName } from '$lib/armies/cards';

const slot = (over: Partial<ModelSlot> = {}): ModelSlot => ({
  id: crypto.randomUUID(), ordinal: 0, name: 'Trooper', stats: {}, properties: [], base: { shape: 'circle', a: 1.26, b: 1.26 }, height: 1.9, weapons: [], ...over,
});
const unit = (models: ModelSlot[], stats: Record<string, string> = { Quality: '3+', Defense: '4+' }): Unit =>
  ({ id: crypto.randomUUID(), ordinal: 0, name: 'Flayer Cavalry', stats, properties: [], models });

describe('army cards', () => {
  it('names the systems the import takes, and passes any other through', () => {
    expect(systemName('grimdark-future')).toBe('Grimdark Future');
    expect(systemName('age-of-fantasy')).toBe('Age of Fantasy');
    expect(systemName('something-else')).toBe('something-else');
  });

  it('draws four pips: all, none, and a part rounded but never to all or none', () => {
    expect(meshPips(13, 13)).toEqual([true, true, true, true]);
    expect(meshPips(0, 13)).toEqual([false, false, false, false]);
    expect(meshPips(9, 13)).toEqual([true, true, true, false]);
    expect(meshPips(1, 40)).toEqual([true, false, false, false]);
    expect(meshPips(39, 40)).toEqual([true, true, true, false]);
    expect(meshPips(0, 0)).toEqual([false, false, false, false]);
  });

  it('says the mesh count in a few words', () => {
    expect(meshLine(9, 13)).toBe('meshes 9 of 13');
    expect(meshLine(13, 13)).toBe('all meshes');
    expect(meshLine(0, 13)).toBe('no meshes');
  });

  it('is one row for every model when they share mesh, base and height, else a row per model', () => {
    const same = unit([slot(), slot(), slot()]);
    expect(slotRows(same)).toEqual({ shared: true, rows: same.models });
    const one = unit([slot({ name: 'Reaper Champion' })]);
    expect(slotRows(one)).toEqual({ shared: false, rows: one.models });
    const mixed = unit([slot(), slot({ assetId: 'a' }), slot()]);
    expect(slotRows(mixed)).toEqual({ shared: false, rows: mixed.models });
    const based = unit([slot(), slot({ base: { shape: 'circle', a: 1.57, b: 1.57 } })]);
    expect(slotRows(based).shared).toBe(false);
  });

  it('badges Quality and Defense with their icons, then any other stat', () => {
    expect(statBadges(unit([], { Quality: '3+', Defense: '4+' }))).toEqual([
      { key: 'Quality', icon: 'quality', value: '3+' }, { key: 'Defense', icon: 'defense', value: '4+' },
    ]);
    expect(statBadges(unit([], { Defense: '5+', Quality: '4+', Wounds: '3' }))).toEqual([
      { key: 'Quality', icon: 'quality', value: '4+' }, { key: 'Defense', icon: 'defense', value: '5+' }, { key: 'Wounds', icon: null, value: '3' },
    ]);
  });
  it('marks a model with the weapons and rules the rest of its unit lacks', () => {
    const w = (name: string) => ({ id: crypto.randomUUID(), name, stats: {}, properties: [] });
    const rifle = () => slot({ weapons: [w('Battle Rifle'), w('Strike')], properties: [{ name: 'Hold the Line' }] });
    const banner = slot({ weapons: [w('Battle Rifle'), w('Strike')], properties: [{ name: 'Hold the Line' }, { name: 'Bravery Aura' }] });
    const team = slot({ weapons: [w('Autocannon'), w('Strike')], properties: [{ name: 'Hold the Line' }, { name: 'Tough', parameter: 3 }] });
    const hero = slot({ name: 'Captain 1', weapons: [w('Pistol')], properties: [{ name: 'Hero' }] });
    banner.name = 'Trooper (War Banner) 3'; team.name = 'Trooper (Weapon Team) 4';
    const squad = unit([{ ...rifle(), name: 'Trooper 1' }, { ...rifle(), name: 'Trooper 2' }, banner, team, hero]);
    expect(slotMarks(squad, hero)).toEqual([]);
    expect(slotMarks(squad, squad.models[0]!)).toEqual(['Battle Rifle']);
    expect(slotMarks(squad, banner)).toEqual(['Battle Rifle', 'Bravery Aura']);
    expect(slotMarks(squad, team)).toEqual(['Autocannon', 'Tough(3)']);
    const same = unit([{ ...rifle(), name: 'Trooper 1' }, { ...rifle(), name: 'Trooper 2' }]);
    expect(slotMarks(same, same.models[0]!)).toEqual([]);
    expect(slotMarks(unit([banner]), banner)).toEqual([]);
  });
  it('draws alike models as one row and a model unlike the rest as its own', () => {
    const w = (name: string) => ({ id: crypto.randomUUID(), name, stats: {}, properties: [] });
    const rifle = (n: number, over: Partial<ModelSlot> = {}) => slot({ name: `Trooper ${n}`, weapons: [w('Rifle')], ...over });
    const banner = slot({ name: 'Trooper 4', weapons: [w('Rifle')], properties: [{ name: 'Bravery Aura' }] });
    const squad = unit([rifle(1), rifle(2), rifle(3), banner, slot({ name: 'Trooper 5', weapons: [w('Laser')] }), slot({ name: 'Captain 1' })]);
    expect(slotGroups(squad).map((g) => [g.label, g.marks, g.slots.length])).toEqual([
      ['Trooper ×3', ['Rifle'], 3], ['Trooper 4', ['Rifle', 'Bravery Aura'], 1], ['Trooper 5', ['Laser'], 1], ['Captain 1', [], 1],
    ]);
    // One of the three given its own mesh leaves the group; the other two stay one row.
    const meshed = unit([rifle(1), rifle(2, { assetId: 'a1' }), rifle(3)]);
    expect(slotGroups(meshed).map((g) => [g.label, g.slots.length])).toEqual([['Trooper ×2', 2], ['Trooper 2', 1]]);
    const same = unit([rifle(1), rifle(2)]);
    expect(slotGroups(same).map((g) => [g.label, g.marks])).toEqual([['Trooper ×2', []]]);
  });
});
