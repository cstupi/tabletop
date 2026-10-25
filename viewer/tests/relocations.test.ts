import { describe, expect, it } from 'vitest';
import { PendingDrops } from '../src/lib/play/pendingDrops';
import { relocations } from '../src/lib/play/relocations';
import type { GameEvent, ModelRelocation } from '../src/lib/protocol/wire.generated';

const at = (x: number, y: number) => ({ kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees: 0 } } }) as const;
const move = (id: string, x: number, y: number): ModelRelocation => ({ modelId: id, from: at(0, 0), to: at(x, y) });
const event = (payload: unknown): GameEvent => ({ seq: 1, payload } as GameEvent);

describe('relocations (AnimationQueue.Relocations)', () => {
  it('a hand move\'s ModelsMoved, every ModelsMoved among a step\'s changes, and nothing for any other event', () => {
    expect(relocations(event({ kind: 'models_moved', moves: [move('a', 1, 1)] }))).toEqual([move('a', 1, 1)]);
    expect(relocations(event({
      kind: 'step_completed', frameId: 'f', step: 'move', rolls: [],
      changes: [{ kind: 'models_moved', moves: [move('a', 1, 1)] }, { kind: 'wounds_set', modelId: 'b', wounds: 1 }, { kind: 'models_moved', moves: [move('b', 2, 2)] }],
    }))).toEqual([move('a', 1, 1), move('b', 2, 2)]);
    expect(relocations(event({ kind: 'turn_started', teamId: 't' }))).toEqual([]);
  });

  it('an answer\'s move, which arrives as a step, ends the hold on its drops', () => {
    const drops = new PendingDrops();
    drops.add('1', [move('a', 3, 4), move('b', 5, 6)]);
    const step = event({ kind: 'step_completed', frameId: 'f', step: 'move', rolls: [], changes: [{ kind: 'models_moved', moves: [move('a', 3, 4), move('b', 5, 6)] }] });
    for (const m of relocations(step)) drops.consume(m);
    expect(drops.all.size).toBe(0);
  });
});
