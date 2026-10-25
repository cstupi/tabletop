import { describe, expect, it } from 'vitest';
import type { Contact, ContactBody, ContactTerms, Geometry, Placement } from '../src/lib/geometry';
import { allWords, bodiesOf, move, placement, words } from '../src/lib/play/contactCheck';
import { Applier } from '../src/lib/live/apply';
import type { MoveRegion } from '../src/lib/protocol/wire.generated';
import { sample } from './play';

const at = (x: number, y: number): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: 0 } });
const size = { width: 72, length: 48 };

function fake(found: Map<string, Contact> = new Map()) {
  const calls: { models: readonly ContactBody[]; proposed: ReadonlyMap<string, Placement>; terms: ContactTerms }[] = [];
  const g = { contacts: (models: readonly ContactBody[], proposed: ReadonlyMap<string, Placement>, _t: unknown, terms: ContactTerms) => { calls.push({ models, proposed, terms }); return found; } } as unknown as Geometry;
  return { g, calls };
}
const region = (over: Partial<MoveRegion> = {}): MoveRegion => ({
  kind: 'move_region', modelIds: [], allowance: 6, impassableVolumeIds: ['w'], difficultVolumeIds: ['d'], difficultCap: 6, stoppable: false, passesModels: false, ignoresTerrain: false, ...over,
});

describe('contactCheck', () => {
  it('move builds the bodies and the Ask\'s terms', () => {
    const s = sample();
    const p = new Applier(s.board, s.game).projection;
    p.models.get(s.model.id)!.location = { kind: 'table', placement: at(5, 5) };
    const { g, calls } = fake();
    const proposed = new Map([[s.model.id, at(9, 9)]]);
    move(g, p, region({ separation: 1 }), proposed, size);
    expect(calls[0]!.proposed).toBe(proposed);
    expect(calls[0]!.models).toEqual(bodiesOf(p));
    expect(calls[0]!.models.find((b) => b.id === s.model.id)).toMatchObject({ unitId: s.squad.id, at: at(5, 5) });
    expect(calls[0]!.models.find((b) => b.id === s.rival.id)!.at).toBeUndefined();
    expect(calls[0]!.terms).toEqual({
      separation: 1, passesModels: false, landing: true, size,
      move: { allowance: 6, impassable: ['w'], difficult: ['d'], difficultCap: 6 },
    });
    move(g, p, region({ ignoresTerrain: true, passesModels: true }), proposed, size);
    expect(calls[1]!.terms).toMatchObject({ passesModels: true, landing: false });
    expect(calls[1]!.terms.separation).toBeUndefined();
  });

  it('placement asks for overlaps only', () => {
    const s = sample();
    const p = new Applier(s.board, s.game).projection;
    const { g, calls } = fake();
    placement(g, p, new Map());
    expect(calls[0]!.terms).toEqual({ passesModels: true, landing: false });
  });

  it('words say what each contact is', () => {
    const name = (id: string) => (id === 'o' ? 'Orc Boyz' : '?');
    expect(words({ modelId: 'm', kind: 'on_base', unitId: 'o' }, undefined, name)).toBe('overlaps a base');
    expect(words({ modelId: 'm', kind: 'crosses' }, undefined, name)).toBe('path crosses a model');
    expect(words({ modelId: 'm', kind: 'in_impassable' }, undefined, name)).toBe('ends in impassable terrain');
    expect(words({ modelId: 'm', kind: 'too_close', unitId: 'o' }, 1, name)).toBe('within 1" of Orc Boyz');
    expect(words({ modelId: 'm', kind: 'too_close' }, 1.25, name)).toBe('within 1.3" of another unit');
    expect(allWords(new Map([['m', { modelId: 'm', kind: 'crosses' } as Contact]]), undefined, name)).toEqual(new Map([['m', 'path crosses a model']]));
  });
});
