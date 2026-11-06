import { describe, expect, it } from 'vitest';
import { ModelPicks, type PickCandidate } from '../src/lib/play/modelPicks';

const c = (modelId: string, unitId: string, x: number, y: number): PickCandidate => ({ modelId, unitId, x, y });
const a1 = c('a1', 'A', 1, 1), a2 = c('a2', 'A', 2, 1), a3 = c('a3', 'A', 3, 1);
const b1 = c('b1', 'B', 10, 1), b2 = c('b2', 'B', 11, 1);
const all = [a1, a2, a3, b1, b2];
const box = (fx: number, fy: number, tx: number, ty: number) => ({ from: { x: fx, y: fy }, to: { x: tx, y: ty } });

describe('ModelPicks', () => {
  it('a toggle adds a model and a second takes it out; the last one out leaves none', () => {
    const p = ModelPicks.none.toggle('A', 'a1').toggle('A', 'a2');
    expect(p.modelIds).toEqual(['a1', 'a2']);
    expect(p.unitId).toBe('A');
    expect(p.toggle('A', 'a1').modelIds).toEqual(['a2']);
    expect(p.toggle('A', 'a1').toggle('A', 'a2')).toBe(ModelPicks.none);
  });

  it('a toggle on another unit starts over with that model alone', () => {
    const p = ModelPicks.none.toggle('A', 'a1').toggle('A', 'a2').toggle('B', 'b1');
    expect([p.unitId, p.modelIds]).toEqual(['B', ['b1']]);
  });

  it('a box takes base centres inside it, drawn either way, edges included', () => {
    expect(ModelPicks.inBox(all, box(0, 0, 2.5, 2))).toEqual({ unitId: 'A', modelIds: ['a1', 'a2'] });
    expect(ModelPicks.inBox(all, box(2.5, 2, 0, 0))).toEqual({ unitId: 'A', modelIds: ['a1', 'a2'] });
    expect(ModelPicks.inBox(all, box(1, 1, 1, 1))).toEqual({ unitId: 'A', modelIds: ['a1'] });
    expect(ModelPicks.inBox(all, box(50, 50, 60, 60))).toBeNull();
  });

  it('a box over several units takes the one with most inside; a tie the unit of the model nearest the press', () => {
    expect(ModelPicks.inBox(all, box(1.5, 0, 10.5, 2))!.unitId).toBe('A');      // a2, a3 against b1
    expect(ModelPicks.inBox([a3, b1], box(12, 0, 0, 2))!.unitId).toBe('B');
    expect(ModelPicks.inBox([a3, b1], box(0, 0, 12, 2))!.unitId).toBe('A');
  });

  it('a box adds to its own unit’s picks, starts over on another unit, and over nothing changes nothing', () => {
    const p = ModelPicks.none.toggle('A', 'a1');
    const q = p.boxed(all, box(1.5, 0, 3.5, 2));
    expect(q.modelIds).toEqual(['a1', 'a2', 'a3']);
    expect(q.boxed(all, box(2.5, 0, 12, 2)).modelIds).toEqual(['b1', 'b2']);    // a3, b1, b2: B has most
    expect(p.boxed(all, box(50, 50, 60, 60))).toBe(p);
  });

  it('a drag on a picked model holds the picks in unit order and keeps them; on another model the whole unit, and they clear', () => {
    const p = ModelPicks.none.toggle('A', 'a3').toggle('A', 'a1');
    expect(p.held('a3', ['a1', 'a2', 'a3'])).toEqual(['a1', 'a3']);
    expect(p.pressed('a3')).toBe(p);
    expect(p.held('a2', ['a1', 'a2', 'a3'])).toEqual(['a1', 'a2', 'a3']);
    expect(p.pressed('a2')).toBe(ModelPicks.none);
  });

  it('a selection carries the picks: of reads them back, a unit with none selects the whole unit', () => {
    const p = ModelPicks.none.toggle('A', 'a1');
    expect(p.selection('A')).toEqual({ pieceId: 'A', modelIds: ['a1'] });
    expect(p.selection('B')).toEqual({ pieceId: 'B', modelIds: [] });
    expect(ModelPicks.of(p.selection('A')).modelIds).toEqual(['a1']);
    expect(ModelPicks.of({ pieceId: 'A', modelIds: [] })).toBe(ModelPicks.none);
    expect(ModelPicks.of(null)).toBe(ModelPicks.none);
  });
});
