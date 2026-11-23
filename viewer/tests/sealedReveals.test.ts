import { describe, expect, it } from 'vitest';
import { answer } from '../src/lib/play/logText';
import { Names } from '../src/lib/play/names';
import { SealedReveals } from '../src/lib/play/sealedReveals';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { TEAM1, TEAM2, askOf, ev, gameOf, option } from './play';

const n = new Names(gameOf(), null);
const sealed = askOf({ kind: 'options', choices: [option('a', 'A')] }, { teams: [TEAM1, TEAM2], sealed: true });
const next = askOf({ kind: 'options', choices: [option('a', 'A')] });
const batch = (seq: number, ...p: EventPayload[]) => p.map((x, i) => ev(seq + i, x));
const passed = { kind: 'passed' } as const;
const closed = (id: string, ...teams: string[]): EventPayload => ({ kind: 'ask_closed', askId: id, answers: teams.map((teamId) => ({ teamId, answer: passed })) });
const chip = (team: string) => ({ ordinal: team === TEAM1 ? 1 : 2, team: n.team(team), choice: answer(passed, n) });

describe('SealedReveals', () => {
  it('a sealed close and my next card in one frame show no reveal on the card', () => {
    const r = new SealedReveals();
    r.read(batch(1, { kind: 'ask_opened', ask: sealed }), n);
    expect(r.shown(false)).toBeNull();
    r.read(batch(2, closed(sealed.id, TEAM1, TEAM2)), n);
    r.read(batch(3, { kind: 'ask_opened', ask: next }), n);
    expect(r.shown(true)).toBeNull();
    expect(r.shown(true)).toBeNull();
    expect(r.shown(false)).toBeNull();
  });

  it('a reveal with no card stays until my next card clears it', () => {
    const r = new SealedReveals();
    r.read(batch(1, { kind: 'ask_opened', ask: sealed }, closed(sealed.id, TEAM1, TEAM2)), n);
    expect(r.shown(false)).toEqual([[chip(TEAM1), chip(TEAM2)]]);
    expect(r.shown(false)).toBe(r.shown(false));
    expect(r.shown(true)).toBeNull();
    expect(r.shown(false)).toBeNull();
  });

  it('reveals queue in order and an unsealed or unanswered close reveals nothing', () => {
    const r = new SealedReveals();
    const second = { ...sealed, id: 'second' };
    r.read(batch(1, { kind: 'ask_opened', ask: sealed }, { kind: 'ask_opened', ask: second }, { kind: 'ask_opened', ask: next }), n);
    r.read(batch(4, closed(next.id, TEAM1), closed(second.id, TEAM2)), n);
    r.read(batch(6, closed(sealed.id, TEAM1)), n);
    expect(r.shown(false)).toEqual([[chip(TEAM2)], [chip(TEAM1)]]);
    const unanswered = { ...sealed, id: 'third' };
    expect(r.shown(true)).toBeNull();
    r.read(batch(7, { kind: 'ask_opened', ask: unanswered }, closed(unanswered.id)), n);
    expect(r.shown(false)).toBeNull();
  });

  it('a sealed choice reveals the option chosen', () => {
    const r = new SealedReveals();
    r.read(batch(1, { kind: 'ask_opened', ask: sealed }, { kind: 'ask_closed', askId: sealed.id, answers: [{ teamId: TEAM2, answer: { kind: 'chosen', optionId: 'continue' } }] }), n);
    expect(r.shown(false)).toEqual([[{ ordinal: 2, team: n.team(TEAM2), choice: 'continue' }]]);
  });
});
