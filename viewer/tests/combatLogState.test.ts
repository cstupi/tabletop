import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { build, latest, pip, type ActivationBlock } from '../src/lib/play/combatLog';
import { CombatLogCache } from '../src/lib/play/combatLogState';
import { Names } from '../src/lib/play/names';
import { collapsed, type LogEntry } from '../src/lib/play/rollEntry';
import type { EventPayload, GameInfo } from '../src/lib/protocol/wire.generated';
import { BoardSample, D, Frame, P1, Rifle, Roll, Round1, TEAM1, Unit, ctx, finished, started, step, undone } from './logSamples';
import { ev, gameOf, newId } from './play';

// CombatLogCacheTests
const game: GameInfo = gameOf();

function applierOf(...history: EventPayload[]): Applier {
  const a = new Applier(BoardSample, game);
  history.forEach((p, i) => a.apply(ev(i + 1, p, P1)));
  return a;
}

const hitStep = (roll: string): EventPayload =>
  step(Frame, 'roll-to-hit', [{ id: roll, roll: 'hit', threshold: 3, modifiers: [], dice: [D(4, 4, true)] }]);
const shooting = (roll: string): EventPayload[] => [Round1, started(Frame, 'shooting', ctx(Unit, [], [Rifle], 'Hold')), hitStep(roll), finished(Frame)];

const wordsOf = (entries: readonly LogEntry[]): string[] => entries.flatMap((e) => (e.type === 'block' ? (e as ActivationBlock).entries.map((x) => `${e.header}|${word(x)}`) : [word(e)]));
const word = (e: LogEntry): string => `${e.seq}|${e.undone}|` + (e.type === 'roll' ? collapsed(e, pip) : e.type === 'line' ? e.text : '');

describe('CombatLogCache', () => {
  it('the same log state is the same instance and built once', () => {
    const a = applierOf(...shooting(Roll));
    const cache = new CombatLogCache();
    const first = cache.of(a.log, null, game, a.projection);
    const second = cache.of(a.log, null, game, a.projection);
    expect(second).toBe(first);
    expect(second.entries).toBe(first.entries);
    expect(first.entries).toBe(first.entries);
  });

  it('a new event an undone or a pending undo is a new state', () => {
    const a = applierOf(...shooting(Roll));
    const cache = new CombatLogCache();
    const before = cache.of(a.log, null, game, a.projection);
    a.apply(ev(5, { kind: 'score_set', teamId: TEAM1, score: 1 }, P1));
    const appended = cache.of(a.log, null, game, a.projection);
    expect(appended).not.toBe(before);
    const predicted = a.projectTo(4);
    const pending = cache.of(a.log, 4, game, predicted);
    expect(pending).not.toBe(appended);
    a.apply(ev(6, undone(4)));
    expect(cache.of(a.log, null, game, a.projection)).not.toBe(pending);
  });

  it('its entries latest and undo match a fresh build across appends and an undone', () => {
    const second = newId();
    const a = applierOf(...shooting(Roll));
    const cache = new CombatLogCache();
    const same = () => {
      const n = new Names(game, a.projection);
      const state = cache.of(a.log, null, game, a.projection);
      expect(wordsOf(state.entries)).toEqual(wordsOf(build(a.log, n)));
      expect(state.latest?.record.id).toBe(latest(build(a.log, n))?.record.id);
    };
    same();
    let seq = a.log.length;
    for (const p of shooting(second)) a.apply(ev(++seq, p, P1));
    same();
    a.apply(ev(++seq, undone(4), P1));
    same();
    expect(cache.of(a.log, null, game, a.projection).latest!.record.id).toBe(Roll);
  });

  it.todo('a pending undo plans over the log with its marker');
});
