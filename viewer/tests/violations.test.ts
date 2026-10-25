import { describe, expect, it } from 'vitest';
import { Violations } from '../src/lib/play/violations';

const A = 'a', B = 'b', C = 'c';
const marks = (o: Record<string, string>) => new Map(Object.entries(o));

describe('Violations', () => {
  it('a measure replaces only the models it was taken over', () => {
    const v = new Violations();
    v.measure([A, B], marks({ [A]: 'overlaps a base', [B]: 'path crosses a model' }));
    v.measure([C], marks({ [C]: 'within 1" of Orcs' }));
    v.measure([B], marks({}));
    expect(v.marked).toEqual(marks({ [A]: 'overlaps a base', [C]: 'within 1" of Orcs' }));
  });

  it('changed is raised only when the marks change', () => {
    const v = new Violations();
    let raised = 0;
    v.subscribe(() => raised++);
    v.measure([A], marks({ [A]: 'overlaps a base' }));
    v.measure([A], marks({ [A]: 'overlaps a base' }));
    expect(raised).toBe(1);
    v.measure([A], marks({}));
    expect(raised).toBe(2);
    v.clear();
    expect(raised).toBe(2);
  });

  it('with a subject, a mark reads as a sentence naming the first model with it; the board\'s labels keep the words', () => {
    const v = new Violations((id) => (id === C ? null : id.toUpperCase()));
    v.measure([A, B, C], marks({ [A]: 'within 1" of Orcs', [B]: 'within 1" of Orcs', [C]: 'overlaps a base' }));
    expect(v.marked).toEqual(marks({ [A]: 'A would end within 1" of Orcs (p.7)', [B]: 'A would end within 1" of Orcs (p.7)', [C]: 'overlaps a base' }));
    expect(v.labels([B, A, C])).toEqual([{ modelId: B, reason: 'within 1" of Orcs' }, { modelId: C, reason: 'overlaps a base' }]);
    v.refused([C], 'C would be placed outside its deployment zone (p.6)');
    v.measure([C], marks({}));
    expect(v.marked.get(C)).toBe('C would be placed outside its deployment zone (p.6)');
  });

  it('a refusal marks its models with the server\'s reason until one of them moves', () => {
    const v = new Violations();
    v.refused([A, B], 'would move through another model');
    expect(v.marked.get(A)).toBe('would move through another model');
    expect(v.marked.get(B)).toBe('would move through another model');
    v.measure([A, B], marks({}));
    expect(v.marked.size).toBe(2);
    v.moved([C]);
    expect(v.marked.size).toBe(2);
    v.moved([B]);
    expect(v.marked.size).toBe(0);
  });

  it('the client\'s own reason for a model comes before the server\'s', () => {
    const v = new Violations();
    v.refused([A], 'refused');
    v.measure([A], marks({ [A]: 'overlaps a base' }));
    expect(v.marked.get(A)).toBe('overlaps a base');
  });

  it('only strict waits on a mark', () => {
    const v = new Violations();
    expect(v.blocks('strict')).toBe(false);
    v.measure([A], marks({ [A]: 'overlaps a base' }));
    expect(v.blocks('strict')).toBe(true);
    expect(v.blocks('warn')).toBe(false);
    expect(v.blocks('silent')).toBe(false);
  });

  it('labels name each reason once over its first model', () => {
    const v = new Violations();
    v.measure([A, B, C], marks({ [A]: 'overlaps a base', [B]: 'overlaps a base', [C]: 'within 1" of Orcs' }));
    expect(v.labels([A, B, C])).toEqual([{ modelId: A, reason: 'overlaps a base' }, { modelId: C, reason: 'within 1" of Orcs' }]);
    expect(v.labels([B, C, A])).toEqual([{ modelId: B, reason: 'overlaps a base' }, { modelId: C, reason: 'within 1" of Orcs' }]);
  });

  it('clear drops the measure and the refusal', () => {
    const v = new Violations();
    v.measure([A], marks({ [A]: 'overlaps a base' }));
    v.refused([B], 'refused');
    v.clear();
    expect(v.marked.size).toBe(0);
  });
});
