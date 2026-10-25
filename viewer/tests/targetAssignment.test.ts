import { describe, expect, it } from 'vitest';
import { TargetAssignment, targetsOf } from '../src/lib/play/targetAssignment';
import type { Assignment } from '../src/lib/protocol/wire.generated';
import { option } from './play';

const T1 = '0198b7ac-0001-7000-8000-000000000001', T2 = '0198b7ac-0002-7000-8000-000000000002', T3 = '0198b7ac-0003-7000-8000-000000000003';
const NOT_OFFERED = '0198b7ac-0009-7000-8000-000000000009';
const legal = (...groups: string[]): Assignment => ({ kind: 'assignment', items: groups.map((g) => option(g, g)), targets: [T1, T2, T3], maxDistinctTargets: 2 });
const obj = (m: ReadonlyMap<string, string>) => Object.fromEntries(m);

describe('TargetAssignment', () => {
  it('every group starts picked and one click aims them all', () => {
    const t = new TargetAssignment(legal('Rifles', 'Plasma Gun'));
    expect(t.groups.map((g) => g.id)).toEqual(['Rifles', 'Plasma Gun']);
    expect(t.picked).toEqual(new Set(['Rifles', 'Plasma Gun']));
    expect(t.answer()).toBeNull();
    expect(t.aim(T1)).toBe(true);
    expect(t.answer()).toEqual({ kind: 'assigned', targets: { Rifles: T1, 'Plasma Gun': T1 } });
  });

  it('a chip picks its group alone for a second target', () => {
    const t = new TargetAssignment(legal('Rifles', 'Plasma Gun'));
    t.aim(T1);
    t.pickOnly('Plasma Gun');
    expect(t.picked).toEqual(new Set(['Plasma Gun']));
    expect(t.aim(T2)).toBe(true);
    expect(obj(t.assigned)).toEqual({ Rifles: T1, 'Plasma Gun': T2 });
    expect(t.canTarget(T3)).toBe(true);
  });

  it('two targets taken and no other unit glows', () => {
    const t = new TargetAssignment(legal('Rifles', 'Plasma Gun', 'Grenades'));
    t.aim(T1);
    t.pickOnly('Plasma Gun'); t.aim(T2);
    t.pickOnly('Grenades');
    expect(t.canTarget(T3)).toBe(false);
    expect(t.aim(T3)).toBe(false);
    expect(t.canTarget(T1)).toBe(true); expect(t.canTarget(T2)).toBe(true);
    expect(t.assigned.get('Grenades')).toBe(T1);
  });

  it('a unit the ask did not offer is never a target', () => {
    const t = new TargetAssignment(legal('Rifles'));
    expect(t.canTarget(NOT_OFFERED)).toBe(false);
    expect(t.aim(NOT_OFFERED)).toBe(false);
    expect(t.assigned.size).toBe(0);
  });

  it('one aimed group can fire while the others hold and clear starts over', () => {
    const t = new TargetAssignment(legal('Rifles', 'Plasma Gun'));
    expect(t.answer()).toBeNull();
    t.pickOnly('Rifles'); t.aim(T1);
    expect(t.answer()).toEqual({ kind: 'assigned', targets: { Rifles: T1 } });
    t.clear();
    expect(t.assigned.size).toBe(0);
    expect(t.picked).toEqual(new Set(['Rifles', 'Plasma Gun']));
  });

  it('two targets taken leave only those two as candidates', () => {
    const t = new TargetAssignment(legal('Rifles', 'Plasma Gun'));
    expect(t.candidates).toEqual([T1, T2, T3]);
    t.aim(T1);
    expect(t.candidates).toEqual([T1, T2, T3]);
    t.pickOnly('Plasma Gun'); t.aim(T2);
    expect(t.candidates).toEqual([T1, T2]);
    expect(t.aim(T1)).toBe(true);
    expect(t.candidates).toEqual([T1, T2, T3]);
  });

  const split = (): Assignment => ({
    kind: 'assignment', items: [option('Rifles', 'Rifles'), option('Pistols', 'Pistols')], targets: [T1, T2], maxDistinctTargets: 2,
    targetsByItem: { Rifles: [T1, T2], Pistols: [T1] },
  });

  it('a click aims only the picked groups that can shoot the unit', () => {
    const t = new TargetAssignment(split());
    expect(t.aim(T2)).toBe(true);
    expect(obj(t.assigned)).toEqual({ Rifles: T2 });
    expect(t.answer()).not.toBeNull();
    expect(t.aim(T1)).toBe(true);
    expect(obj(t.assigned)).toEqual({ Rifles: T1, Pistols: T1 });
    t.pickOnly('Rifles'); t.aim(T2);
    expect(obj(t.assigned)).toEqual({ Rifles: T2, Pistols: T1 });
    expect(t.answer()).not.toBeNull();
  });

  it('a unit no picked group can shoot is not a candidate', () => {
    const t = new TargetAssignment(split());
    expect(t.candidates).toEqual([T1, T2]);
    t.pickOnly('Pistols');
    expect(t.canTarget(T2)).toBe(false);
    expect(t.candidates).toEqual([T1]);
    expect(t.aim(T2)).toBe(false);
    expect(t.assigned.size).toBe(0);
  });

  it('a chip names its group and how many weapons it holds', () => {
    expect(TargetAssignment.chip(option('Rifles', 'Rifles', ['a', 'b', 'c', 'd']))).toBe('Rifles ×4');
    expect(TargetAssignment.chip(option('Plasma Gun', 'Plasma Gun', ['a']))).toBe('Plasma Gun ×1');
  });

  it('a chip names its target or says it holds fire', () => {
    const rifles = option('Rifles', 'Rifles', ['a']);
    expect(TargetAssignment.chip(rifles, 'Red unit 2')).toBe('Rifles ×1 → Red unit 2');
    expect(TargetAssignment.chip(rifles, null)).toBe('Rifles ×1 · holds fire');
  });

  it('targetsOf falls back to every target without a per-item map and to none for an unlisted item', () => {
    expect(targetsOf(legal('Rifles'), 'Rifles')).toEqual([T1, T2, T3]);
    expect(targetsOf(split(), 'Pistols')).toEqual([T1]);
    expect(targetsOf(split(), 'Grenades')).toEqual([]);
  });
});
