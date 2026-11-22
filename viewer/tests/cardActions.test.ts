import { describe, expect, it } from 'vitest';
import { actionTip, actionsOf, numberActions } from '../src/lib/play/cardActions';
import type { LegalSet, MoveRegion } from '../src/lib/protocol/wire.generated';
import { askOf, projectionOf, sample } from './play';

// The owner, 2026-11-21: "if 1,2,3 decides then users should hit 1 not enter". Godot's CardActions.
const s = sample();
const p = projectionOf(s.game, s.board).projection;
const ask = (legal: LegalSet, question = 'x', allowPass = false) => askOf(legal, { question, allowPass });
const region = (over: Partial<MoveRegion> = {}): MoveRegion => ({
  kind: 'move_region', modelIds: [s.model.id], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, ...over,
} as MoveRegion);
const ids = (a: readonly { id: string }[]) => a.map((x) => x.id);

describe('cardActions', () => {
  it('shooting holds fire, then clear, then pass', () => {
    const a = actionsOf(ask({ kind: 'assignment', items: [], targets: [], maxDistinctTargets: 1 } as LegalSet, 'declare_targets', true), 'assign', p);
    expect(ids(a)).toEqual(['fire', 'clear', 'pass']);
    expect(a.map((x) => x.primary)).toEqual([true, false, false]);
    expect(a.map((x) => x.word)).toEqual(['Fire', 'Clear', 'Pass']);
  });

  it('a move commits, then stops here when stoppable', () => {
    expect(ids(actionsOf(ask(region({ stoppable: true }), 'move'), 'move', p))).toEqual(['commit', 'stop']);
    expect(actionsOf(ask(region({ stoppable: true }), 'move'), 'move', p)[1]!.word).toBe('Stop here');
    expect(ids(actionsOf(ask(region(), 'move'), 'move', p))).toEqual(['commit']);
  });

  it('the other modes\' foot actions', () => {
    expect(ids(actionsOf(ask({ kind: 'confirmation' } as LegalSet), 'confirm', p))).toEqual(['confirm']);
    expect(actionsOf(ask({ kind: 'confirmation' } as LegalSet, 'end_round'), 'confirm', p)[0]!.word).toBe('End the round');
    expect(ids(actionsOf(ask({ kind: 'token_spend', maxByModel: {} } as LegalSet), 'tokenSpend', p))).toEqual(['spend']);
    expect(ids(actionsOf(ask({ kind: 'allocation', targets: [], amount: 1 } as LegalSet), 'allocate', p))).toEqual(['confirm', 'auto', 'clear']);
    expect(ids(actionsOf(ask({ kind: 'pieces', eligible: [], min: 1, max: 2 } as LegalSet), 'pickPieces', p))).toEqual(['done']);
    expect(actionsOf(ask({ kind: 'pieces', eligible: [], min: 1, max: 1 } as LegalSet), 'pickPieces', p)).toEqual([]);
    const activate = actionsOf(ask({ kind: 'options', choices: [] }, 'act_or_end_phase', true), 'choose', p);
    expect([ids(activate), activate[0]!.word]).toEqual([['pass'], 'End phase']);
  });

  it('keys follow the tiles, primary first; past nine none, never a duplicate', () => {
    const a = actionsOf(ask({ kind: 'assignment', items: [], targets: [], maxDistinctTargets: 1 } as LegalSet, 'declare_targets', true), 'assign', p);
    expect(numberActions(a, 0).map((x) => x.key)).toEqual([1, 2, 3]);
    expect(numberActions(a, 3).map((x) => x.key)).toEqual([4, 5, 6]);
    expect(numberActions(a, 7).map((x) => x.key)).toEqual([8, 9, 0]);
    expect(numberActions(a, 12).map((x) => x.key)).toEqual([0, 0, 0]);
    expect(numberActions(actionsOf(ask({ kind: 'confirmation' } as LegalSet), 'confirm', p), 0)[0]!.key).toBe(1);
  });

  it('a tip is the word and its key, and the reason when dim', () => {
    expect(actionTip({ id: 'fire', word: 'Fire', icon: 'shoot', primary: true, key: 1, enabled: true, reason: null })).toBe('Fire  1');
    expect(actionTip({ id: 'clear', word: 'Clear', icon: 'remove', primary: false, key: 0, enabled: true, reason: null })).toBe('Clear');
    expect(actionTip({ id: 'fire', word: 'Fire', icon: 'shoot', primary: true, key: 1, enabled: false, reason: 'No target' })).toBe('Fire  1 · No target');
  });
});
