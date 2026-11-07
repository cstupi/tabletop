import { describe, expect, it } from 'vitest';
import type { ActivationBlock } from '../src/lib/play/combatLog';
import { BACK, FORWARD, replayCaption, replayTicks } from '../src/lib/replay/replayBarView';
import type { GameEvent } from '../src/lib/protocol/wire.generated';

// ReplayBarViewTests
const e = (seq: number, payload: object): GameEvent => ({ seq, payload } as unknown as GameEvent);

describe('the replay bar', () => {
  it('goes back by round, turn, action and step, then forward by step, action, turn and round', () => {
    expect(BACK.map((b) => b.unit)).toEqual(['round', 'turn', 'action', 'step']);
    expect(FORWARD.map((b) => b.unit)).toEqual(['step', 'action', 'turn', 'round']);
    expect(BACK.map((b) => b.label)).toEqual(['Round', 'Turn', 'Action', 'Step']);
  });

  it('marks each round start along the log', () => {
    const log = [e(1, { kind: 'round_started', round: 1 }), e(2, { kind: 'turn_started' }), e(3, { kind: 'score_set' }), e(4, { kind: 'score_set' }),
      e(5, { kind: 'round_started', round: 2 }), e(6, { kind: 'turn_started' }), e(7, { kind: 'score_set' }), e(8, { kind: 'score_set' })];
    expect(replayTicks(log)).toEqual([{ at: 1 / 8, round: 1 }, { at: 5 / 8, round: 2 }]);
    expect(replayTicks([])).toEqual([]);
  });

  it('captions the latest live action\'s header with the round spelled out', () => {
    const block = { type: 'block', undone: false, header: 'R2 · Team 1 · Red unit 1 · Charge → Blue unit 2' } as ActivationBlock;
    const undone = { ...block, undone: true, header: 'R2 · Team 1 · Red unit 3 · Hold' };
    expect(replayCaption([block, undone], 2)).toBe('Round 2 · Team 1 · Red unit 1 · Charge → Blue unit 2');
    expect(replayCaption([], 3)).toBe('Round 3');
    expect(replayCaption([], 0)).toBe('Before round 1');
  });
});
