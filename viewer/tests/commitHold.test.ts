import { describe, expect, it } from 'vitest';
import { CommitHold } from '../src/lib/play/commitHold';
import type { Location } from '../src/lib/protocol/wire.generated';

const A = 'a', B = 'b', ASK = 'ask';
const at = (x: number, y: number, yaw = 0): Location => ({ kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees: yaw } } });
const shelf: Location = { kind: 'shelf' };

describe('CommitHold', () => {
  it('a refusal of the commit hands back its ghosts and the models it moved', () => {
    const hold = new CommitHold();
    hold.remember(ASK, [{ modelId: A, from: at(0, 0), to: at(5, 0, 90) }, { modelId: B, from: at(2, 0), to: at(2, 0) }]);
    hold.sent('t1');
    expect(hold.refused('other')).toBeNull();
    const held = hold.refused('t1')!;
    expect(held.key).toBe(ASK);
    expect(held.at.get(A)).toEqual({ x: 5, y: 0, z: 0 });
    expect(held.yaws.get(A)).toEqual({ degrees: 90 });
    expect(held.moved).toEqual([A]);
    expect(hold.refused('t1')).toBeNull();
  });

  it('a placement from the shelf moves every model', () => {
    const hold = new CommitHold();
    hold.remember(A, [{ modelId: A, from: shelf, to: at(5, 0) }, { modelId: B, from: shelf, to: at(6, 0) }]);
    hold.sent('t1');
    expect(hold.refused('t1')!.moved).toEqual([A, B]);
  });

  it('a stop before moving marks every model', () => {
    const hold = new CommitHold();
    hold.remember(ASK, [{ modelId: A, from: at(0, 0), to: at(0, 0) }, { modelId: B, from: at(2, 0), to: at(2, 0) }]);
    hold.sent('t1');
    expect(hold.refused('t1')!.moved).toEqual([A, B]);
  });

  it('another send after the commit drops it', () => {
    const hold = new CommitHold();
    hold.remember(ASK, [{ modelId: A, from: at(0, 0), to: at(5, 0) }]);
    hold.sent('t1');
    hold.sent('t2');
    expect(hold.refused('t2')).toBeNull();
    expect(hold.refused('t1')).toBeNull();
  });
});
