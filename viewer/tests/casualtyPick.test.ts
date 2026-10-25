import { describe, expect, it } from 'vitest';
import { CasualtyPick } from '../src/lib/play/casualtyPick';
import type { Allocated, Allocation } from '../src/lib/protocol/wire.generated';

const M1 = '0198b7ac-0011-7000-8000-000000000011', M2 = '0198b7ac-0012-7000-8000-000000000012', M3 = '0198b7ac-0013-7000-8000-000000000013';
const ELSEWHERE = '0198b7ac-0019-7000-8000-000000000019';
const legal = (targets: string[], amount: number, maxTargets?: number): Allocation => ({ kind: 'allocation', targets, amount, ...(maxTargets === undefined ? {} : { maxTargets }) });
const left = (o: Record<string, number>) => new Map(Object.entries(o));
const obj = (m: ReadonlyMap<string, number>) => Object.fromEntries(m);
const counts = (a: Allocated | null) => a!.counts;

describe('CasualtyPick', () => {
  it('add stops at wounds left', () => {
    const c = new CasualtyPick(legal([M1, M2], 3), left({ [M1]: 2, [M2]: 1 }));
    c.add(M2); c.add(M2);
    expect(c.counts.get(M2)).toBe(1);
    expect([c.chosen, c.complete]).toEqual([1, false]);
    c.add(M1);
    expect(c.complete).toBe(false);
    c.add(M1); c.add(M1);
    expect([c.counts.get(M1), c.chosen, c.complete]).toEqual([2, 3, true]);
  });

  it('nothing past the amount and nothing outside the targets', () => {
    const c = new CasualtyPick(legal([M1, M2], 1), left({ [M1]: 1, [M2]: 1 }));
    c.add(ELSEWHERE);
    expect(c.chosen).toBe(0);
    c.add(M1); c.add(M2);
    expect([c.chosen, c.complete, c.counts.has(M2)]).toEqual([1, true, false]);
  });

  it('remove takes one away and an absent count is one wound', () => {
    const c = new CasualtyPick(legal([M1, M2], 2), left({}));
    expect(c.required).toBe(2);
    c.add(M1); c.add(M1);
    expect(c.counts.get(M1)).toBe(1);
    c.remove(M1); c.remove(M1); c.remove(M2);
    expect(c.counts.size).toBe(0);
    expect(c.answer()).toBeNull();
  });

  it('auto fills what is left in the ask\'s order', () => {
    const c = new CasualtyPick(legal([M1, M2, M3], 2), left({ [M1]: 1, [M2]: 1, [M3]: 1 }));
    c.add(M3);
    c.auto();
    expect(obj(c.counts)).toEqual({ [M3]: 1, [M1]: 1 });
    expect(counts(c.answer())).toEqual({ [M3]: 1, [M1]: 1 });
  });

  it('wounds past every model\'s capacity complete at capacity and answer the whole amount', () => {
    const c = new CasualtyPick(legal([M1, M2], 5), left({ [M1]: 1, [M2]: 1 }));
    expect(c.required).toBe(2);
    c.auto();
    expect(c.complete).toBe(true);
    expect(counts(c.answer())).toEqual({ [M1]: 1, [M2]: 4 });
    expect(c.chosen).toBe(2);
  });

  it('under a max of one target a click on another model moves the pick and the surplus stays on it', () => {
    const c = new CasualtyPick(legal([M1, M2], 4, 1), left({ [M1]: 3, [M2]: 3 }));
    expect([c.required, c.title]).toEqual([3, 'Assign 4 wounds to one model']);
    c.add(M1); c.add(M1); c.add(M2);
    expect(obj(c.counts)).toEqual({ [M2]: 1 });
    c.add(M2); c.add(M2); c.add(M2);
    expect(c.complete).toBe(true);
    expect(counts(c.answer())).toEqual({ [M2]: 4 });
    const auto = new CasualtyPick(legal([M1, M2], 2, 1), left({ [M1]: 3, [M2]: 3 }));
    auto.auto();
    expect(obj(auto.counts)).toEqual({ [M1]: 2 });
  });

  it('a click on a model that can take no more puts it back', () => {
    const c = new CasualtyPick(legal([M1, M2, M3], 2), left({ [M1]: 1, [M2]: 1, [M3]: 1 }));
    c.click(M1); c.click(M1);
    expect(c.counts.size).toBe(0);
    c.click(M1); c.click(M2); c.click(M2);
    expect(obj(c.counts)).toEqual({ [M1]: 1 });
    const tough = new CasualtyPick(legal([M1, M2], 3), left({ [M1]: 2, [M2]: 3 }));
    tough.click(M1); tough.click(M1);
    expect(tough.counts.get(M1)).toBe(2);
    tough.click(M1);
    expect(tough.counts.size).toBe(0);
    const one = new CasualtyPick(legal([M1, M2], 4, 1), left({ [M1]: 3, [M2]: 3 }));
    one.click(M1); one.click(M1); one.click(M1); one.click(M2);
    expect(obj(one.counts)).toEqual({ [M2]: 1 });
  });

  it('clear puts every model back', () => {
    const c = new CasualtyPick(legal([M1, M2], 2), left({ [M1]: 1, [M2]: 1 }));
    c.auto();
    c.clear();
    expect([c.chosen, c.complete]).toEqual([0, false]);
  });

  it('one-wound models read as models to remove', () => {
    const c = new CasualtyPick(legal([M1, M2, M3], 7), left({ [M1]: 1, [M2]: 1, [M3]: 1 }));
    expect([c.oneEach, c.title, c.status]).toEqual([true, 'Remove 3 models', '0 of 3 chosen']);
    expect(c.hint.startsWith('Click a model to remove it')).toBe(true);
    const tough = new CasualtyPick(legal([M1, M2], 2), left({ [M1]: 1, [M2]: 3 }));
    expect([tough.oneEach, tough.title]).toEqual([false, 'Assign 2 wounds']);
  });
});
